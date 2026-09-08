using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecondDimensionWatcherReDive.Migrations
{
    /// <inheritdoc />
    public partial class RestoreStandaloneAutomation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "StandaloneAutomationPending",
                table: "AnimationInfo",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_AnimationInfo_StandaloneAutomationPending",
                table: "AnimationInfo",
                column: "Id",
                filter: "\"StandaloneAutomationPending\" = TRUE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AnimationInfo_StandaloneAutomationPending",
                table: "AnimationInfo");

            migrationBuilder.DropColumn(
                name: "StandaloneAutomationPending",
                table: "AnimationInfo");
        }
    }
}
