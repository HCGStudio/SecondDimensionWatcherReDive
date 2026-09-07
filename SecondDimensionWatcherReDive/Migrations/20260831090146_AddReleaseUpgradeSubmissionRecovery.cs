using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecondDimensionWatcherReDive.Migrations
{
    /// <inheritdoc />
    public partial class AddReleaseUpgradeSubmissionRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DownloadSubmissionLeaseId",
                table: "AnimationInfo",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DownloadSubmissionLeaseUntil",
                table: "AnimationInfo",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DownloadCancellationLeaseId",
                table: "AnimationInfo",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DownloadCancellationLeaseUntil",
                table: "AnimationInfo",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DownloadCancellationRemoveFile",
                table: "AnimationInfo",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                """
                UPDATE "AnimationInfo"
                SET "DownloadCancellationLeaseId" = gen_random_uuid(),
                    "DownloadCancellationLeaseUntil" = clock_timestamp() + interval '3 minutes'
                WHERE "DownloadCancellationId" IS NOT NULL
                """);

            migrationBuilder.AddColumn<bool>(
                name: "DownloadCancellationRemoveFile",
                table: "ReleaseUpgradeOperations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DownloadPreparedAt",
                table: "ReleaseUpgradeOperations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DownloadSubmissionLeaseId",
                table: "ReleaseUpgradeOperations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DownloadSubmissionLeaseUntil",
                table: "ReleaseUpgradeOperations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DownloadSubmittedAt",
                table: "ReleaseUpgradeOperations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(
                """
                WITH recovery AS (
                    SELECT DISTINCT ON (operation."CandidateReleaseId")
                        operation."Id",
                        info."DownloadCancellationLeaseId" AS "LeaseId",
                        info."DownloadCancellationLeaseUntil" AS "LeaseUntil"
                    FROM "ReleaseUpgradeOperations" AS operation
                    INNER JOIN "AnimationInfo" AS info
                        ON info."Id" = operation."CandidateReleaseId"
                    WHERE info."DownloadCancellationId" IS NOT NULL
                      AND operation."Status" IN
                          ('Downloading', 'Verifying', 'Failed', 'RolledBack', 'Completed')
                    ORDER BY operation."CandidateReleaseId",
                        CASE WHEN operation."Status" IN ('Downloading', 'Verifying') THEN 0 ELSE 1 END,
                        operation."CreatedAt" DESC
                )
                UPDATE "ReleaseUpgradeOperations" AS operation
                SET "Status" = CASE
                        WHEN operation."Status" IN ('Downloading', 'Verifying') THEN 'Failed'
                        ELSE operation."Status"
                    END,
                    "FailureSummary" = CASE
                        WHEN operation."Status" IN ('Downloading', 'Verifying')
                            THEN 'Upgrade recovery resumed a cancellation left pending during schema upgrade.'
                        ELSE operation."FailureSummary"
                    END,
                    "CompletedAt" = CASE
                        WHEN operation."Status" IN ('Downloading', 'Verifying')
                            THEN COALESCE(operation."CompletedAt", clock_timestamp())
                        ELSE operation."CompletedAt"
                    END,
                    "DownloadSubmissionLeaseId" = recovery."LeaseId",
                    "DownloadSubmissionLeaseUntil" = recovery."LeaseUntil"
                FROM recovery
                WHERE operation."Id" = recovery."Id"
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DownloadSubmissionLeaseId",
                table: "AnimationInfo");

            migrationBuilder.DropColumn(
                name: "DownloadSubmissionLeaseUntil",
                table: "AnimationInfo");

            migrationBuilder.DropColumn(
                name: "DownloadCancellationLeaseId",
                table: "AnimationInfo");

            migrationBuilder.DropColumn(
                name: "DownloadCancellationLeaseUntil",
                table: "AnimationInfo");

            migrationBuilder.DropColumn(
                name: "DownloadCancellationRemoveFile",
                table: "AnimationInfo");

            migrationBuilder.DropColumn(
                name: "DownloadCancellationRemoveFile",
                table: "ReleaseUpgradeOperations");

            migrationBuilder.DropColumn(
                name: "DownloadPreparedAt",
                table: "ReleaseUpgradeOperations");

            migrationBuilder.DropColumn(
                name: "DownloadSubmissionLeaseId",
                table: "ReleaseUpgradeOperations");

            migrationBuilder.DropColumn(
                name: "DownloadSubmissionLeaseUntil",
                table: "ReleaseUpgradeOperations");

            migrationBuilder.DropColumn(
                name: "DownloadSubmittedAt",
                table: "ReleaseUpgradeOperations");
        }
    }
}
