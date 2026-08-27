using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecondDimensionWatcherReDive.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaybackAndIncidentInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DownloadAttemptId",
                table: "AnimationInfo",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DownloadCancellationId",
                table: "AnimationInfo",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Incidents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Severity = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Detail = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    SourceId = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    DetectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    LastRetryAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastRetryError = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Incidents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlaybackPreferences",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubtitleLanguage = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SubtitleTrackLabel = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    AudioLanguage = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AudioTrackLabel = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    AutoPlayNext = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaybackPreferences", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "PlaybackProgresses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AnimationInfoId = table.Column<Guid>(type: "uuid", nullable: false),
                    VirtualPath = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    PositionSeconds = table.Column<double>(type: "double precision", nullable: false),
                    DurationSeconds = table.Column<double>(type: "double precision", nullable: false),
                    IsWatched = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    WatchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaybackProgresses", x => x.Id);
                    table.CheckConstraint("CK_PlaybackProgresses_Duration_NonNegative", "\"DurationSeconds\" >= 0");
                    table.CheckConstraint("CK_PlaybackProgresses_Position_NonNegative", "\"PositionSeconds\" >= 0");
                    table.ForeignKey(
                        name: "FK_PlaybackProgresses_AnimationInfo_AnimationInfoId",
                        column: x => x.AnimationInfoId,
                        principalTable: "AnimationInfo",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_Fingerprint",
                table: "Incidents",
                column: "Fingerprint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_ResolvedAt_Type_UpdatedAt",
                table: "Incidents",
                columns: new[] { "ResolvedAt", "Type", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PlaybackProgresses_AnimationInfoId",
                table: "PlaybackProgresses",
                column: "AnimationInfoId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaybackProgresses_UserId_AnimationInfoId_VirtualPath",
                table: "PlaybackProgresses",
                columns: new[] { "UserId", "AnimationInfoId", "VirtualPath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlaybackProgresses_UserId_IsWatched_UpdatedAt",
                table: "PlaybackProgresses",
                columns: new[] { "UserId", "IsWatched", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Incidents");

            migrationBuilder.DropTable(
                name: "PlaybackPreferences");

            migrationBuilder.DropTable(
                name: "PlaybackProgresses");

            migrationBuilder.DropColumn(
                name: "DownloadAttemptId",
                table: "AnimationInfo");

            migrationBuilder.DropColumn(
                name: "DownloadCancellationId",
                table: "AnimationInfo");
        }
    }
}
