using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cyberpilot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRemotePipelineFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "GitHubActionsRunId",
                table: "PipelineRuns",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsRemote",
                table: "PipelineRuns",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "IssueUrl",
                table: "PipelineRuns",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetRepository",
                table: "PipelineRuns",
                type: "TEXT",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GitHubActionsRunId",
                table: "PipelineRuns");

            migrationBuilder.DropColumn(
                name: "IsRemote",
                table: "PipelineRuns");

            migrationBuilder.DropColumn(
                name: "IssueUrl",
                table: "PipelineRuns");

            migrationBuilder.DropColumn(
                name: "TargetRepository",
                table: "PipelineRuns");
        }
    }
}
