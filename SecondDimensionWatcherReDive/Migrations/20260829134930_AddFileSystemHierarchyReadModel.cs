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
