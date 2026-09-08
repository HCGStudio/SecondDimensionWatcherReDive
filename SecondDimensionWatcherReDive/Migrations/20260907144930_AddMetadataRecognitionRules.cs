using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecondDimensionWatcherReDive.Migrations
{
    /// <inheritdoc />
    public partial class AddMetadataRecognitionRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MetadataRecognitionRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    SourceFeedId = table.Column<Guid>(type: "uuid", nullable: true),
                    TitlePattern = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SubtitleGroup = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TmdbId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FixedSeason = table.Column<int>(type: "integer", nullable: true),
                    EpisodeOffset = table.Column<int>(type: "integer", nullable: false),
                    CanonicalGroupName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedFromItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetadataRecognitionRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MetadataRecognitionHits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RuleRevision = table.Column<long>(type: "bigint", nullable: false),
                    AnimationInfoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    ItemRevision = table.Column<long>(type: "bigint", nullable: false),
                    AppliedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetadataRecognitionHits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MetadataRecognitionHits_AnimationInfo_AnimationInfoId",
                        column: x => x.AnimationInfoId,
                        principalTable: "AnimationInfo",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MetadataRecognitionHits_MetadataRecognitionRules_RuleId",
                        column: x => x.RuleId,
                        principalTable: "MetadataRecognitionRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MetadataRecognitionHits_AnimationInfoId_ItemRevision",
                table: "MetadataRecognitionHits",
                columns: new[] { "AnimationInfoId", "ItemRevision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MetadataRecognitionHits_AppliedAt",
                table: "MetadataRecognitionHits",
                column: "AppliedAt");

            migrationBuilder.CreateIndex(
                name: "IX_MetadataRecognitionHits_RuleId",
                table: "MetadataRecognitionHits",
                column: "RuleId");

            migrationBuilder.CreateIndex(
                name: "IX_MetadataRecognitionRules_Enabled_SourceFeedId",
                table: "MetadataRecognitionRules",
                columns: new[] { "Enabled", "SourceFeedId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MetadataRecognitionHits");

            migrationBuilder.DropTable(
                name: "MetadataRecognitionRules");
        }
    }
}
