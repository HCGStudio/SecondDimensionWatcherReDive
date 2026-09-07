using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecondDimensionWatcherReDive.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiSourceSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MultiSourceSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TmdbId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Season = table.Column<int>(type: "integer", nullable: false),
                    WaitMinutes = table.Column<int>(type: "integer", nullable: false),
                    Mode = table.Column<string>(type: "text", nullable: false),
                    SubtitleGroups = table.Column<string[]>(type: "text[]", nullable: false),
                    Resolutions = table.Column<string[]>(type: "text[]", nullable: false),
                    Codecs = table.Column<string[]>(type: "text[]", nullable: false),
                    Languages = table.Column<string[]>(type: "text[]", nullable: false),
                    MinSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    MaxSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    ExcludedKeywords = table.Column<string[]>(type: "text[]", nullable: false),
                    EnableVersionUpgrade = table.Column<bool>(type: "boolean", nullable: false),
                    MinimumUpgradeScore = table.Column<int>(type: "integer", nullable: false),
                    UpgradeRollbackHours = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MultiSourceSubscriptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MultiSourceEpisodeDecisions",
                columns: table => new
                {
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Episode = table.Column<int>(type: "integer", nullable: false),
                    WaitStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    WaitUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SelectedReleaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    Outcome = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MultiSourceEpisodeDecisions", x => new { x.SubscriptionId, x.Episode });
                    table.ForeignKey(
                        name: "FK_MultiSourceEpisodeDecisions_MultiSourceSubscriptions_Subscr~",
                        column: x => x.SubscriptionId,
                        principalTable: "MultiSourceSubscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MultiSourceFeeds",
                columns: table => new
                {
                    FeedId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MultiSourceFeeds", x => x.FeedId);
                    table.ForeignKey(
                        name: "FK_MultiSourceFeeds_Feeds_FeedId",
                        column: x => x.FeedId,
                        principalTable: "Feeds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MultiSourceFeeds_MultiSourceSubscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "MultiSourceSubscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MultiSourceFeeds_SubscriptionId_Priority",
                table: "MultiSourceFeeds",
                columns: new[] { "SubscriptionId", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_MultiSourceSubscriptions_TmdbId_Season",
                table: "MultiSourceSubscriptions",
                columns: new[] { "TmdbId", "Season" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MultiSourceEpisodeDecisions");

            migrationBuilder.DropTable(
                name: "MultiSourceFeeds");

            migrationBuilder.DropTable(
                name: "MultiSourceSubscriptions");
        }
    }
}
