using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecondDimensionWatcherReDive.Migrations
{
    /// <inheritdoc />
    public partial class AddPluginNotificationTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PluginProviderId",
                table: "NotificationOutboxMessages",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PluginPublisherIdentity",
                table: "NotificationOutboxMessages",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Older hosts cannot deserialize the Plugin channel or safely deliver its targets.
            // Keep the schema and pending events intact rather than silently discarding them.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "NotificationOutboxMessages" WHERE "Channel" = 'Plugin') THEN
                        RAISE EXCEPTION 'Cannot downgrade while plugin notification records exist. Restore a compatible backup instead.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropColumn(
                name: "PluginProviderId",
                table: "NotificationOutboxMessages");

            migrationBuilder.DropColumn(
                name: "PluginPublisherIdentity",
                table: "NotificationOutboxMessages");
        }
    }
}
