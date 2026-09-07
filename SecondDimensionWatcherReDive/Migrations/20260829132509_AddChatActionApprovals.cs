using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SecondDimensionWatcherReDive.Migrations
{
    /// <inheritdoc />
    public partial class AddChatActionApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChatPendingActions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToolCallId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ToolName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RiskLevel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProtectedParameters = table.Column<string>(type: "text", nullable: false),
                    ParameterHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProtectedApprovalToken = table.Column<string>(type: "text", nullable: false),
                    ApprovalTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ParameterSummary = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    ImpactSummary = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    IsReversible = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExecutionStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResultSummary = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ErrorSummary = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ToolResultJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatPendingActions", x => x.Id);
                    table.CheckConstraint("CK_ChatPendingActions_Expiry", "\"ExpiresAt\" > \"CreatedAt\"");
                });

            migrationBuilder.CreateTable(
                name: "ChatActionAudits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ActionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToolName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RiskLevel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Event = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ParameterHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ParameterSummary = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Detail = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatActionAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatActionAudits_ChatPendingActions_ActionId",
                        column: x => x.ActionId,
                        principalTable: "ChatPendingActions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatActionAudits_ActionId",
                table: "ChatActionAudits",
                column: "ActionId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatActionAudits_UserId_ConversationId_CreatedAt",
                table: "ChatActionAudits",
                columns: new[] { "UserId", "ConversationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatPendingActions_ConversationId_ToolCallId",
                table: "ChatPendingActions",
                columns: new[] { "ConversationId", "ToolCallId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatPendingActions_State_ExpiresAt",
                table: "ChatPendingActions",
                columns: new[] { "State", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatPendingActions_UserId_ConversationId_State",
                table: "ChatPendingActions",
                columns: new[] { "UserId", "ConversationId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatPendingActions_UserId_ConversationId_ToolCallId",
                table: "ChatPendingActions",
                columns: new[] { "UserId", "ConversationId", "ToolCallId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatActionAudits");

            migrationBuilder.DropTable(
                name: "ChatPendingActions");
        }
    }
}
