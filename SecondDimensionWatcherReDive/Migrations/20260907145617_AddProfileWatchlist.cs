using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecondDimensionWatcherReDive.Migrations
{
    /// <inheritdoc />
    public partial class AddProfileWatchlist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WatchlistItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectKey = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    TmdbId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MikanId = table.Column<int>(type: "integer", nullable: true),
                    Title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WatchlistItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WatchlistItems_Profiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "Profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WatchlistItems_ProfileId_MikanId",
                table: "WatchlistItems",
                columns: new[] { "ProfileId", "MikanId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WatchlistItems_ProfileId_SubjectKey",
                table: "WatchlistItems",
                columns: new[] { "ProfileId", "SubjectKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WatchlistItems");
        }
    }
}
