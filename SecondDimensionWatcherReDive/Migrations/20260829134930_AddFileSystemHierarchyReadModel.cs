using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecondDimensionWatcherReDive.Migrations
{
    /// <inheritdoc />
    public partial class AddFileSystemHierarchyReadModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The deployment migration lease (SDWMIGR1) is acquired by the migration
            // hook before EF runs this migration. Inside that lease, take the same
            // namespace lock as every application writer before taking table locks.
            migrationBuilder.Sql(
                """
                SELECT pg_advisory_xact_lock(6000016614559404081);
                LOCK TABLE "FileMappings" IN SHARE ROW EXCLUSIVE MODE;
                LOCK TABLE "MetadataReviewMappingSnapshots" IN SHARE ROW EXCLUSIVE MODE;

                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM "FileMappings"
                        WHERE "VirtualPath" !~ '^/[^/]+(?:/[^/]+)*$'
                           OR "VirtualPath" ~ '(^|/)\.\.?($|/)') THEN
                        RAISE EXCEPTION 'FileMappings contains a non-canonical virtual path; repair it before migrating';
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM "MetadataReviewMappingSnapshots"
                        WHERE "VirtualPath" !~ '^/[^/]+(?:/[^/]+)*$'
                           OR "VirtualPath" ~ '(^|/)\.\.?($|/)') THEN
                        RAISE EXCEPTION 'MetadataReviewMappingSnapshots contains a non-canonical virtual path; repair it before migrating';
                    END IF;
                END $$;

                CREATE TEMP TABLE sdw_conflicting_mappings ON COMMIT DROP AS
                WITH conflicts AS (
                    SELECT
                        mapping."Id",
                        mapping."AnimationInfoId",
                        mapping."VirtualPath" AS old_path,
                        CASE
                            WHEN regexp_replace(mapping."VirtualPath", '/[^/]+$', '') = '' THEN '/'
                            ELSE regexp_replace(mapping."VirtualPath", '/[^/]+$', '')
                        END AS parent_path,
                        regexp_replace(mapping."VirtualPath", '^.*/', '') AS file_name
                    FROM "FileMappings" AS mapping
                    WHERE EXISTS (
                        SELECT 1
                        FROM "FileMappings" AS descendant
                        WHERE descendant."Id" <> mapping."Id"
                          AND left(descendant."VirtualPath", length(mapping."VirtualPath") + 1)
                              = mapping."VirtualPath" || '/')
                ), parsed AS (
                    SELECT
                        conflicts.*,
                        CASE
                            WHEN file_name ~ '^.+\.[^.]*$'
                                THEN substring(file_name FROM '(\.[^.]*)$')
                            ELSE ''
                        END AS extension,
                        CASE
                            WHEN file_name ~ '^.+\.[^.]*$'
                                THEN left(
                                    file_name,
                                    length(file_name) - length(substring(file_name FROM '(\.[^.]*)$')))
                            ELSE file_name
                        END AS suffixed_stem
                    FROM conflicts
                )
                SELECT
                    parsed.*,
                    regexp_replace(suffixed_stem, ' \([0-9]{1,9}\)$', '') AS base_stem,
                    NULL::text AS candidate_path
                FROM parsed;

                CREATE INDEX IX_sdw_conflicting_mappings_group
                    ON sdw_conflicting_mappings (parent_path, base_stem, extension);

                CREATE TEMP TABLE sdw_occupied_nodes ON COMMIT DROP AS
                WITH source_paths AS (
                    SELECT "VirtualPath" AS path FROM "FileMappings"
                    UNION
                    SELECT "VirtualPath" AS path FROM "MetadataReviewMappingSnapshots"
                ), path_segments AS (
                    SELECT
                        path,
                        string_to_array(trim(BOTH '/' FROM path), '/') AS segments
                    FROM source_paths
                )
                SELECT path
                FROM source_paths
                UNION
                SELECT '/' || array_to_string(segments[1:depth], '/')
                FROM path_segments
                CROSS JOIN LATERAL generate_series(1, cardinality(segments) - 1) AS depth;

                CREATE UNIQUE INDEX IX_sdw_occupied_nodes_path
                    ON sdw_occupied_nodes (path);

                WITH occupied_names AS (
                    SELECT
                        CASE
                            WHEN regexp_replace(path, '/[^/]+$', '') = '' THEN '/'
                            ELSE regexp_replace(path, '/[^/]+$', '')
                        END AS parent_path,
                        regexp_replace(path, '^.*/', '') AS file_name
                    FROM sdw_occupied_nodes
                ), occupied_parsed AS (
                    SELECT
                        parent_path,
                        CASE
                            WHEN file_name ~ '^.+\.[^.]*$'
                                THEN substring(file_name FROM '(\.[^.]*)$')
                            ELSE ''
                        END AS extension,
                        CASE
                            WHEN file_name ~ '^.+\.[^.]*$'
                                THEN left(
                                    file_name,
                                    length(file_name) - length(substring(file_name FROM '(\.[^.]*)$')))
                            ELSE file_name
                        END AS suffixed_stem
                    FROM occupied_names
                ), occupied_suffixes AS (
                    SELECT
                        parent_path,
                        regexp_replace(suffixed_stem, ' \([0-9]{1,9}\)$', '') AS base_stem,
                        extension,
                        COALESCE(
                            substring(suffixed_stem FROM ' \(([0-9]{1,9})\)$')::bigint,
                            1) AS suffix_number
                    FROM occupied_parsed
                ), group_maxima AS (
                    SELECT parent_path, base_stem, extension, max(suffix_number) AS max_suffix
                    FROM occupied_suffixes
                    GROUP BY parent_path, base_stem, extension
                ), ranked_conflicts AS (
                    SELECT
                        conflict."Id",
                        conflict.parent_path,
                        conflict.base_stem,
                        conflict.extension,
                        group_maxima.max_suffix
                            + row_number() OVER (
                                PARTITION BY conflict.parent_path, conflict.base_stem, conflict.extension
                                ORDER BY conflict.old_path COLLATE "C", conflict."Id") AS allocated_suffix
                    FROM sdw_conflicting_mappings AS conflict
                    JOIN group_maxima
                      ON group_maxima.parent_path = conflict.parent_path
                     AND group_maxima.base_stem = conflict.base_stem
                     AND group_maxima.extension = conflict.extension
                )
                UPDATE sdw_conflicting_mappings AS conflict
                SET candidate_path =
                    CASE
                        WHEN ranked.parent_path = '/' THEN '/'
                        ELSE ranked.parent_path || '/'
                    END
                    || ranked.base_stem || ' (' || ranked.allocated_suffix || ')'
                    || ranked.extension
                FROM ranked_conflicts AS ranked
                WHERE ranked."Id" = conflict."Id";

                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM sdw_conflicting_mappings
                        WHERE candidate_path IS NULL) THEN
                        RAISE EXCEPTION 'Could not allocate a replacement virtual path';
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM sdw_conflicting_mappings AS replacement
                        JOIN sdw_occupied_nodes AS occupied
                          ON occupied.path = replacement.candidate_path) THEN
                        RAISE EXCEPTION 'Replacement virtual path allocation collided with an occupied node';
                    END IF;
                END $$;

                CREATE TEMP TABLE sdw_progress_remap ON COMMIT DROP AS
                SELECT
                    progress."Id",
                    progress."UserId",
                    progress."AnimationInfoId",
                    replacement.candidate_path AS "VirtualPath",
                    progress."PositionSeconds",
                    progress."DurationSeconds",
                    progress."IsWatched",
                    progress."UpdatedAt",
                    progress."WatchedAt"
                FROM "PlaybackProgresses" AS progress
                JOIN sdw_conflicting_mappings AS replacement
                  ON replacement."AnimationInfoId" = progress."AnimationInfoId"
                 AND replacement.old_path = progress."VirtualPath";

                DELETE FROM "PlaybackProgresses" AS progress
                USING sdw_progress_remap AS replacement
                WHERE progress."Id" = replacement."Id";

                INSERT INTO "PlaybackProgresses"
                    ("Id", "UserId", "AnimationInfoId", "VirtualPath",
                     "PositionSeconds", "DurationSeconds", "IsWatched", "UpdatedAt", "WatchedAt")
                SELECT
                    "Id", "UserId", "AnimationInfoId", "VirtualPath",
                    "PositionSeconds", "DurationSeconds", "IsWatched", "UpdatedAt", "WatchedAt"
                FROM sdw_progress_remap
                ON CONFLICT ("UserId", "AnimationInfoId", "VirtualPath") DO UPDATE
                SET "PositionSeconds" = EXCLUDED."PositionSeconds",
                    "DurationSeconds" = EXCLUDED."DurationSeconds",
                    "IsWatched" = EXCLUDED."IsWatched",
                    "UpdatedAt" = EXCLUDED."UpdatedAt",
                    "WatchedAt" = EXCLUDED."WatchedAt"
                WHERE EXCLUDED."UpdatedAt" > "PlaybackProgresses"."UpdatedAt"
                   OR (EXCLUDED."UpdatedAt" = "PlaybackProgresses"."UpdatedAt"
                       AND EXCLUDED."IsWatched" AND NOT "PlaybackProgresses"."IsWatched");

                UPDATE "MetadataReviewMappingSnapshots" AS snapshot
                SET "VirtualPath" = replacement.candidate_path
                FROM "MetadataReviewOperations" AS operation,
                     sdw_conflicting_mappings AS replacement
                WHERE snapshot."OperationId" = operation."Id"
                  AND operation."AnimationInfoId" = replacement."AnimationInfoId"
                  AND snapshot."VirtualPath" = replacement.old_path;

                UPDATE "FileMappings" AS mapping
                SET "VirtualPath" = replacement.candidate_path
                FROM sdw_conflicting_mappings AS replacement
                WHERE mapping."Id" = replacement."Id";
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_FileMappings_VirtualPath_Canonical",
                table: "FileMappings",
                sql: "\"VirtualPath\" ~ '^/[^/]+(?:/[^/]+)*$' AND \"VirtualPath\" !~ '(^|/)\\.\\.?($|/)'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MetadataReviewMappingSnapshots_VirtualPath_Canonical",
                table: "MetadataReviewMappingSnapshots",
                sql: "\"VirtualPath\" ~ '^/[^/]+(?:/[^/]+)*$' AND \"VirtualPath\" !~ '(^|/)\\.\\.?($|/)'");

            migrationBuilder.Sql(
                "CREATE SEQUENCE sdw_file_system_entry_cookie_seq AS bigint START WITH 1;");

            migrationBuilder.CreateTable(
                name: "FileSystemDirectoryStates",
                columns: table => new
                {
                    Path = table.Column<string>(type: "text", nullable: false),
                    Generation = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileSystemDirectoryStates", x => x.Path);
                    table.CheckConstraint(
                        "CK_FileSystemDirectoryStates_Generation_Positive",
                        "\"Generation\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "FileSystemEntries",
                columns: table => new
                {
                    Path = table.Column<string>(type: "text", nullable: false),
                    EntryId = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    ParentPath = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    IsDirectory = table.Column<bool>(type: "boolean", nullable: false),
                    DescendantFileCount = table.Column<int>(type: "integer", nullable: false),
                    Cookie = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('sdw_file_system_entry_cookie_seq')"),
                    FileMappingId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileSystemEntries", x => x.Path);
                    table.CheckConstraint(
                        "CK_FileSystemEntries_NodeShape",
                        "(\"IsDirectory\" AND \"FileMappingId\" IS NULL AND \"DescendantFileCount\" > 0) OR " +
                        "(NOT \"IsDirectory\" AND \"FileMappingId\" IS NOT NULL AND \"DescendantFileCount\" = 1)");
                    table.ForeignKey(
                        name: "FK_FileSystemEntries_FileMappings_FileMappingId",
                        column: x => x.FileMappingId,
                        principalTable: "FileMappings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FileSystemEntries_Cookie",
                table: "FileSystemEntries",
                column: "Cookie",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FileSystemEntries_EntryId",
                table: "FileSystemEntries",
                column: "EntryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FileSystemEntries_FileMappingId",
                table: "FileSystemEntries",
                column: "FileMappingId",
                unique: true,
                filter: "\"FileMappingId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FileSystemEntries_ParentPath_Cookie",
                table: "FileSystemEntries",
                columns: new[] { "ParentPath", "Cookie" });

            migrationBuilder.CreateIndex(
                name: "IX_FileSystemEntries_ParentPath_IsDirectory_Name",
                table: "FileSystemEntries",
                columns: new[] { "ParentPath", "IsDirectory", "Name" });

            migrationBuilder.Sql(
                """
                INSERT INTO "FileSystemDirectoryStates" ("Path", "Generation")
                VALUES ('/', 1);

                WITH mapping_segments AS (
                    SELECT string_to_array(trim(BOTH '/' FROM mapping."VirtualPath"), '/') AS segments
                    FROM "FileMappings" AS mapping
                ), directory_occurrences AS (
                    SELECT
                        '/' || array_to_string(segments[1:depth], '/') AS path,
                        CASE
                            WHEN depth = 1 THEN '/'
                            ELSE '/' || array_to_string(segments[1:depth - 1], '/')
                        END AS parent_path,
                        segments[depth] AS name
                    FROM mapping_segments
                    CROSS JOIN LATERAL generate_series(1, cardinality(segments) - 1) AS depth
                ), directories AS (
                    SELECT path, parent_path, name, count(*)::integer AS file_count
                    FROM directory_occurrences
                    GROUP BY path, parent_path, name
                )
                INSERT INTO "FileSystemEntries"
                    ("Path", "ParentPath", "Name", "IsDirectory", "DescendantFileCount", "FileMappingId")
                SELECT path, parent_path, name, TRUE, file_count, NULL
                FROM directories;

                INSERT INTO "FileSystemDirectoryStates" ("Path", "Generation")
                SELECT "Path", 1
                FROM "FileSystemEntries"
                WHERE "IsDirectory";

                INSERT INTO "FileSystemEntries"
                    ("Path", "ParentPath", "Name", "IsDirectory", "DescendantFileCount", "FileMappingId")
                SELECT
                    mapping."VirtualPath",
                    CASE
                        WHEN regexp_replace(mapping."VirtualPath", '/[^/]+$', '') = '' THEN '/'
                        ELSE regexp_replace(mapping."VirtualPath", '/[^/]+$', '')
                    END,
                    regexp_replace(mapping."VirtualPath", '^.*/', ''),
                    FALSE,
                    1,
                    mapping."Id"
                FROM "FileMappings" AS mapping;
                """);

            migrationBuilder.Sql(
                """
                CREATE FUNCTION sdw_file_mapping_namespace_lock() RETURNS trigger AS $$
                BEGIN
                    PERFORM pg_advisory_xact_lock(6000016614559404081);
                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;

                CREATE FUNCTION sdw_bump_directory_generation(directory_path text) RETURNS void AS $$
                DECLARE
                    affected integer;
                BEGIN
                    UPDATE "FileSystemDirectoryStates"
                    SET "Generation" = "Generation" + 1
                    WHERE "Path" = directory_path
                      AND "Generation" < 9223372036854775807;
                    GET DIAGNOSTICS affected = ROW_COUNT;
                    IF affected <> 1 THEN
                        RAISE EXCEPTION 'Missing or exhausted hierarchy generation for directory: %', directory_path;
                    END IF;
                END;
                $$ LANGUAGE plpgsql;

                CREATE FUNCTION sdw_file_system_entry_insert() RETURNS trigger AS $$
                DECLARE
                    segments text[];
                    segment_count integer;
                    depth integer;
                    directory_path text;
                    parent_path text;
                    affected integer;
                    directory_created integer;
                BEGIN
                    IF NEW."VirtualPath" !~ '^/[^/]+(?:/[^/]+)*$'
                       OR NEW."VirtualPath" ~ '(^|/)\.\.?($|/)' THEN
                        RAISE EXCEPTION 'Invalid virtual path: %', NEW."VirtualPath";
                    END IF;

                    segments := string_to_array(trim(BOTH '/' FROM NEW."VirtualPath"), '/');
                    segment_count := cardinality(segments);

                    FOR depth IN 1..segment_count - 1 LOOP
                        directory_path := '/' || array_to_string(segments[1:depth], '/');
                        parent_path := CASE
                            WHEN depth = 1 THEN '/'
                            ELSE '/' || array_to_string(segments[1:depth - 1], '/')
                        END;

                        INSERT INTO "FileSystemEntries"
                            ("Path", "ParentPath", "Name", "IsDirectory", "DescendantFileCount", "FileMappingId")
                        VALUES
                            (directory_path, parent_path, segments[depth], TRUE, 1, NULL)
                        ON CONFLICT ("Path") DO UPDATE
                            SET "DescendantFileCount" = "FileSystemEntries"."DescendantFileCount" + 1
                            WHERE "FileSystemEntries"."IsDirectory";
                        GET DIAGNOSTICS affected = ROW_COUNT;
                        IF affected <> 1 THEN
                            RAISE EXCEPTION 'Virtual path is both a file and directory: %', directory_path;
                        END IF;

                        INSERT INTO "FileSystemDirectoryStates" ("Path", "Generation")
                        VALUES (directory_path, 1)
                        ON CONFLICT ("Path") DO NOTHING;
                        GET DIAGNOSTICS directory_created = ROW_COUNT;
                        IF directory_created = 1 THEN
                            PERFORM sdw_bump_directory_generation(parent_path);
                        END IF;
                    END LOOP;

                    parent_path := CASE
                        WHEN segment_count = 1 THEN '/'
                        ELSE '/' || array_to_string(segments[1:segment_count - 1], '/')
                    END;
                    INSERT INTO "FileSystemEntries"
                        ("Path", "ParentPath", "Name", "IsDirectory", "DescendantFileCount", "FileMappingId")
                    VALUES
                        (NEW."VirtualPath", parent_path, segments[segment_count], FALSE, 1, NEW."Id");
                    PERFORM sdw_bump_directory_generation(parent_path);
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE FUNCTION sdw_file_system_entry_delete() RETURNS trigger AS $$
                DECLARE
                    segments text[];
                    segment_count integer;
                    depth integer;
                    directory_path text;
                    parent_path text;
                    affected integer;
                BEGIN
                    segments := string_to_array(trim(BOTH '/' FROM OLD."VirtualPath"), '/');
                    segment_count := cardinality(segments);
                    parent_path := CASE
                        WHEN segment_count = 1 THEN '/'
                        ELSE '/' || array_to_string(segments[1:segment_count - 1], '/')
                    END;

                    DELETE FROM "FileSystemEntries"
                    WHERE "Path" = OLD."VirtualPath"
                      AND NOT "IsDirectory"
                      AND "FileMappingId" = OLD."Id";
                    GET DIAGNOSTICS affected = ROW_COUNT;
                    IF affected <> 1 THEN
                        RAISE EXCEPTION 'Missing hierarchy file during mapping delete: %', OLD."VirtualPath";
                    END IF;
                    PERFORM sdw_bump_directory_generation(parent_path);

                    FOR depth IN REVERSE segment_count - 1..1 LOOP
                        directory_path := '/' || array_to_string(segments[1:depth], '/');
                        parent_path := CASE
                            WHEN depth = 1 THEN '/'
                            ELSE '/' || array_to_string(segments[1:depth - 1], '/')
                        END;

                        DELETE FROM "FileSystemEntries"
                        WHERE "Path" = directory_path
                          AND "IsDirectory"
                          AND "DescendantFileCount" = 1;
                        GET DIAGNOSTICS affected = ROW_COUNT;
                        IF affected = 1 THEN
                            DELETE FROM "FileSystemDirectoryStates"
                            WHERE "Path" = directory_path;
                            GET DIAGNOSTICS affected = ROW_COUNT;
                            IF affected <> 1 THEN
                                RAISE EXCEPTION 'Missing generation state during directory delete: %', directory_path;
                            END IF;
                            PERFORM sdw_bump_directory_generation(parent_path);
                        ELSE
                            UPDATE "FileSystemEntries"
                            SET "DescendantFileCount" = "DescendantFileCount" - 1
                            WHERE "Path" = directory_path
                              AND "IsDirectory"
                              AND "DescendantFileCount" > 1;
                            GET DIAGNOSTICS affected = ROW_COUNT;
                            IF affected <> 1 THEN
                                RAISE EXCEPTION 'Missing hierarchy directory during mapping delete: %', directory_path;
                            END IF;
                        END IF;
                    END LOOP;
                    RETURN OLD;
                END;
                $$ LANGUAGE plpgsql;

                CREATE FUNCTION sdw_file_system_entry_reject_path_update() RETURNS trigger AS $$
                BEGIN
                    IF OLD."VirtualPath" IS DISTINCT FROM NEW."VirtualPath" THEN
                        RAISE EXCEPTION 'VirtualPath updates must use an atomic delete-and-insert remap';
                    END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER "TR_FileMappings_00_NamespaceLock"
                BEFORE INSERT OR DELETE OR UPDATE ON "FileMappings"
                FOR EACH STATEMENT EXECUTE FUNCTION sdw_file_mapping_namespace_lock();

                CREATE TRIGGER "TR_FileMappings_HierarchyInsert"
                AFTER INSERT ON "FileMappings"
                FOR EACH ROW EXECUTE FUNCTION sdw_file_system_entry_insert();

                CREATE TRIGGER "TR_FileMappings_HierarchyDelete"
                BEFORE DELETE ON "FileMappings"
                FOR EACH ROW EXECUTE FUNCTION sdw_file_system_entry_delete();

                CREATE TRIGGER "TR_FileMappings_HierarchyRejectPathUpdate"
                BEFORE UPDATE OF "VirtualPath" ON "FileMappings"
                FOR EACH ROW EXECUTE FUNCTION sdw_file_system_entry_reject_path_update();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS "TR_FileMappings_00_NamespaceLock" ON "FileMappings";
                DROP TRIGGER IF EXISTS "TR_FileMappings_HierarchyInsert" ON "FileMappings";
                DROP TRIGGER IF EXISTS "TR_FileMappings_HierarchyDelete" ON "FileMappings";
                DROP TRIGGER IF EXISTS "TR_FileMappings_HierarchyRejectPathUpdate" ON "FileMappings";
                DROP FUNCTION IF EXISTS sdw_file_mapping_namespace_lock();
                DROP FUNCTION IF EXISTS sdw_bump_directory_generation(text);
                DROP FUNCTION IF EXISTS sdw_file_system_entry_insert();
                DROP FUNCTION IF EXISTS sdw_file_system_entry_delete();
                DROP FUNCTION IF EXISTS sdw_file_system_entry_reject_path_update();
                """);

            migrationBuilder.DropTable(name: "FileSystemEntries");
            migrationBuilder.DropTable(name: "FileSystemDirectoryStates");
            migrationBuilder.Sql("DROP SEQUENCE IF EXISTS sdw_file_system_entry_cookie_seq;");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FileMappings_VirtualPath_Canonical",
                table: "FileMappings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MetadataReviewMappingSnapshots_VirtualPath_Canonical",
                table: "MetadataReviewMappingSnapshots");
        }
    }
}
