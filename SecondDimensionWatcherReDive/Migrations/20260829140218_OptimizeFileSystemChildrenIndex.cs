using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecondDimensionWatcherReDive.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeFileSystemChildrenIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FileSystemEntries_ParentPath_IsDirectory_Name",
                table: "FileSystemEntries");

            migrationBuilder.CreateIndex(
                name: "IX_FileSystemEntries_ParentPath_IsDirectory_Name",
                table: "FileSystemEntries",
                columns: new[] { "ParentPath", "IsDirectory", "Name" },
                descending: new[] { false, true, false });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FileSystemEntries_ParentPath_IsDirectory_Name",
                table: "FileSystemEntries");

            migrationBuilder.CreateIndex(
                name: "IX_FileSystemEntries_ParentPath_IsDirectory_Name",
                table: "FileSystemEntries",
                columns: new[] { "ParentPath", "IsDirectory", "Name" });
        }
    }
}
