using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecondDimensionWatcherReDive.Migrations
{
    /// <inheritdoc />
    public partial class AddSeasonAndEpisode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Episode",
                table: "AnimationInfo",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Season",
                table: "AnimationInfo",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Episode",
                table: "AnimationInfo");

            migrationBuilder.DropColumn(
                name: "Season",
                table: "AnimationInfo");
        }
    }
}
