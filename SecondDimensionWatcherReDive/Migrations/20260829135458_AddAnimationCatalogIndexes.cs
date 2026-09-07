using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecondDimensionWatcherReDive.Migrations
{
    /// <inheritdoc />
    public partial class AddAnimationCatalogIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AnimationInfo_AnimationId",
                table: "AnimationInfo");

            migrationBuilder.CreateIndex(
                name: "IX_AnimationInfo_AnimationId_PublishTime_Id",
                table: "AnimationInfo",
                columns: new[] { "AnimationId", "PublishTime", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AnimationInfo_MediaLibraryMissingSince_PublishTime_Id",
                table: "AnimationInfo",
                columns: new[] { "MediaLibraryMissingSince", "PublishTime", "Id" });

            migrationBuilder.CreateTable(
                name: "AnimationCatalogStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnimationCatalogStates", x => x.Id);
                    table.CheckConstraint(
                        "CK_AnimationCatalogStates_Revision_Positive",
                        "\"Revision\" > 0");
                    table.CheckConstraint(
                        "CK_AnimationCatalogStates_Singleton",
                        "\"Id\" = 1");
                });

            migrationBuilder.CreateTable(
                name: "AnimationCatalogEntries",
                columns: table => new
                {
                    AnimationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TmdbId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    OriginalName = table.Column<string>(type: "text", nullable: false),
                    PosterPath = table.Column<string>(type: "text", nullable: true),
                    EpisodeCount = table.Column<int>(type: "integer", nullable: false),
                    ReleaseCount = table.Column<int>(type: "integer", nullable: false),
                    AutomationAttentionCount = table.Column<int>(type: "integer", nullable: false),
                    LatestPublishTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnimationCatalogEntries", x => x.AnimationId);
                    table.CheckConstraint(
                        "CK_AnimationCatalogEntries_Counts",
                        "\"EpisodeCount\" >= 0 AND \"ReleaseCount\" > 0 AND \"AutomationAttentionCount\" >= 0");
                    table.ForeignKey(
                        name: "FK_AnimationCatalogEntries_Animations_AnimationId",
                        column: x => x.AnimationId,
                        principalTable: "Animations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnimationCatalogEntries_LatestPublishTime_TmdbId",
                table: "AnimationCatalogEntries",
                columns: new[] { "LatestPublishTime", "TmdbId" },
                descending: new[] { true, true });

            migrationBuilder.CreateIndex(
                name: "IX_AnimationCatalogEntries_TmdbId",
                table: "AnimationCatalogEntries",
                column: "TmdbId",
                unique: true);

            migrationBuilder.Sql(
                """
                INSERT INTO "AnimationCatalogStates" ("Id", "Revision")
                VALUES (1, 1);

                INSERT INTO "AnimationCatalogEntries"
                    ("AnimationId", "TmdbId", "Name", "OriginalName", "PosterPath",
                     "EpisodeCount", "ReleaseCount", "AutomationAttentionCount", "LatestPublishTime")
                SELECT
                    animation."Id",
                    animation."TmdbId",
                    animation."Name",
                    animation."OriginalName",
                    animation."PosterPath",
                    count(DISTINCT (info."Season", info."Episode"))
                        FILTER (WHERE info."Episode" IS NOT NULL)::integer,
                    count(*)::integer,
                    count(*) FILTER (
                        WHERE info."AutomationDisposition" IN
                            ('Notified', 'PendingConfirmation', 'AutoDownloadFailed'))::integer,
                    max(info."PublishTime")
                FROM "AnimationInfo" AS info
                JOIN "Animations" AS animation ON animation."Id" = info."AnimationId"
                WHERE info."MediaLibraryMissingSince" IS NULL
                GROUP BY animation."Id", animation."TmdbId", animation."Name",
                         animation."OriginalName", animation."PosterPath";

                CREATE FUNCTION sdw_bump_animation_catalog_revision() RETURNS void AS $$
                DECLARE
                    affected integer;
                BEGIN
                    UPDATE "AnimationCatalogStates"
                    SET "Revision" = "Revision" + 1
                    WHERE "Id" = 1
                      AND "Revision" < 9223372036854775807;
                    GET DIAGNOSTICS affected = ROW_COUNT;
                    IF affected <> 1 THEN
                        RAISE EXCEPTION 'Missing or exhausted animation catalog revision';
                    END IF;
                END;
                $$ LANGUAGE plpgsql;

                CREATE FUNCTION sdw_refresh_animation_catalog(animation_ids uuid[]) RETURNS void AS $$
                BEGIN
                    IF animation_ids IS NULL OR cardinality(animation_ids) = 0 THEN
                        RETURN;
                    END IF;

                    DELETE FROM "AnimationCatalogEntries"
                    WHERE "AnimationId" = ANY(animation_ids);

                    INSERT INTO "AnimationCatalogEntries"
                        ("AnimationId", "TmdbId", "Name", "OriginalName", "PosterPath",
                         "EpisodeCount", "ReleaseCount", "AutomationAttentionCount", "LatestPublishTime")
                    SELECT
                        animation."Id",
                        animation."TmdbId",
                        animation."Name",
                        animation."OriginalName",
                        animation."PosterPath",
                        count(DISTINCT (info."Season", info."Episode"))
                            FILTER (WHERE info."Episode" IS NOT NULL)::integer,
                        count(*)::integer,
                        count(*) FILTER (
                            WHERE info."AutomationDisposition" IN
                                ('Notified', 'PendingConfirmation', 'AutoDownloadFailed'))::integer,
                        max(info."PublishTime")
                    FROM "Animations" AS animation
                    JOIN "AnimationInfo" AS info ON info."AnimationId" = animation."Id"
                    WHERE animation."Id" = ANY(animation_ids)
                      AND info."MediaLibraryMissingSince" IS NULL
                    GROUP BY animation."Id", animation."TmdbId", animation."Name",
                             animation."OriginalName", animation."PosterPath";
                END;
                $$ LANGUAGE plpgsql;

                CREATE FUNCTION sdw_animation_info_catalog_insert() RETURNS trigger AS $$
                DECLARE
                    affected_ids uuid[];
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM new_rows) THEN
                        RETURN NULL;
                    END IF;
                    SELECT COALESCE(array_agg(DISTINCT "AnimationId")
                                        FILTER (WHERE "AnimationId" IS NOT NULL), ARRAY[]::uuid[])
                    INTO affected_ids
                    FROM new_rows;
                    PERFORM sdw_refresh_animation_catalog(affected_ids);
                    PERFORM sdw_bump_animation_catalog_revision();
                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;

                CREATE FUNCTION sdw_animation_info_catalog_update() RETURNS trigger AS $$
                DECLARE
                    affected_ids uuid[];
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM new_rows) THEN
                        RETURN NULL;
                    END IF;
                    SELECT COALESCE(array_agg(DISTINCT "AnimationId")
                                        FILTER (WHERE "AnimationId" IS NOT NULL), ARRAY[]::uuid[])
                    INTO affected_ids
                    FROM (
                        SELECT "AnimationId" FROM old_rows
                        UNION
                        SELECT "AnimationId" FROM new_rows
                    ) AS changed;
                    PERFORM sdw_refresh_animation_catalog(affected_ids);
                    PERFORM sdw_bump_animation_catalog_revision();
                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;

                CREATE FUNCTION sdw_animation_info_catalog_delete() RETURNS trigger AS $$
                DECLARE
                    affected_ids uuid[];
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM old_rows) THEN
                        RETURN NULL;
                    END IF;
                    SELECT COALESCE(array_agg(DISTINCT "AnimationId")
                                        FILTER (WHERE "AnimationId" IS NOT NULL), ARRAY[]::uuid[])
                    INTO affected_ids
                    FROM old_rows;
                    PERFORM sdw_refresh_animation_catalog(affected_ids);
                    PERFORM sdw_bump_animation_catalog_revision();
                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;

                CREATE FUNCTION sdw_animation_info_catalog_truncate() RETURNS trigger AS $$
                BEGIN
                    DELETE FROM "AnimationCatalogEntries";
                    PERFORM sdw_bump_animation_catalog_revision();
                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;

                CREATE FUNCTION sdw_animation_catalog_animation_update() RETURNS trigger AS $$
                DECLARE
                    affected_ids uuid[];
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM new_rows) THEN
                        RETURN NULL;
                    END IF;
                    SELECT array_agg(DISTINCT "Id") INTO affected_ids FROM new_rows;
                    PERFORM sdw_refresh_animation_catalog(affected_ids);
                    PERFORM sdw_bump_animation_catalog_revision();
                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;

                CREATE FUNCTION sdw_animation_catalog_animation_delete() RETURNS trigger AS $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM old_rows) THEN
                        PERFORM sdw_bump_animation_catalog_revision();
                    END IF;
                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;

                CREATE FUNCTION sdw_animation_catalog_group_update() RETURNS trigger AS $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM new_rows) THEN
                        -- Group names are projected into both categorized and
                        -- uncategorized episode pages even though they are not
                        -- stored in the compact catalog aggregate.
                        PERFORM sdw_bump_animation_catalog_revision();
                    END IF;
                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER "TR_AnimationInfo_CatalogInsert"
                AFTER INSERT ON "AnimationInfo"
                REFERENCING NEW TABLE AS new_rows
                FOR EACH STATEMENT EXECUTE FUNCTION sdw_animation_info_catalog_insert();

                CREATE TRIGGER "TR_AnimationInfo_CatalogUpdate"
                AFTER UPDATE ON "AnimationInfo"
                REFERENCING OLD TABLE AS old_rows NEW TABLE AS new_rows
                FOR EACH STATEMENT EXECUTE FUNCTION sdw_animation_info_catalog_update();

                CREATE TRIGGER "TR_AnimationInfo_CatalogDelete"
                AFTER DELETE ON "AnimationInfo"
                REFERENCING OLD TABLE AS old_rows
                FOR EACH STATEMENT EXECUTE FUNCTION sdw_animation_info_catalog_delete();

                CREATE TRIGGER "TR_AnimationInfo_CatalogTruncate"
                AFTER TRUNCATE ON "AnimationInfo"
                FOR EACH STATEMENT EXECUTE FUNCTION sdw_animation_info_catalog_truncate();

                CREATE TRIGGER "TR_Animations_CatalogUpdate"
                AFTER UPDATE ON "Animations"
                REFERENCING NEW TABLE AS new_rows
                FOR EACH STATEMENT EXECUTE FUNCTION sdw_animation_catalog_animation_update();

                CREATE TRIGGER "TR_Animations_CatalogDelete"
                AFTER DELETE ON "Animations"
                REFERENCING OLD TABLE AS old_rows
                FOR EACH STATEMENT EXECUTE FUNCTION sdw_animation_catalog_animation_delete();

                CREATE TRIGGER "TR_AnimationGroups_CatalogUpdate"
                AFTER UPDATE ON "AnimationGroups"
                REFERENCING NEW TABLE AS new_rows
                FOR EACH STATEMENT EXECUTE FUNCTION sdw_animation_catalog_group_update();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS "TR_AnimationInfo_CatalogInsert" ON "AnimationInfo";
                DROP TRIGGER IF EXISTS "TR_AnimationInfo_CatalogUpdate" ON "AnimationInfo";
                DROP TRIGGER IF EXISTS "TR_AnimationInfo_CatalogDelete" ON "AnimationInfo";
                DROP TRIGGER IF EXISTS "TR_AnimationInfo_CatalogTruncate" ON "AnimationInfo";
                DROP TRIGGER IF EXISTS "TR_Animations_CatalogUpdate" ON "Animations";
                DROP TRIGGER IF EXISTS "TR_Animations_CatalogDelete" ON "Animations";
                DROP TRIGGER IF EXISTS "TR_AnimationGroups_CatalogUpdate" ON "AnimationGroups";
                DROP FUNCTION IF EXISTS sdw_animation_info_catalog_insert();
                DROP FUNCTION IF EXISTS sdw_animation_info_catalog_update();
                DROP FUNCTION IF EXISTS sdw_animation_info_catalog_delete();
                DROP FUNCTION IF EXISTS sdw_animation_info_catalog_truncate();
                DROP FUNCTION IF EXISTS sdw_animation_catalog_animation_update();
                DROP FUNCTION IF EXISTS sdw_animation_catalog_animation_delete();
                DROP FUNCTION IF EXISTS sdw_animation_catalog_group_update();
                DROP FUNCTION IF EXISTS sdw_refresh_animation_catalog(uuid[]);
                DROP FUNCTION IF EXISTS sdw_bump_animation_catalog_revision();
                """);

            migrationBuilder.DropTable(name: "AnimationCatalogEntries");
            migrationBuilder.DropTable(name: "AnimationCatalogStates");

            migrationBuilder.DropIndex(
                name: "IX_AnimationInfo_AnimationId_PublishTime_Id",
                table: "AnimationInfo");

            migrationBuilder.DropIndex(
                name: "IX_AnimationInfo_MediaLibraryMissingSince_PublishTime_Id",
                table: "AnimationInfo");

            migrationBuilder.CreateIndex(
                name: "IX_AnimationInfo_AnimationId",
                table: "AnimationInfo",
                column: "AnimationId");
        }
    }
}
