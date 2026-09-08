using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecondDimensionWatcherReDive.Migrations
{
    /// <inheritdoc />
    public partial class BindEpisodeTimelinesToMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MappingId",
                table: "MediaTimelines",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaTimelines_MappingId",
                table: "MediaTimelines",
                column: "MappingId");

            migrationBuilder.AddForeignKey(
                name: "FK_MediaTimelines_FileMappings_MappingId",
                table: "MediaTimelines",
                column: "MappingId",
                principalTable: "FileMappings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // Legacy episode keys contain an irreversible hash. A season binding
            // is the only persisted source of their mapping identity. Recover those
            // associations, and leave unowned episodes intact: a later read can
            // safely claim them after resolving that exact current media version.
            // Shared season templates and their acceptance bindings remain intact.
            migrationBuilder.Sql("""
                UPDATE "MediaTimelines" AS timeline
                SET "MappingId" = binding."MappingId"
                FROM "MediaTimelineBindings" AS binding
                WHERE timeline."Key" = 'media:' || binding."MediaVersion";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MediaTimelines_FileMappings_MappingId",
                table: "MediaTimelines");

            migrationBuilder.DropIndex(
                name: "IX_MediaTimelines_MappingId",
                table: "MediaTimelines");

            migrationBuilder.DropColumn(
                name: "MappingId",
                table: "MediaTimelines");
        }
    }
}
