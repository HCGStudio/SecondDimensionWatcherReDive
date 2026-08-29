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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
