using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecondDimensionWatcherReDive.Migrations
{
    /// <inheritdoc />
    public partial class AddRetiredReleaseMarker : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsRetiredRelease",
                table: "AnimationInfo",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                """
                UPDATE "AnimationInfo" AS info
                SET "IsRetiredRelease" = TRUE
                WHERE NOT info."IsActiveRelease"
                  AND (
                      EXISTS (
                          SELECT 1
                          FROM "ReleaseUpgradeOperations" AS operation
                          WHERE operation."CurrentReleaseId" = info."Id"
                            AND operation."Status" IN ('Applied', 'Completed'))
                      OR EXISTS (
                          SELECT 1
                          FROM "ReleaseUpgradeOperations" AS operation
                          WHERE operation."CandidateReleaseId" = info."Id"
                            AND operation."Status" = 'RolledBack'));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsRetiredRelease",
                table: "AnimationInfo");
        }
    }
}
