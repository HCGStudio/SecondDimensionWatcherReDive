using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecondDimensionWatcherReDive.Migrations
{
    /// <inheritdoc />
    public partial class AddFileMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FileMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AnimationInfoId = table.Column<Guid>(type: "uuid", nullable: false),
                    VirtualPath = table.Column<string>(type: "text", nullable: false),
                    PhysicalPath = table.Column<string>(type: "text", nullable: false),
                    FileStore = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileMappings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FileMappings_AnimationInfoId",
                table: "FileMappings",
                column: "AnimationInfoId");

            migrationBuilder.CreateIndex(
                name: "IX_FileMappings_VirtualPath",
                table: "FileMappings",
                column: "VirtualPath",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FileMappings");
        }
    }
}
