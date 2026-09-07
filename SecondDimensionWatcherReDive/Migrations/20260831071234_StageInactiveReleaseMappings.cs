using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecondDimensionWatcherReDive.Migrations
{
    /// <inheritdoc />
    public partial class StageInactiveReleaseMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StagedFileMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AnimationInfoId = table.Column<Guid>(type: "uuid", nullable: false),
                    VirtualPath = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    PhysicalPath = table.Column<string>(type: "text", nullable: false),
                    FileStore = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StagedFileMappings", x => x.Id);
                    table.CheckConstraint("CK_StagedFileMappings_VirtualPath_Canonical", "\"VirtualPath\" ~ '^/[^/]+(?:/[^/]+)*$' AND \"VirtualPath\" !~ '(^|/)\\.\\.?($|/)'");
                    table.ForeignKey(
                        name: "FK_StagedFileMappings_AnimationInfo_AnimationInfoId",
                        column: x => x.AnimationInfoId,
                        principalTable: "AnimationInfo",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StagedFileMappings_AnimationInfoId_VirtualPath",
                table: "StagedFileMappings",
                columns: new[] { "AnimationInfoId", "VirtualPath" },
                unique: true);

            // Alternative releases used to share the live FileMappings namespace.
            // Move only known-episode alternatives that have a different active
            // release; unknown/unmatched downloads remain ordinary live content.
            migrationBuilder.Sql(
                """
                INSERT INTO "StagedFileMappings"
                    ("Id", "AnimationInfoId", "VirtualPath", "PhysicalPath", "FileStore")
                SELECT mapping."Id", mapping."AnimationInfoId", mapping."VirtualPath",
                       mapping."PhysicalPath", mapping."FileStore"
                FROM "FileMappings" AS mapping
                JOIN "AnimationInfo" AS candidate
                  ON candidate."Id" = mapping."AnimationInfoId"
                WHERE NOT candidate."IsActiveRelease"
                  AND candidate."AnimationId" IS NOT NULL
                  AND candidate."Season" IS NOT NULL
                  AND candidate."Episode" IS NOT NULL
                  AND EXISTS (
                      SELECT 1
                      FROM "AnimationInfo" AS active
                      WHERE active."Id" <> candidate."Id"
                        AND active."IsActiveRelease"
                        AND active."AnimationId" = candidate."AnimationId"
                        AND active."Season" = candidate."Season"
                        AND active."Episode" = candidate."Episode");

                DELETE FROM "FileMappings" AS mapping
                USING "AnimationInfo" AS candidate
                WHERE candidate."Id" = mapping."AnimationInfoId"
                  AND NOT candidate."IsActiveRelease"
                  AND candidate."AnimationId" IS NOT NULL
                  AND candidate."Season" IS NOT NULL
                  AND candidate."Episode" IS NOT NULL
                  AND EXISTS (
                      SELECT 1
                      FROM "AnimationInfo" AS active
                      WHERE active."Id" <> candidate."Id"
                        AND active."IsActiveRelease"
                        AND active."AnimationId" = candidate."AnimationId"
                        AND active."Season" = candidate."Season"
                        AND active."Episode" = candidate."Episode");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Fail the downgrade transaction if the live namespace changed in a
            // way that prevents restoring every staged mapping; never discard it.
            migrationBuilder.Sql(
                """
                INSERT INTO "FileMappings"
                    ("Id", "AnimationInfoId", "VirtualPath", "PhysicalPath", "FileStore")
                SELECT "Id", "AnimationInfoId", "VirtualPath", "PhysicalPath", "FileStore"
                FROM "StagedFileMappings";
                """);

            migrationBuilder.DropTable(
                name: "StagedFileMappings");
        }
    }
}
