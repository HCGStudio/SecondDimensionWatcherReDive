using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecondDimensionWatcherReDive.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnimationGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnimationGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Animations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TmdbId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    OriginalName = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Animations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AnimationInfo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    PublishTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DownloadUrl = table.Column<string>(type: "text", nullable: false),
                    DownloadType = table.Column<string>(type: "text", nullable: false),
                    CachedDownloadData = table.Column<byte[]>(type: "bytea", nullable: false),
                    AdditionalDownloadInfo = table.Column<string>(type: "text", nullable: false),
                    IsDownloadTracked = table.Column<bool>(type: "boolean", nullable: false),
                    DownloadStartTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DownloadEndTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsDownloadFinished = table.Column<bool>(type: "boolean", nullable: false),
                    FileStore = table.Column<string>(type: "text", nullable: true),
                    StorePath = table.Column<string>(type: "text", nullable: true),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: true),
                    AnimationId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnimationInfo", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnimationInfo_AnimationGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "AnimationGroups",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AnimationInfo_Animations_AnimationId",
                        column: x => x.AnimationId,
                        principalTable: "Animations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnimationInfo_AnimationId",
                table: "AnimationInfo",
                column: "AnimationId");

            migrationBuilder.CreateIndex(
                name: "IX_AnimationInfo_GroupId",
                table: "AnimationInfo",
                column: "GroupId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnimationInfo");

            migrationBuilder.DropTable(
                name: "AnimationGroups");

            migrationBuilder.DropTable(
                name: "Animations");
        }
    }
}
