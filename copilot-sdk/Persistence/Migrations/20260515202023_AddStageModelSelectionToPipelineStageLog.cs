using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cyberpilot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStageModelSelectionToPipelineStageLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConfiguredModel",
                table: "PipelineStageLogs",
                type: "TEXT",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FallbackModel",
                table: "PipelineStageLogs",
                type: "TEXT",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FallbackReason",
                table: "PipelineStageLogs",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SelectedModel",
                table: "PipelineStageLogs",
                type: "TEXT",
                maxLength: 120,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConfiguredModel",
                table: "PipelineStageLogs");

            migrationBuilder.DropColumn(
                name: "FallbackModel",
                table: "PipelineStageLogs");

            migrationBuilder.DropColumn(
                name: "FallbackReason",
                table: "PipelineStageLogs");

            migrationBuilder.DropColumn(
                name: "SelectedModel",
                table: "PipelineStageLogs");
        }
    }
}
