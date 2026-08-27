using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecondDimensionWatcherReDive.Migrations
{
    /// <inheritdoc />
    public partial class AddMetadataReviewCenter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CurrentMetadataReviewOperationId",
                table: "AnimationInfo",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "MetadataConfidence",
                table: "AnimationInfo",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MetadataLastError",
                table: "AnimationInfo",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "MetadataReviewedAt",
                table: "AnimationInfo",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MetadataStatus",
                table: "AnimationInfo",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "StateVersion",
                table: "AnimationInfo",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.Sql(
                """
                UPDATE "AnimationInfo"
                SET "MetadataStatus" = CASE
                    WHEN "IsAiProcessed" THEN 1
                    WHEN "AiRetryCount" >= 3 THEN 3
                    ELSE 0
                END
                """);

            migrationBuilder.CreateTable(
                name: "MetadataReviewOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AnimationInfoId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    BaseVersion = table.Column<long>(type: "bigint", nullable: false),
                    BaseFileStore = table.Column<string>(type: "text", nullable: true),
                    BaseStorePath = table.Column<string>(type: "text", nullable: true),
                    BaseIsDownloadFinished = table.Column<bool>(type: "boolean", nullable: false),
                    ProposedAnimationTmdbId = table.Column<string>(type: "text", nullable: false),
                    ProposedAnimationName = table.Column<string>(type: "text", nullable: false),
                    ProposedAnimationOriginalName = table.Column<string>(type: "text", nullable: false),
                    ProposedAnimationPosterPath = table.Column<string>(type: "text", nullable: true),
                    ProposedDescription = table.Column<string>(type: "text", nullable: false),
                    ProposedSeason = table.Column<int>(type: "integer", nullable: true),
                    ProposedEpisode = table.Column<int>(type: "integer", nullable: true),
                    ProposedGroupName = table.Column<string>(type: "text", nullable: true),
                    AppliedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UndoneAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AppliedVersion = table.Column<long>(type: "bigint", nullable: true),
                    PreviousDescription = table.Column<string>(type: "text", nullable: true),
                    PreviousAnimationId = table.Column<Guid>(type: "uuid", nullable: true),
                    PreviousGroupId = table.Column<Guid>(type: "uuid", nullable: true),
                    PreviousSeason = table.Column<int>(type: "integer", nullable: true),
                    PreviousEpisode = table.Column<int>(type: "integer", nullable: true),
                    PreviousMetadataStatus = table.Column<int>(type: "integer", nullable: true),
                    PreviousConfidence = table.Column<double>(type: "double precision", nullable: true),
                    PreviousLastError = table.Column<string>(type: "text", nullable: true),
                    PreviousIsAiProcessed = table.Column<bool>(type: "boolean", nullable: true),
                    PreviousAiRetryCount = table.Column<int>(type: "integer", nullable: true),
                    PreviousReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PreviousCurrentOperationId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetadataReviewOperations", x => x.Id);
                    table.CheckConstraint("CK_MetadataReviewOperations_Expiry", "\"ExpiresAt\" > \"CreatedAt\"");
                    table.ForeignKey(
                        name: "FK_MetadataReviewOperations_AnimationInfo_AnimationInfoId",
                        column: x => x.AnimationInfoId,
                        principalTable: "AnimationInfo",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MetadataReviewMappingSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    VirtualPath = table.Column<string>(type: "text", nullable: false),
                    PhysicalPath = table.Column<string>(type: "text", nullable: false),
                    FileStore = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetadataReviewMappingSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MetadataReviewMappingSnapshots_MetadataReviewOperations_Ope~",
                        column: x => x.OperationId,
                        principalTable: "MetadataReviewOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(
                """
                WITH ranked_rules AS (
                    SELECT rules."Id",
                           row_number() OVER (
                               PARTITION BY animations."TmdbId", rules."Pattern"
                               ORDER BY animations."Id", rules."CreatedAt", rules."Id") AS duplicate_rank
                    FROM "FileNameRegexRules" AS rules
                    INNER JOIN "Animations" AS animations
                        ON animations."Id" = rules."AnimationId"
                )
                DELETE FROM "FileNameRegexRules" AS rules
                USING ranked_rules
                WHERE rules."Id" = ranked_rules."Id"
                  AND ranked_rules.duplicate_rank > 1;

                WITH ranked_animations AS (
                    SELECT animations."Id",
                           first_value(animations."Id") OVER (
                               PARTITION BY animations."TmdbId"
                               ORDER BY animations."Id") AS keep_id
                    FROM "Animations" AS animations
                )
                UPDATE "FileNameRegexRules" AS rules
                SET "AnimationId" = ranked_animations.keep_id
                FROM ranked_animations
                WHERE rules."AnimationId" = ranked_animations."Id"
                  AND ranked_animations."Id" <> ranked_animations.keep_id;

                WITH ranked_animations AS (
                    SELECT animations."Id",
                           first_value(animations."Id") OVER (
                               PARTITION BY animations."TmdbId"
                               ORDER BY animations."Id") AS keep_id
                    FROM "Animations" AS animations
                )
                UPDATE "AnimationInfo" AS info
                SET "AnimationId" = ranked_animations.keep_id
                FROM ranked_animations
                WHERE info."AnimationId" = ranked_animations."Id"
                  AND ranked_animations."Id" <> ranked_animations.keep_id;

                WITH ranked_animations AS (
                    SELECT animations."Id",
                           first_value(animations."Id") OVER (
                               PARTITION BY animations."TmdbId"
                               ORDER BY animations."Id") AS keep_id
                    FROM "Animations" AS animations
                )
                DELETE FROM "Animations" AS animations
                USING ranked_animations
                WHERE animations."Id" = ranked_animations."Id"
                  AND ranked_animations."Id" <> ranked_animations.keep_id;

                WITH ranked_groups AS (
                    SELECT groups."Id",
                           first_value(groups."Id") OVER (
                               PARTITION BY groups."Name"
                               ORDER BY groups."Id") AS keep_id
                    FROM "AnimationGroups" AS groups
                )
                UPDATE "AnimationInfo" AS info
                SET "GroupId" = ranked_groups.keep_id
                FROM ranked_groups
                WHERE info."GroupId" = ranked_groups."Id"
                  AND ranked_groups."Id" <> ranked_groups.keep_id;

                WITH ranked_groups AS (
                    SELECT groups."Id",
                           first_value(groups."Id") OVER (
                               PARTITION BY groups."Name"
                               ORDER BY groups."Id") AS keep_id
                    FROM "AnimationGroups" AS groups
                )
                DELETE FROM "AnimationGroups" AS groups
                USING ranked_groups
                WHERE groups."Id" = ranked_groups."Id"
                  AND ranked_groups."Id" <> ranked_groups.keep_id;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Animations_TmdbId",
                table: "Animations",
                column: "TmdbId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AnimationInfo_CurrentMetadataReviewOperationId",
                table: "AnimationInfo",
                column: "CurrentMetadataReviewOperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AnimationInfo_MetadataStatus_PublishTime",
                table: "AnimationInfo",
                columns: new[] { "MetadataStatus", "PublishTime" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_AnimationInfo_MetadataConfidence_Range",
                table: "AnimationInfo",
                sql: "\"MetadataConfidence\" IS NULL OR (\"MetadataConfidence\" >= 0 AND \"MetadataConfidence\" <= 1)");

            migrationBuilder.CreateIndex(
                name: "IX_AnimationGroups_Name",
                table: "AnimationGroups",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MetadataReviewMappingSnapshots_OperationId_Kind_VirtualPath",
                table: "MetadataReviewMappingSnapshots",
                columns: new[] { "OperationId", "Kind", "VirtualPath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MetadataReviewOperations_AnimationInfoId_AppliedVersion",
                table: "MetadataReviewOperations",
                columns: new[] { "AnimationInfoId", "AppliedVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MetadataReviewOperations_AnimationInfoId_State",
                table: "MetadataReviewOperations",
                columns: new[] { "AnimationInfoId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_MetadataReviewOperations_State_ExpiresAt",
                table: "MetadataReviewOperations",
                columns: new[] { "State", "ExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MetadataReviewMappingSnapshots");

            migrationBuilder.DropTable(
                name: "MetadataReviewOperations");

            migrationBuilder.DropIndex(
                name: "IX_Animations_TmdbId",
                table: "Animations");

            migrationBuilder.DropIndex(
                name: "IX_AnimationInfo_CurrentMetadataReviewOperationId",
                table: "AnimationInfo");

            migrationBuilder.DropIndex(
                name: "IX_AnimationInfo_MetadataStatus_PublishTime",
                table: "AnimationInfo");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AnimationInfo_MetadataConfidence_Range",
                table: "AnimationInfo");

            migrationBuilder.DropIndex(
                name: "IX_AnimationGroups_Name",
                table: "AnimationGroups");

            migrationBuilder.DropColumn(
                name: "CurrentMetadataReviewOperationId",
                table: "AnimationInfo");

            migrationBuilder.DropColumn(
                name: "MetadataConfidence",
                table: "AnimationInfo");

            migrationBuilder.DropColumn(
                name: "MetadataLastError",
                table: "AnimationInfo");

            migrationBuilder.DropColumn(
                name: "MetadataReviewedAt",
                table: "AnimationInfo");

            migrationBuilder.DropColumn(
                name: "MetadataStatus",
                table: "AnimationInfo");

            migrationBuilder.DropColumn(
                name: "StateVersion",
                table: "AnimationInfo");
        }
    }
}
