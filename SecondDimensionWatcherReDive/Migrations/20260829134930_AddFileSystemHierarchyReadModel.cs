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
            // The legacy unique index allowed a file path to also be the prefix of
            // another mapping. Normalize those ambiguous file nodes before building
            // the hierarchy, preserving playback and metadata-review history for the
            // remapped physical file. Shallow conflicts are handled first so a
            // descendant always has a real directory parent by the time it is checked.
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE
                    conflicting record;
                    old_path text;
                    candidate_path text;
                    directory_path text;
                    file_name text;
                    stem text;
                    extension text;
                    existing_suffix text;
                    suffix_number bigint;
                BEGIN
                    LOOP
                        SELECT mapping.* INTO conflicting
                        FROM "FileMappings" AS mapping
                        WHERE EXISTS (
                            SELECT 1
                            FROM "FileMappings" AS descendant
                            WHERE descendant."Id" <> mapping."Id"
                              AND left(
                                    descendant."VirtualPath",
                                    length(mapping."VirtualPath") + 1)
                                  = mapping."VirtualPath" || '/')
                        ORDER BY cardinality(string_to_array(
                                     trim(BOTH '/' FROM mapping."VirtualPath"), '/')),
                                 mapping."VirtualPath",
                                 mapping."Id"
                        LIMIT 1;
                        EXIT WHEN NOT FOUND;

                        old_path := conflicting."VirtualPath";
                        directory_path := regexp_replace(old_path, '/[^/]+$', '');
                        file_name := regexp_replace(old_path, '^.*/', '');
                        IF file_name ~ '^.+\.[^.]*$' THEN
                            extension := substring(file_name FROM '(\.[^.]*)$');
                            stem := left(file_name, length(file_name) - length(extension));
                        ELSE
                            extension := '';
                            stem := file_name;
                        END IF;
                        existing_suffix := substring(stem FROM ' \(([0-9]{1,9})\)$');
                        IF existing_suffix IS NULL THEN
                            suffix_number := 2;
                        ELSE
                            suffix_number := existing_suffix::bigint + 1;
                            stem := regexp_replace(stem, ' \([0-9]{1,9}\)$', '');
                        END IF;

                        LOOP
                            candidate_path := directory_path || '/' || stem ||
                                ' (' || suffix_number || ')' || extension;
                            EXIT WHEN NOT EXISTS (
                                SELECT 1
                                FROM "FileMappings" AS occupied
                                WHERE occupied."Id" <> conflicting."Id"
                                  AND (occupied."VirtualPath" = candidate_path
                                       OR left(
                                            occupied."VirtualPath",
                                            length(candidate_path) + 1)
                                          = candidate_path || '/'))
                              AND NOT EXISTS (
                                SELECT 1
                                FROM "MetadataReviewMappingSnapshots" AS snapshot
                                WHERE snapshot."VirtualPath" = candidate_path);
                            suffix_number := suffix_number + 1;
                        END LOOP;

                        UPDATE "PlaybackProgresses" AS target
                        SET "PositionSeconds" = source."PositionSeconds",
                            "DurationSeconds" = source."DurationSeconds",
                            "IsWatched" = source."IsWatched",
                            "UpdatedAt" = source."UpdatedAt",
                            "WatchedAt" = source."WatchedAt"
                        FROM "PlaybackProgresses" AS source
                        WHERE source."AnimationInfoId" = conflicting."AnimationInfoId"
                          AND source."VirtualPath" = old_path
                          AND target."AnimationInfoId" = source."AnimationInfoId"
                          AND target."UserId" = source."UserId"
                          AND target."VirtualPath" = candidate_path
                          AND (source."UpdatedAt" > target."UpdatedAt"
                               OR (source."UpdatedAt" = target."UpdatedAt"
                                   AND source."IsWatched" AND NOT target."IsWatched"));

                        DELETE FROM "PlaybackProgresses" AS source
                        USING "PlaybackProgresses" AS target
                        WHERE source."AnimationInfoId" = conflicting."AnimationInfoId"
                          AND source."VirtualPath" = old_path
                          AND target."AnimationInfoId" = source."AnimationInfoId"
                          AND target."UserId" = source."UserId"
                          AND target."VirtualPath" = candidate_path;

                        UPDATE "PlaybackProgresses"
                        SET "VirtualPath" = candidate_path
                        WHERE "AnimationInfoId" = conflicting."AnimationInfoId"
                          AND "VirtualPath" = old_path;

                        UPDATE "MetadataReviewMappingSnapshots" AS snapshot
                        SET "VirtualPath" = candidate_path
                        FROM "MetadataReviewOperations" AS operation
                        WHERE snapshot."OperationId" = operation."Id"
                          AND operation."AnimationInfoId" = conflicting."AnimationInfoId"
                          AND snapshot."VirtualPath" = old_path
                          AND snapshot."PhysicalPath" = conflicting."PhysicalPath"
                          AND snapshot."FileStore" = conflicting."FileStore";

                        UPDATE "FileMappings"
                        SET "VirtualPath" = candidate_path
                        WHERE "Id" = conflicting."Id";
                    END LOOP;
                END $$;
                """);

            migrationBuilder.CreateTable(
                name: "FileSystemEntries",
                columns: table => new
                {
                    Path = table.Column<string>(type: "text", nullable: false),
                    ParentPath = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    IsDirectory = table.Column<bool>(type: "boolean", nullable: false),
                    DescendantFileCount = table.Column<int>(type: "integer", nullable: false),
                    FileMappingId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileSystemEntries", x => x.Path);
                    table.CheckConstraint("CK_FileSystemEntries_NodeShape", "(\"IsDirectory\" AND \"FileMappingId\" IS NULL AND \"DescendantFileCount\" > 0) OR (NOT \"IsDirectory\" AND \"FileMappingId\" IS NOT NULL AND \"DescendantFileCount\" = 1)");
                    table.ForeignKey(
                        name: "FK_FileSystemEntries_FileMappings_FileMappingId",
                        column: x => x.FileMappingId,
                        principalTable: "FileMappings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FileSystemEntries_FileMappingId",
                table: "FileSystemEntries",
                column: "FileMappingId",
                unique: true,
                filter: "\"FileMappingId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FileSystemEntries_ParentPath_IsDirectory_Name",
                table: "FileSystemEntries",
                columns: new[] { "ParentPath", "IsDirectory", "Name" });

            // Backfill is set-based and runs in the schema migration transaction. A
            // failure therefore rolls back cleanly and the migration can be retried.
            migrationBuilder.Sql(
                """
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
                )
                INSERT INTO "FileSystemEntries"
                    ("Path", "ParentPath", "Name", "IsDirectory", "DescendantFileCount", "FileMappingId")
                SELECT path, parent_path, name, TRUE, count(*)::integer, NULL
                FROM directory_occurrences
                GROUP BY path, parent_path, name;
                """);

            // Triggers keep the read model in the same transaction as every mapping
            // writer, including imports and metadata-review undo/redo paths.
            migrationBuilder.Sql(
                """
                CREATE FUNCTION sdw_file_system_entry_insert() RETURNS trigger AS $$
                DECLARE
                    segments text[];
                    segment_count integer;
                    depth integer;
                    directory_path text;
                    parent_path text;
                    affected integer;
                BEGIN
                    IF NEW."VirtualPath" !~ '^/[^/]+(?:/[^/]+)*$' THEN
                        RAISE EXCEPTION 'Invalid virtual path: %', NEW."VirtualPath";
                    END IF;

                    segments := string_to_array(trim(BOTH '/' FROM NEW."VirtualPath"), '/');
                    segment_count := cardinality(segments);
                    parent_path := CASE
                        WHEN segment_count = 1 THEN '/'
                        ELSE '/' || array_to_string(segments[1:segment_count - 1], '/')
                    END;

                    INSERT INTO "FileSystemEntries"
                        ("Path", "ParentPath", "Name", "IsDirectory", "DescendantFileCount", "FileMappingId")
                    VALUES
                        (NEW."VirtualPath", parent_path, segments[segment_count], FALSE, 1, NEW."Id");

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
                    END LOOP;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE FUNCTION sdw_file_system_entry_delete() RETURNS trigger AS $$
                DECLARE
                    segments text[];
                    segment_count integer;
                    depth integer;
                    directory_path text;
                    affected integer;
                BEGIN
                    segments := string_to_array(trim(BOTH '/' FROM OLD."VirtualPath"), '/');
                    segment_count := cardinality(segments);

                    FOR depth IN REVERSE segment_count - 1..1 LOOP
                        directory_path := '/' || array_to_string(segments[1:depth], '/');
                        DELETE FROM "FileSystemEntries"
                        WHERE "Path" = directory_path
                          AND "IsDirectory"
                          AND "DescendantFileCount" = 1;
                        GET DIAGNOSTICS affected = ROW_COUNT;
                        IF affected = 0 THEN
                            UPDATE "FileSystemEntries"
                            SET "DescendantFileCount" = "DescendantFileCount" - 1
                            WHERE "Path" = directory_path
                              AND "IsDirectory"
                              AND "DescendantFileCount" > 1;
                            GET DIAGNOSTICS affected = ROW_COUNT;
                        END IF;
                        IF affected <> 1 THEN
                            RAISE EXCEPTION 'Missing hierarchy directory during mapping delete: %', directory_path;
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

                CREATE TRIGGER "TR_FileMappings_HierarchyInsert"
                AFTER INSERT ON "FileMappings"
                FOR EACH ROW EXECUTE FUNCTION sdw_file_system_entry_insert();

                CREATE TRIGGER "TR_FileMappings_HierarchyDelete"
                AFTER DELETE ON "FileMappings"
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
                DROP TRIGGER IF EXISTS "TR_FileMappings_HierarchyInsert" ON "FileMappings";
                DROP TRIGGER IF EXISTS "TR_FileMappings_HierarchyDelete" ON "FileMappings";
                DROP TRIGGER IF EXISTS "TR_FileMappings_HierarchyRejectPathUpdate" ON "FileMappings";
                DROP FUNCTION IF EXISTS sdw_file_system_entry_insert();
                DROP FUNCTION IF EXISTS sdw_file_system_entry_delete();
                DROP FUNCTION IF EXISTS sdw_file_system_entry_reject_path_update();
                """);

            migrationBuilder.DropTable(
                name: "FileSystemEntries");
        }
    }
}
