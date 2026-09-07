using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecondDimensionWatcherReDive.Migrations
{
    /// <inheritdoc />
    public partial class AddWebPushNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Channel",
                table: "NotificationOutboxMessages",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "Webhook");

            // Move pre-channel webhook rows into the same explicit target
            // namespace used by the publisher. This preserves their deduplication
            // identity across the upgrade while keeping every channel target in
            // the table-wide unique-key domain.
            migrationBuilder.DropIndex(
                name: "IX_NotificationOutboxMessages_DeduplicationKey",
                table: "NotificationOutboxMessages");

            migrationBuilder.Sql(
                """
                UPDATE "NotificationOutboxMessages"
                SET "DeduplicationKey" = CASE
                    WHEN char_length('webhook:' || "DeduplicationKey") <= 256
                        THEN 'webhook:' || "DeduplicationKey"
                    ELSE left('webhook:' || "DeduplicationKey", 191)
                         || ':'
                         || encode(sha256(convert_to('webhook:' || "DeduplicationKey", 'UTF8')), 'hex')
                END;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutboxMessages_DeduplicationKey",
                table: "NotificationOutboxMessages",
                column: "DeduplicationKey",
                unique: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EventId",
                table: "NotificationOutboxMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE \"NotificationOutboxMessages\" SET \"EventId\" = \"Id\" WHERE \"EventId\" IS NULL;");

            migrationBuilder.AlterColumn<Guid>(
                name: "EventId",
                table: "NotificationOutboxMessages",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WebPushSubscriptionId",
                table: "NotificationOutboxMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WebPushSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EndpointHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProtectedEndpoint = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    ProtectedP256Dh = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    ProtectedAuth = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSuccessAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastFailureAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebPushSubscriptions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutboxMessages_WebPushSubscriptionId",
                table: "NotificationOutboxMessages",
                column: "WebPushSubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_WebPushSubscriptions_EndpointHash",
                table: "WebPushSubscriptions",
                column: "EndpointHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_NotificationOutboxMessages_DeduplicationKey",
                table: "NotificationOutboxMessages");

            migrationBuilder.Sql(
                """
                DELETE FROM "NotificationOutboxMessages"
                WHERE "Channel" = 'WebPush';

                UPDATE "NotificationOutboxMessages"
                SET "DeduplicationKey" = substring("DeduplicationKey" from 9)
                WHERE "Channel" = 'Webhook'
                  AND "DeduplicationKey" LIKE 'webhook:%';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutboxMessages_DeduplicationKey",
                table: "NotificationOutboxMessages",
                column: "DeduplicationKey",
                unique: true);

            migrationBuilder.DropTable(
                name: "WebPushSubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_NotificationOutboxMessages_WebPushSubscriptionId",
                table: "NotificationOutboxMessages");

            migrationBuilder.DropColumn(
                name: "Channel",
                table: "NotificationOutboxMessages");

            migrationBuilder.DropColumn(
                name: "EventId",
                table: "NotificationOutboxMessages");

            migrationBuilder.DropColumn(
                name: "WebPushSubscriptionId",
                table: "NotificationOutboxMessages");
        }
    }
}
