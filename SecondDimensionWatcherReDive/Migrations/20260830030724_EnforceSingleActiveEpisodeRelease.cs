using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecondDimensionWatcherReDive.Migrations
{
    /// <inheritdoc />
    public partial class EnforceSingleActiveEpisodeRelease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReleaseUpgradeOperations_CandidateReleaseId",
                table: "ReleaseUpgradeOperations");

            migrationBuilder.Sql(
                """
                WITH ranked_active_releases AS (
                    SELECT info."Id",
                           row_number() OVER (
                               PARTITION BY info."AnimationId", info."Season", info."Episode"
                               ORDER BY info."IngestedAt", info."PublishTime", info."Id"
                           ) AS rank
                    FROM "AnimationInfo" info
                    WHERE info."IsActiveRelease" = TRUE
                      AND info."AnimationId" IS NOT NULL
                      AND info."Season" IS NOT NULL
                      AND info."Episode" IS NOT NULL
                )
                UPDATE "AnimationInfo" info
                SET "IsActiveRelease" = FALSE
                FROM ranked_active_releases ranked
                WHERE info."Id" = ranked."Id"
                  AND ranked.rank > 1;
                """);

            migrationBuilder.CreateIndex(
                name: "UX_AnimationInfo_ActiveEpisodeRelease",
                table: "AnimationInfo",
                columns: new[] { "AnimationId", "Season", "Episode" },
                unique: true,
                filter: "\"IsActiveRelease\" = TRUE AND \"AnimationId\" IS NOT NULL AND \"Season\" IS NOT NULL AND \"Episode\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseUpgradeOperations_CandidateReleaseId",
                table: "ReleaseUpgradeOperations",
                column: "CandidateReleaseId",
                unique: true,
                filter: "\"Status\" <> 'Failed'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_AnimationInfo_ActiveEpisodeRelease",
                table: "AnimationInfo");

            migrationBuilder.DropIndex(
                name: "IX_ReleaseUpgradeOperations_CandidateReleaseId",
                table: "ReleaseUpgradeOperations");

            migrationBuilder.Sql(
                """
                WITH ranked_candidate_operations AS (
                    SELECT operation."Id",
                           row_number() OVER (
                               PARTITION BY operation."CandidateReleaseId"
                               ORDER BY (operation."Status" <> 'Failed') DESC,
                                        operation."CreatedAt" DESC,
                                        operation."Id"
                           ) AS rank
                    FROM "ReleaseUpgradeOperations" operation
                )
                DELETE FROM "ReleaseUpgradeOperations" operation
                USING ranked_candidate_operations ranked
                WHERE operation."Id" = ranked."Id"
                  AND ranked.rank > 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseUpgradeOperations_CandidateReleaseId",
                table: "ReleaseUpgradeOperations",
                column: "CandidateReleaseId",
                unique: true);
        }
    }
}
