using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cyberpilot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddShasToPipelineRun : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CyberpilotSha",
                table: "PipelineRuns",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetRepoSha",
                table: "PipelineRuns",
                type: "TEXT",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CyberpilotSha",
                table: "PipelineRuns");

            migrationBuilder.DropColumn(
                name: "TargetRepoSha",
                table: "PipelineRuns");
        }
    }
}
