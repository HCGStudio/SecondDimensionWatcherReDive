using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecondDimensionWatcherReDive.Migrations
{
    /// <inheritdoc />
    public partial class BindRecognitionRulesToMetadataReviewPreviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RecognitionRuleId",
                table: "MetadataReviewOperations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "RecognitionRuleRevision",
                table: "MetadataReviewOperations",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RecognitionRuleId",
                table: "MetadataReviewOperations");

            migrationBuilder.DropColumn(
                name: "RecognitionRuleRevision",
                table: "MetadataReviewOperations");
        }
    }
}
