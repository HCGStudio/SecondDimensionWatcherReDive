using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecondDimensionWatcherReDive.Migrations
{
    /// <inheritdoc />
    public partial class RemoveDownloadCapacity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DownloadCapacityEntries");

            migrationBuilder.DropColumn(
                name: "CountsAgainstDownloads",
                table: "TranscodeCapacityReservations");

            migrationBuilder.DropColumn(
                name: "VolumeIdentity",
                table: "TranscodeCapacityReservations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CountsAgainstDownloads",
                table: "TranscodeCapacityReservations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "VolumeIdentity",
                table: "TranscodeCapacityReservations",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DownloadCapacityEntries",
                columns: table => new
                {
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DownloadAttemptId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExpectedBytes = table.Column<long>(type: "bigint", nullable: true),
                    Hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Paused = table.Column<bool>(type: "boolean", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    RemainingBytes = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DownloadCapacityEntries", x => x.ItemId);
                    table.ForeignKey(
                        name: "FK_DownloadCapacityEntries_AnimationInfo_ItemId",
                        column: x => x.ItemId,
                        principalTable: "AnimationInfo",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DownloadCapacityEntries_State_CreatedAt",
                table: "DownloadCapacityEntries",
                columns: new[] { "State", "CreatedAt" });
        }
    }
}
