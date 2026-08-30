using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SecondDimensionWatcherReDive.Migrations
{
    /// <inheritdoc />
    public partial class AddIncidentOccurrences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Occurrence",
                table: "Incidents",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Incidents_Occurrence_Positive",
                table: "Incidents",
                sql: "\"Occurrence\" > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Incidents_Occurrence_Positive",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "Occurrence",
                table: "Incidents");
        }
    }
}
