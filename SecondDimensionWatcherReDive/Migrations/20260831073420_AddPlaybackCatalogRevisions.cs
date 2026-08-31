using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecondDimensionWatcherReDive.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaybackCatalogRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlaybackCatalogStates",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaybackCatalogStates", x => x.UserId);
                    table.CheckConstraint("CK_PlaybackCatalogStates_Revision_Positive", "\"Revision\" > 0");
                });

            migrationBuilder.Sql(
                """
                INSERT INTO "PlaybackCatalogStates" ("UserId", "Revision")
                SELECT DISTINCT "UserId", 1
                FROM "PlaybackProgresses"
                WHERE "IsWatched" OR "PositionSeconds" > 0;

                CREATE FUNCTION sdw_bump_playback_catalog_revisions(user_ids uuid[]) RETURNS void AS $$
                DECLARE
                    expected integer;
                    affected integer;
                BEGIN
                    IF user_ids IS NULL OR cardinality(user_ids) = 0 THEN
                        RETURN;
                    END IF;

                    SELECT count(*) INTO expected
                    FROM (
                        SELECT DISTINCT user_id
                        FROM unnest(user_ids) AS changed(user_id)
                        WHERE user_id IS NOT NULL
                    ) AS affected_users;
                    IF expected = 0 THEN
                        RETURN;
                    END IF;

                    INSERT INTO "PlaybackCatalogStates" AS state ("UserId", "Revision")
                    SELECT DISTINCT user_id, 1
                    FROM unnest(user_ids) AS changed(user_id)
                    WHERE user_id IS NOT NULL
                    ON CONFLICT ("UserId") DO UPDATE
                    SET "Revision" = state."Revision" + 1
                    WHERE state."Revision" < 9223372036854775807;
                    GET DIAGNOSTICS affected = ROW_COUNT;
                    IF affected <> expected THEN
                        RAISE EXCEPTION 'Exhausted playback catalog revision';
                    END IF;
                END;
                $$ LANGUAGE plpgsql;

                CREATE FUNCTION sdw_playback_catalog_insert() RETURNS trigger AS $$
                DECLARE
                    affected_users uuid[];
                BEGIN
                    SELECT array_agg(DISTINCT "UserId") INTO affected_users
                    FROM new_rows
                    WHERE "IsWatched" OR "PositionSeconds" > 0;
                    PERFORM sdw_bump_playback_catalog_revisions(affected_users);
                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;

                CREATE FUNCTION sdw_playback_catalog_update() RETURNS trigger AS $$
                DECLARE
                    affected_users uuid[];
                BEGIN
                    SELECT array_agg(DISTINCT "UserId") INTO affected_users
                    FROM (
                        SELECT previous."UserId"
                        FROM old_rows AS previous
                        LEFT JOIN new_rows AS current ON current."Id" = previous."Id"
                        WHERE (previous."IsWatched" OR previous."PositionSeconds" > 0)
                          AND (
                              current."Id" IS NULL
                              OR current."UserId" <> previous."UserId"
                              OR current."AnimationInfoId" <> previous."AnimationInfoId"
                              OR CASE
                                     WHEN current."IsWatched" THEN 2
                                     WHEN current."PositionSeconds" > 0 THEN 1
                                     ELSE 0
                                 END <> CASE
                                     WHEN previous."IsWatched" THEN 2
                                     WHEN previous."PositionSeconds" > 0 THEN 1
                                     ELSE 0
                                 END)
                        UNION
                        SELECT current."UserId"
                        FROM new_rows AS current
                        LEFT JOIN old_rows AS previous ON previous."Id" = current."Id"
                        WHERE (current."IsWatched" OR current."PositionSeconds" > 0)
                          AND (
                              previous."Id" IS NULL
                              OR current."UserId" <> previous."UserId"
                              OR current."AnimationInfoId" <> previous."AnimationInfoId"
                              OR CASE
                                     WHEN current."IsWatched" THEN 2
                                     WHEN current."PositionSeconds" > 0 THEN 1
                                     ELSE 0
                                 END <> CASE
                                     WHEN previous."IsWatched" THEN 2
                                     WHEN previous."PositionSeconds" > 0 THEN 1
                                     ELSE 0
                                 END)
                    ) AS changed;
                    PERFORM sdw_bump_playback_catalog_revisions(affected_users);
                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;

                CREATE FUNCTION sdw_playback_catalog_delete() RETURNS trigger AS $$
                DECLARE
                    affected_users uuid[];
                BEGIN
                    SELECT array_agg(DISTINCT "UserId") INTO affected_users
                    FROM old_rows
                    WHERE "IsWatched" OR "PositionSeconds" > 0;
                    PERFORM sdw_bump_playback_catalog_revisions(affected_users);
                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;

                CREATE FUNCTION sdw_playback_catalog_truncate() RETURNS trigger AS $$
                DECLARE
                    affected_users uuid[];
                BEGIN
                    SELECT array_agg(DISTINCT "UserId") INTO affected_users
                    FROM "PlaybackProgresses"
                    WHERE "IsWatched" OR "PositionSeconds" > 0;
                    PERFORM sdw_bump_playback_catalog_revisions(affected_users);
                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER "TR_PlaybackProgresses_CatalogInsert"
                AFTER INSERT ON "PlaybackProgresses"
                REFERENCING NEW TABLE AS new_rows
                FOR EACH STATEMENT EXECUTE FUNCTION sdw_playback_catalog_insert();

                CREATE TRIGGER "TR_PlaybackProgresses_CatalogUpdate"
                AFTER UPDATE ON "PlaybackProgresses"
                REFERENCING OLD TABLE AS old_rows NEW TABLE AS new_rows
                FOR EACH STATEMENT EXECUTE FUNCTION sdw_playback_catalog_update();

                CREATE TRIGGER "TR_PlaybackProgresses_CatalogDelete"
                AFTER DELETE ON "PlaybackProgresses"
                REFERENCING OLD TABLE AS old_rows
                FOR EACH STATEMENT EXECUTE FUNCTION sdw_playback_catalog_delete();

                CREATE TRIGGER "TR_PlaybackProgresses_CatalogTruncate"
                BEFORE TRUNCATE ON "PlaybackProgresses"
                FOR EACH STATEMENT EXECUTE FUNCTION sdw_playback_catalog_truncate();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER "TR_PlaybackProgresses_CatalogInsert" ON "PlaybackProgresses";
                DROP TRIGGER "TR_PlaybackProgresses_CatalogUpdate" ON "PlaybackProgresses";
                DROP TRIGGER "TR_PlaybackProgresses_CatalogDelete" ON "PlaybackProgresses";
                DROP TRIGGER "TR_PlaybackProgresses_CatalogTruncate" ON "PlaybackProgresses";
                DROP FUNCTION sdw_playback_catalog_insert();
                DROP FUNCTION sdw_playback_catalog_update();
                DROP FUNCTION sdw_playback_catalog_delete();
                DROP FUNCTION sdw_playback_catalog_truncate();
                DROP FUNCTION sdw_bump_playback_catalog_revisions(uuid[]);
                """);

            migrationBuilder.DropTable(
                name: "PlaybackCatalogStates");
        }
    }
}
