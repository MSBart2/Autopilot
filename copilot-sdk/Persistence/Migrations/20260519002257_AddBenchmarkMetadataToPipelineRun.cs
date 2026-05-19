using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cyberpilot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBenchmarkMetadataToPipelineRun : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BenchmarkIteration",
                table: "PipelineRuns",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BenchmarkRepeatGroup",
                table: "PipelineRuns",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExperimentVariant",
                table: "PipelineRuns",
                type: "TEXT",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BenchmarkIteration",
                table: "PipelineRuns");

            migrationBuilder.DropColumn(
                name: "BenchmarkRepeatGroup",
                table: "PipelineRuns");

            migrationBuilder.DropColumn(
                name: "ExperimentVariant",
                table: "PipelineRuns");
        }
    }
}
