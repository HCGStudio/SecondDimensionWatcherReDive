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
                name: "Feeds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Url = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Feeds", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SeasonBangumis",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MikanId = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    ImageUrl = table.Column<string>(type: "text", nullable: true),
                    ScrapedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeasonBangumis", x => x.Id);
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
                    Season = table.Column<int>(type: "integer", nullable: true),
                    Episode = table.Column<int>(type: "integer", nullable: true),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: true),
                    AnimationId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsAiProcessed = table.Column<bool>(type: "boolean", nullable: false),
                    AiRetryCount = table.Column<int>(type: "integer", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "BangumiSubgroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SeasonBangumiId = table.Column<Guid>(type: "uuid", nullable: false),
                    MikanSubgroupId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ScrapedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BangumiSubgroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BangumiSubgroups_SeasonBangumis_SeasonBangumiId",
                        column: x => x.SeasonBangumiId,
                        principalTable: "SeasonBangumis",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnimationInfo_AnimationId",
                table: "AnimationInfo",
                column: "AnimationId");

            migrationBuilder.CreateIndex(
                name: "IX_AnimationInfo_GroupId",
                table: "AnimationInfo",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_BangumiSubgroups_SeasonBangumiId_MikanSubgroupId",
                table: "BangumiSubgroups",
                columns: new[] { "SeasonBangumiId", "MikanSubgroupId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeasonBangumis_MikanId",
                table: "SeasonBangumis",
                column: "MikanId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnimationInfo");

            migrationBuilder.DropTable(
                name: "BangumiSubgroups");

            migrationBuilder.DropTable(
                name: "Feeds");

            migrationBuilder.DropTable(
                name: "AnimationGroups");

            migrationBuilder.DropTable(
                name: "Animations");

            migrationBuilder.DropTable(
                name: "SeasonBangumis");
        }
    }
}
