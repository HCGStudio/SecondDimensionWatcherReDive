using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecondDimensionWatcherReDive.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaTimelines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoSkip",
                table: "PlaybackPreferences",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "MediaTimelines",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(192)", maxLength: 192, nullable: false),
                    DurationSeconds = table.Column<double>(type: "double precision", nullable: false),
                    PointsJson = table.Column<string>(type: "jsonb", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaTimelines", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "MediaTimelineBindings",
                columns: table => new
                {
                    MediaVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MappingId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeasonKey = table.Column<string>(type: "character varying(192)", maxLength: 192, nullable: false),
                    DurationSeconds = table.Column<double>(type: "double precision", nullable: false),
                    AcceptedRevision = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaTimelineBindings", x => x.MediaVersion);
                    table.ForeignKey(
                        name: "FK_MediaTimelineBindings_FileMappings_MappingId",
                        column: x => x.MappingId,
                        principalTable: "FileMappings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MediaTimelineBindings_MediaTimelines_SeasonKey",
                        column: x => x.SeasonKey,
                        principalTable: "MediaTimelines",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MediaTimelineBindings_MappingId",
                table: "MediaTimelineBindings",
                column: "MappingId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaTimelineBindings_SeasonKey",
                table: "MediaTimelineBindings",
                column: "SeasonKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MediaTimelineBindings");

            migrationBuilder.DropTable(
                name: "MediaTimelines");

            migrationBuilder.DropColumn(
                name: "AutoSkip",
                table: "PlaybackPreferences");
        }
    }
}
