using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecondDimensionWatcherReDive.Migrations
{
    /// <inheritdoc />
    public partial class AddPasskeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PasswordRemoved",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "PasskeyCeremonies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Origin = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    RelyingPartyId = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    BrowserBindingHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OptionsJson = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PasskeyCeremonies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PasskeyCeremonies_LoginSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "LoginSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PasskeyCeremonies_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PasskeyCredentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CredentialId = table.Column<byte[]>(type: "bytea", maxLength: 1024, nullable: false),
                    PublicKey = table.Column<byte[]>(type: "bytea", nullable: false),
                    SignCount = table.Column<long>(type: "bigint", nullable: false),
                    RelyingPartyId = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsBackupEligible = table.Column<bool>(type: "boolean", nullable: false),
                    IsBackedUp = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PasskeyCredentials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PasskeyCredentials_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PasskeyCeremonies_ExpiresAt",
                table: "PasskeyCeremonies",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_PasskeyCeremonies_SessionId",
                table: "PasskeyCeremonies",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_PasskeyCeremonies_UserId",
                table: "PasskeyCeremonies",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PasskeyCredentials_CredentialId",
                table: "PasskeyCredentials",
                column: "CredentialId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PasskeyCredentials_UserId_RelyingPartyId",
                table: "PasskeyCredentials",
                columns: new[] { "UserId", "RelyingPartyId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "Users" WHERE "PasswordRemoved") THEN
                        RAISE EXCEPTION 'Cannot remove Passkeys while passwordless accounts exist; restore a compatible backup instead.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "PasskeyCeremonies");

            migrationBuilder.DropTable(
                name: "PasskeyCredentials");

            migrationBuilder.DropColumn(
                name: "PasswordRemoved",
                table: "Users");
        }
    }
}
