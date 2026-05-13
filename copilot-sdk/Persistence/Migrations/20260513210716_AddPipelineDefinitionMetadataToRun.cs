using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cyberpilot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPipelineDefinitionMetadataToRun : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContractVersion",
                table: "PipelineRuns",
                type: "TEXT",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PipelineDefinitionName",
                table: "PipelineRuns",
                type: "TEXT",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PipelineDefinitionVersion",
                table: "PipelineRuns",
                type: "TEXT",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PolicyProfileName",
                table: "PipelineRuns",
                type: "TEXT",
                maxLength: 80,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContractVersion",
                table: "PipelineRuns");

            migrationBuilder.DropColumn(
                name: "PipelineDefinitionName",
                table: "PipelineRuns");

            migrationBuilder.DropColumn(
                name: "PipelineDefinitionVersion",
                table: "PipelineRuns");

            migrationBuilder.DropColumn(
                name: "PolicyProfileName",
                table: "PipelineRuns");
        }
    }
}
