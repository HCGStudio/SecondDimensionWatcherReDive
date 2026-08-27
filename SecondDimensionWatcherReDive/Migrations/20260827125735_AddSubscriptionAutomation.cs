using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecondDimensionWatcherReDive.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionAutomation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AutomationDisposition",
                table: "AnimationInfo",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AutomationExplanationJson",
                table: "AnimationInfo",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ReleaseSizeBytes",
                table: "AnimationInfo",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceFeedId",
                table: "AnimationInfo",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SubscriptionAutomationPolicies",
                columns: table => new
                {
                    FeedId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubtitleGroups = table.Column<string[]>(type: "text[]", nullable: false),
                    Resolutions = table.Column<string[]>(type: "text[]", nullable: false),
                    Codecs = table.Column<string[]>(type: "text[]", nullable: false),
                    Languages = table.Column<string[]>(type: "text[]", nullable: false),
                    MinSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    MaxSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    ExcludedKeywords = table.Column<string[]>(type: "text[]", nullable: false),
                    Mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionAutomationPolicies", x => x.FeedId);
                    table.ForeignKey(
                        name: "FK_SubscriptionAutomationPolicies_Feeds_FeedId",
                        column: x => x.FeedId,
                        principalTable: "Feeds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnimationInfo_SourceFeedId",
                table: "AnimationInfo",
                column: "SourceFeedId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionAutomationPolicies_UpdatedAt",
                table: "SubscriptionAutomationPolicies",
                column: "UpdatedAt");

            migrationBuilder.AddForeignKey(
                name: "FK_AnimationInfo_Feeds_SourceFeedId",
                table: "AnimationInfo",
                column: "SourceFeedId",
                principalTable: "Feeds",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AnimationInfo_Feeds_SourceFeedId",
                table: "AnimationInfo");

            migrationBuilder.DropTable(
                name: "SubscriptionAutomationPolicies");

            migrationBuilder.DropIndex(
                name: "IX_AnimationInfo_SourceFeedId",
                table: "AnimationInfo");

            migrationBuilder.DropColumn(
                name: "AutomationDisposition",
                table: "AnimationInfo");

            migrationBuilder.DropColumn(
                name: "AutomationExplanationJson",
                table: "AnimationInfo");

            migrationBuilder.DropColumn(
                name: "ReleaseSizeBytes",
                table: "AnimationInfo");

            migrationBuilder.DropColumn(
                name: "SourceFeedId",
                table: "AnimationInfo");
        }
    }
}
