using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecondDimensionWatcherReDive.Migrations
{
    /// <inheritdoc />
    public partial class AddDownloadCapacity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DownloadCapacityEntries",
                columns: table => new
                {
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    DownloadAttemptId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExpectedBytes = table.Column<long>(type: "bigint", nullable: true),
                    RemainingBytes = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Paused = table.Column<bool>(type: "boolean", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
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

            migrationBuilder.CreateTable(
                name: "TranscodeCapacityReservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DirectoryPath = table.Column<string>(type: "text", nullable: false),
                    VolumeIdentity = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CountsAgainstDownloads = table.Column<bool>(type: "boolean", nullable: false),
                    BudgetBytes = table.Column<long>(type: "bigint", nullable: false),
                    WrittenBytes = table.Column<long>(type: "bigint", nullable: false),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TranscodeCapacityReservations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DownloadCapacityEntries_State_CreatedAt",
                table: "DownloadCapacityEntries",
                columns: new[] { "State", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TranscodeCapacityReservations_DirectoryPath",
                table: "TranscodeCapacityReservations",
                column: "DirectoryPath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TranscodeCapacityReservations_LeaseUntil",
                table: "TranscodeCapacityReservations",
                column: "LeaseUntil");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DownloadCapacityEntries");

            migrationBuilder.DropTable(
                name: "TranscodeCapacityReservations");
        }
    }
}
