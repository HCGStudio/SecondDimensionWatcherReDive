using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecondDimensionWatcherReDive.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaLibrarySources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "MediaLibraryMissingSince",
                table: "AnimationInfo",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MediaLibrarySourceId",
                table: "AnimationInfo",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MediaLibrarySources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Path = table.Column<string>(type: "text", nullable: false),
                    IsMonitoring = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastScanAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    LastImportedCount = table.Column<int>(type: "integer", nullable: false),
                    LastUpdatedCount = table.Column<int>(type: "integer", nullable: false),
                    LastRemovedCount = table.Column<int>(type: "integer", nullable: false),
                    LastSkippedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaLibrarySources", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnimationInfo_FileStore_StorePath",
                table: "AnimationInfo",
                columns: new[] { "FileStore", "StorePath" },
                unique: true,
                filter: "\"DownloadType\" = 'http://schemas.hcgstudio.com/ws/2023/06/sdw/downloadtype/media-library-import'");

            migrationBuilder.CreateIndex(
                name: "IX_AnimationInfo_MediaLibrarySourceId",
                table: "AnimationInfo",
                column: "MediaLibrarySourceId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaLibrarySources_Path",
                table: "MediaLibrarySources",
                column: "Path",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AnimationInfo_MediaLibrarySources_MediaLibrarySourceId",
                table: "AnimationInfo",
                column: "MediaLibrarySourceId",
                principalTable: "MediaLibrarySources",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AnimationInfo_MediaLibrarySources_MediaLibrarySourceId",
                table: "AnimationInfo");

            migrationBuilder.DropTable(
                name: "MediaLibrarySources");

            migrationBuilder.DropIndex(
                name: "IX_AnimationInfo_FileStore_StorePath",
                table: "AnimationInfo");

            migrationBuilder.DropIndex(
                name: "IX_AnimationInfo_MediaLibrarySourceId",
                table: "AnimationInfo");

            migrationBuilder.DropColumn(
                name: "MediaLibraryMissingSince",
                table: "AnimationInfo");

            migrationBuilder.DropColumn(
                name: "MediaLibrarySourceId",
                table: "AnimationInfo");
        }
    }
}
