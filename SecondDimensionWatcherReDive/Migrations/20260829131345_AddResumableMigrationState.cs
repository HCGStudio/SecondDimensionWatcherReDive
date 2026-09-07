using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecondDimensionWatcherReDive.Migrations
{
    /// <inheritdoc />
    public partial class AddResumableMigrationState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_MigrationMarkers",
                table: "MigrationMarkers");

            migrationBuilder.RenameColumn(
                name: "AppliedAt",
                table: "MigrationMarkers",
                newName: "FinishedAt");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "FinishedAt",
                table: "MigrationMarkers",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "Key",
                table: "MigrationMarkers",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "MigrationMarkers",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "AttemptCount",
                table: "MigrationMarkers",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "Checkpoint",
                table: "MigrationMarkers",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastErrorSummary",
                table: "MigrationMarkers",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StartedAt",
                table: "MigrationMarkers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "MigrationMarkers",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "MigrationMarkers",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.Sql(
                "UPDATE \"MigrationMarkers\" SET \"UpdatedAt\" = \"FinishedAt\" " +
                "WHERE \"FinishedAt\" IS NOT NULL");

            migrationBuilder.AddPrimaryKey(
                name: "PK_MigrationMarkers",
                table: "MigrationMarkers",
                columns: new[] { "Key", "Version" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_MigrationMarkers_AttemptCount_NonNegative",
                table: "MigrationMarkers",
                sql: "\"AttemptCount\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MigrationMarkers_Status_Range",
                table: "MigrationMarkers",
                sql: "\"Status\" BETWEEN 0 AND 3");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MigrationMarkers_Version_Positive",
                table: "MigrationMarkers",
                sql: "\"Version\" > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_MigrationMarkers",
                table: "MigrationMarkers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MigrationMarkers_AttemptCount_NonNegative",
                table: "MigrationMarkers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MigrationMarkers_Status_Range",
                table: "MigrationMarkers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MigrationMarkers_Version_Positive",
                table: "MigrationMarkers");

            migrationBuilder.Sql(
                "UPDATE \"MigrationMarkers\" SET \"FinishedAt\" = " +
                "COALESCE(\"FinishedAt\", \"UpdatedAt\")");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "MigrationMarkers");

            migrationBuilder.DropColumn(
                name: "AttemptCount",
                table: "MigrationMarkers");

            migrationBuilder.DropColumn(
                name: "Checkpoint",
                table: "MigrationMarkers");

            migrationBuilder.DropColumn(
                name: "LastErrorSummary",
                table: "MigrationMarkers");

            migrationBuilder.DropColumn(
                name: "StartedAt",
                table: "MigrationMarkers");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "MigrationMarkers");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "MigrationMarkers");

            migrationBuilder.AlterColumn<string>(
                name: "Key",
                table: "MigrationMarkers",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "FinishedAt",
                table: "MigrationMarkers",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.RenameColumn(
                name: "FinishedAt",
                table: "MigrationMarkers",
                newName: "AppliedAt");

            migrationBuilder.AddPrimaryKey(
                name: "PK_MigrationMarkers",
                table: "MigrationMarkers",
                column: "Key");
        }
    }
}
