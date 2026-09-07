using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecondDimensionWatcherReDive.Migrations
{
    /// <inheritdoc />
    public partial class AddTranscodeCacheReaders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TranscodeCacheReaders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DirectoryPath = table.Column<string>(type: "text", nullable: false),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TranscodeCacheReaders", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TranscodeCacheReaders_DirectoryPath",
                table: "TranscodeCacheReaders",
                column: "DirectoryPath");

            migrationBuilder.CreateIndex(
                name: "IX_TranscodeCacheReaders_LeaseUntil",
                table: "TranscodeCacheReaders",
                column: "LeaseUntil");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TranscodeCacheReaders");
        }
    }
}
