using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecondDimensionWatcherReDive.Migrations
{
    /// <inheritdoc />
    public partial class AddLibrarySearchAndReleaseUpgrades : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EnableVersionUpgrade",
                table: "SubscriptionAutomationPolicies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MinimumUpgradeScore",
                table: "SubscriptionAutomationPolicies",
                type: "integer",
                nullable: false,
                defaultValue: 25);

            migrationBuilder.AddColumn<int>(
                name: "UpgradeRollbackHours",
                table: "SubscriptionAutomationPolicies",
                type: "integer",
                nullable: false,
                defaultValue: 72);

            migrationBuilder.AddColumn<string>(
                name: "EnclosureId",
                table: "AnimationInfo",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExpectedEpisodeCount",
                table: "AnimationInfo",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FeedItemGuid",
                table: "AnimationInfo",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "IngestedAt",
                table: "AnimationInfo",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<bool>(
                name: "IsActiveRelease",
                table: "AnimationInfo",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                """
                UPDATE "AnimationInfo"
                SET "IsActiveRelease" = TRUE;

                WITH ranked_releases AS (
                    SELECT info."Id",
                           row_number() OVER (
                               PARTITION BY info."AnimationId", info."Season", info."Episode"
                               ORDER BY (info."IsDownloadFinished" AND EXISTS (
                                            SELECT 1
                                            FROM "FileMappings" mapping
                                            WHERE mapping."AnimationInfoId" = info."Id"
                                        )) DESC,
                                        info."PublishTime" DESC,
                                        info."Id"
                           ) AS rank
                    FROM "AnimationInfo" info
                    WHERE info."AnimationId" IS NOT NULL
                      AND info."Season" IS NOT NULL
                      AND info."Episode" IS NOT NULL
                )
                UPDATE "AnimationInfo" info
                SET "IsActiveRelease" = ranked.rank = 1
                FROM ranked_releases ranked
                WHERE info."Id" = ranked."Id";
                """);

            migrationBuilder.AddColumn<string>(
                name: "ReleaseCodec",
                table: "AnimationInfo",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReleaseIdentity",
                table: "AnimationInfo",
                type: "character varying(192)",
                maxLength: 192,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "AnimationInfo"
                SET "ReleaseIdentity" = 'legacy:' || replace(lower("Id"::text), '-', '')
                WHERE "ReleaseIdentity" IS NULL;
                """);

            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

            migrationBuilder.AddColumn<string[]>(
                name: "ReleaseLanguages",
                table: "AnimationInfo",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<string>(
                name: "ReleaseResolution",
                table: "AnimationInfo",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReleaseScore",
                table: "AnimationInfo",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ReleaseScoreReasonsJson",
                table: "AnimationInfo",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReleaseSubtitleGroup",
                table: "AnimationInfo",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TorrentInfoHash",
                table: "AnimationInfo",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ReleaseUpgradeOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CurrentScore = table.Column<int>(type: "integer", nullable: false),
                    CandidateScore = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AppliedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RollbackUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailureSummary = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReleaseUpgradeOperations", x => x.Id);
                    table.CheckConstraint("CK_ReleaseUpgradeOperations_ScoreIncrease", "\"CandidateScore\" > \"CurrentScore\"");
                    table.ForeignKey(
                        name: "FK_ReleaseUpgradeOperations_AnimationInfo_CandidateReleaseId",
                        column: x => x.CandidateReleaseId,
                        principalTable: "AnimationInfo",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReleaseUpgradeOperations_AnimationInfo_CurrentReleaseId",
                        column: x => x.CurrentReleaseId,
                        principalTable: "AnimationInfo",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReleaseUpgradeMappingSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OriginalMappingId = table.Column<Guid>(type: "uuid", nullable: false),
                    AnimationInfoId = table.Column<Guid>(type: "uuid", nullable: false),
                    VirtualPath = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    PhysicalPath = table.Column<string>(type: "text", nullable: false),
                    FileStore = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReleaseUpgradeMappingSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReleaseUpgradeMappingSnapshots_ReleaseUpgradeOperations_Ope~",
                        column: x => x.OperationId,
                        principalTable: "ReleaseUpgradeOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_SubscriptionAutomationPolicies_MinimumUpgradeScore",
                table: "SubscriptionAutomationPolicies",
                sql: "\"MinimumUpgradeScore\" >= 1 AND \"MinimumUpgradeScore\" <= 1000");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SubscriptionAutomationPolicies_UpgradeRollbackHours",
                table: "SubscriptionAutomationPolicies",
                sql: "\"UpgradeRollbackHours\" >= 1 AND \"UpgradeRollbackHours\" <= 720");

            migrationBuilder.CreateIndex(
                name: "IX_AnimationInfo_Season_Episode_ReleaseScore",
                table: "AnimationInfo",
                columns: new[] { "Season", "Episode", "ReleaseScore" });

            migrationBuilder.CreateIndex(
                name: "UX_AnimationInfo_ReleaseIdentity",
                table: "AnimationInfo",
                column: "ReleaseIdentity",
                unique: true,
                filter: "\"ReleaseIdentity\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AnimationInfo_ExpectedEpisodeCount_Positive",
                table: "AnimationInfo",
                sql: "\"ExpectedEpisodeCount\" IS NULL OR \"ExpectedEpisodeCount\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AnimationInfo_ReleaseScore_NonNegative",
                table: "AnimationInfo",
                sql: "\"ReleaseScore\" >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseUpgradeMappingSnapshots_OperationId_Kind_OriginalMap~",
                table: "ReleaseUpgradeMappingSnapshots",
                columns: new[] { "OperationId", "Kind", "OriginalMappingId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseUpgradeOperations_CandidateReleaseId",
                table: "ReleaseUpgradeOperations",
                column: "CandidateReleaseId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseUpgradeOperations_Status_CreatedAt",
                table: "ReleaseUpgradeOperations",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_ReleaseUpgradeOperations_ActiveCurrentRelease",
                table: "ReleaseUpgradeOperations",
                column: "CurrentReleaseId",
                unique: true,
                filter: "\"Status\" IN ('Downloading', 'Verifying', 'Applied')");

            migrationBuilder.Sql(
                """
                CREATE INDEX "IX_AnimationInfo_Title_Trgm"
                    ON "AnimationInfo" USING GIN ("Title" gin_trgm_ops);
                CREATE INDEX "IX_Animations_Name_Trgm"
                    ON "Animations" USING GIN ("Name" gin_trgm_ops);
                CREATE INDEX "IX_Animations_OriginalName_Trgm"
                    ON "Animations" USING GIN ("OriginalName" gin_trgm_ops);
                CREATE INDEX "IX_AnimationGroups_Name_Trgm"
                    ON "AnimationGroups" USING GIN ("Name" gin_trgm_ops);
                CREATE INDEX "IX_FileMappings_VirtualPath_Trgm"
                    ON "FileMappings" USING GIN ("VirtualPath" gin_trgm_ops);
                CREATE INDEX "IX_AnimationInfo_ReleaseLanguages_Gin"
                    ON "AnimationInfo" USING GIN ("ReleaseLanguages");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS "IX_AnimationInfo_Title_Trgm";
                DROP INDEX IF EXISTS "IX_Animations_Name_Trgm";
                DROP INDEX IF EXISTS "IX_Animations_OriginalName_Trgm";
                DROP INDEX IF EXISTS "IX_AnimationGroups_Name_Trgm";
                DROP INDEX IF EXISTS "IX_FileMappings_VirtualPath_Trgm";
                DROP INDEX IF EXISTS "IX_AnimationInfo_ReleaseLanguages_Gin";
                """);

            migrationBuilder.DropTable(
                name: "ReleaseUpgradeMappingSnapshots");

            migrationBuilder.DropTable(
                name: "ReleaseUpgradeOperations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SubscriptionAutomationPolicies_MinimumUpgradeScore",
                table: "SubscriptionAutomationPolicies");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SubscriptionAutomationPolicies_UpgradeRollbackHours",
                table: "SubscriptionAutomationPolicies");

            migrationBuilder.DropIndex(
                name: "IX_AnimationInfo_Season_Episode_ReleaseScore",
                table: "AnimationInfo");

            migrationBuilder.DropIndex(
                name: "UX_AnimationInfo_ReleaseIdentity",
                table: "AnimationInfo");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AnimationInfo_ExpectedEpisodeCount_Positive",
                table: "AnimationInfo");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AnimationInfo_ReleaseScore_NonNegative",
                table: "AnimationInfo");

            migrationBuilder.DropColumn(
                name: "EnableVersionUpgrade",
                table: "SubscriptionAutomationPolicies");

            migrationBuilder.DropColumn(
                name: "MinimumUpgradeScore",
                table: "SubscriptionAutomationPolicies");

            migrationBuilder.DropColumn(
                name: "UpgradeRollbackHours",
                table: "SubscriptionAutomationPolicies");

            migrationBuilder.DropColumn(
                name: "EnclosureId",
                table: "AnimationInfo");

            migrationBuilder.DropColumn(
                name: "ExpectedEpisodeCount",
                table: "AnimationInfo");

            migrationBuilder.DropColumn(
                name: "FeedItemGuid",
                table: "AnimationInfo");

            migrationBuilder.DropColumn(
                name: "IngestedAt",
                table: "AnimationInfo");

            migrationBuilder.DropColumn(
                name: "IsActiveRelease",
                table: "AnimationInfo");

            migrationBuilder.DropColumn(
                name: "ReleaseCodec",
                table: "AnimationInfo");

            migrationBuilder.DropColumn(
                name: "ReleaseIdentity",
                table: "AnimationInfo");

            migrationBuilder.DropColumn(
                name: "ReleaseLanguages",
                table: "AnimationInfo");

            migrationBuilder.DropColumn(
                name: "ReleaseResolution",
                table: "AnimationInfo");

            migrationBuilder.DropColumn(
                name: "ReleaseScore",
                table: "AnimationInfo");

            migrationBuilder.DropColumn(
                name: "ReleaseScoreReasonsJson",
                table: "AnimationInfo");

            migrationBuilder.DropColumn(
                name: "ReleaseSubtitleGroup",
                table: "AnimationInfo");

            migrationBuilder.DropColumn(
                name: "TorrentInfoHash",
                table: "AnimationInfo");
        }
    }
}
