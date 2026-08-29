using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecondDimensionWatcherReDive.Migrations
{
    /// <inheritdoc />
    public partial class AddHouseholdIdentityAndAccessScopes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExpiresAt",
                table: "WebDavTokens",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RevokedAt",
                table: "WebDavTokens",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Scope",
                table: "WebDavTokens",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "read");

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "WebDavTokens",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000001"));

            migrationBuilder.AddColumn<string>(
                name: "VirtualRoot",
                table: "WebDavTokens",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: false,
                defaultValue: "/");

            migrationBuilder.AddColumn<Guid>(
                name: "ProfileId",
                table: "ChatConversations",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    IsDisabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Avatar = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    PinHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Profiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Profiles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LoginSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActiveProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    RefreshTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DeviceName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    AuthenticatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoginSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LoginSessions_Profiles_ActiveProfileId",
                        column: x => x.ActiveProfileId,
                        principalTable: "Profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LoginSessions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Existing installations authenticated a single household and stored playback
            // rows under Guid.Empty. Materialize that household only when legacy data exists;
            // a truly fresh database must remain eligible for first-user registration.
            migrationBuilder.Sql(
                """
                INSERT INTO "Users"
                    ("Id", "Username", "PasswordHash", "Role", "IsDisabled", "CreatedAt", "UpdatedAt")
                SELECT
                    '00000000-0000-0000-0000-000000000001',
                    'admin',
                    NULL,
                    'Admin',
                    FALSE,
                    CURRENT_TIMESTAMP,
                    CURRENT_TIMESTAMP
                WHERE EXISTS (SELECT 1 FROM "PlaybackProgresses")
                   OR EXISTS (SELECT 1 FROM "PlaybackPreferences")
                   OR EXISTS (SELECT 1 FROM "ChatConversations")
                   OR EXISTS (SELECT 1 FROM "WebDavTokens");

                INSERT INTO "Profiles"
                    ("Id", "UserId", "Name", "Avatar", "PinHash", "IsDefault", "CreatedAt", "UpdatedAt")
                SELECT
                    '00000000-0000-0000-0000-000000000000',
                    '00000000-0000-0000-0000-000000000001',
                    'Home',
                    NULL,
                    NULL,
                    TRUE,
                    CURRENT_TIMESTAMP,
                    CURRENT_TIMESTAMP
                WHERE EXISTS (
                    SELECT 1 FROM "Users"
                    WHERE "Id" = '00000000-0000-0000-0000-000000000001');

                UPDATE "PlaybackProgresses"
                SET "UserId" = '00000000-0000-0000-0000-000000000000';

                UPDATE "PlaybackPreferences"
                SET "UserId" = '00000000-0000-0000-0000-000000000000';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_WebDavTokens_UserId",
                table: "WebDavTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatConversations_ProfileId_UpdatedAt",
                table: "ChatConversations",
                columns: new[] { "ProfileId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LoginSessions_ActiveProfileId",
                table: "LoginSessions",
                column: "ActiveProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_LoginSessions_UserId_RevokedAt_ExpiresAt",
                table: "LoginSessions",
                columns: new[] { "UserId", "RevokedAt", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Profiles_UserId_Name",
                table: "Profiles",
                columns: new[] { "UserId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ChatConversations_Profiles_ProfileId",
                table: "ChatConversations",
                column: "ProfileId",
                principalTable: "Profiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PlaybackPreferences_Profiles_UserId",
                table: "PlaybackPreferences",
                column: "UserId",
                principalTable: "Profiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PlaybackProgresses_Profiles_UserId",
                table: "PlaybackProgresses",
                column: "UserId",
                principalTable: "Profiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_WebDavTokens_Users_UserId",
                table: "WebDavTokens",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The previous schema can represent only the untouched legacy household. Refuse
            // downgrade before dropping any column when doing so would merge profile history,
            // lose account credentials, widen a device root, revive a revoked token, or remove
            // an expiry. PostgreSQL runs the migration transactionally, so this leaves the
            // current schema and all data intact.
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM "Users"
                        WHERE "Id" <> '00000000-0000-0000-0000-000000000001'
                           OR "Username" <> 'admin'
                           OR "PasswordHash" IS NOT NULL
                           OR "Role" <> 'Admin'
                           OR "IsDisabled")
                       OR EXISTS (
                        SELECT 1 FROM "Profiles"
                        WHERE "Id" <> '00000000-0000-0000-0000-000000000000'
                           OR "UserId" <> '00000000-0000-0000-0000-000000000001'
                           OR "Name" <> 'Home'
                           OR "Avatar" IS NOT NULL
                           OR "PinHash" IS NOT NULL
                           OR NOT "IsDefault")
                       OR EXISTS (
                        SELECT 1 FROM "PlaybackProgresses"
                        WHERE "UserId" <> '00000000-0000-0000-0000-000000000000')
                       OR EXISTS (
                        SELECT 1 FROM "PlaybackPreferences"
                        WHERE "UserId" <> '00000000-0000-0000-0000-000000000000')
                       OR EXISTS (
                        SELECT 1 FROM "ChatConversations"
                        WHERE "ProfileId" <> '00000000-0000-0000-0000-000000000000')
                       OR EXISTS (
                        SELECT 1 FROM "WebDavTokens"
                        WHERE "UserId" <> '00000000-0000-0000-0000-000000000001'
                           OR "Scope" <> 'read'
                           OR "VirtualRoot" <> '/'
                           OR "ExpiresAt" IS NOT NULL
                           OR "RevokedAt" IS NOT NULL)
                    THEN
                        RAISE EXCEPTION USING
                            ERRCODE = 'P0001',
                            MESSAGE = 'Cannot downgrade household identity safely: the old schema cannot represent current users, profiles, history, or device-token restrictions.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_ChatConversations_Profiles_ProfileId",
                table: "ChatConversations");

            migrationBuilder.DropForeignKey(
                name: "FK_PlaybackPreferences_Profiles_UserId",
                table: "PlaybackPreferences");

            migrationBuilder.DropForeignKey(
                name: "FK_PlaybackProgresses_Profiles_UserId",
                table: "PlaybackProgresses");

            migrationBuilder.DropForeignKey(
                name: "FK_WebDavTokens_Users_UserId",
                table: "WebDavTokens");

            migrationBuilder.DropTable(
                name: "LoginSessions");

            migrationBuilder.DropTable(
                name: "Profiles");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropIndex(
                name: "IX_WebDavTokens_UserId",
                table: "WebDavTokens");

            migrationBuilder.DropIndex(
                name: "IX_ChatConversations_ProfileId_UpdatedAt",
                table: "ChatConversations");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "WebDavTokens");

            migrationBuilder.DropColumn(
                name: "RevokedAt",
                table: "WebDavTokens");

            migrationBuilder.DropColumn(
                name: "Scope",
                table: "WebDavTokens");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "WebDavTokens");

            migrationBuilder.DropColumn(
                name: "VirtualRoot",
                table: "WebDavTokens");

            migrationBuilder.DropColumn(
                name: "ProfileId",
                table: "ChatConversations");
        }
    }
}
