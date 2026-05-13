using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cyberpilot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStructuredStageResultMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RetryReason",
                table: "PipelineStageLogs",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StageResultContractVersion",
                table: "PipelineStageLogs",
                type: "TEXT",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StageResultJson",
                table: "PipelineStageLogs",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RetryReason",
                table: "PipelineStageLogs");

            migrationBuilder.DropColumn(
                name: "StageResultContractVersion",
                table: "PipelineStageLogs");

            migrationBuilder.DropColumn(
                name: "StageResultJson",
                table: "PipelineStageLogs");
        }
    }
}
