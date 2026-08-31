using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecondDimensionWatcherReDive.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationsAndTodoCenter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NotificationOutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeduplicationKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Type = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Body = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    DeepLink = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationOutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TodoItemStates",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ReadAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SnoozedUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TodoItemStates", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnimationInfo_AutomationDisposition_PublishTime",
                table: "AnimationInfo",
                columns: new[] { "AutomationDisposition", "PublishTime" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutboxMessages_DeduplicationKey",
                table: "NotificationOutboxMessages",
                column: "DeduplicationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutboxMessages_Status_NextAttemptAt",
                table: "NotificationOutboxMessages",
                columns: new[] { "Status", "NextAttemptAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AnimationInfo_AutomationDisposition_PublishTime",
                table: "AnimationInfo");

            migrationBuilder.DropTable(
                name: "NotificationOutboxMessages");

            migrationBuilder.DropTable(
                name: "TodoItemStates");
        }
    }
}
