using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cyberpilot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStageExecutionMetricsToPipelineStageLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApiCallIds",
                table: "PipelineStageLogs",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CacheReadTokens",
                table: "PipelineStageLogs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CacheWriteTokens",
                table: "PipelineStageLogs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "DurationMs",
                table: "PipelineStageLogs",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FailedToolCallCount",
                table: "PipelineStageLogs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Model",
                table: "PipelineStageLogs",
                type: "TEXT",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PremiumRequestCost",
                table: "PipelineStageLogs",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderCallIds",
                table: "PipelineStageLogs",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ReachedIdle",
                table: "PipelineStageLogs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReasoningTokens",
                table: "PipelineStageLogs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SessionErrorCount",
                table: "PipelineStageLogs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ToolCallCount",
                table: "PipelineStageLogs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TurnCount",
                table: "PipelineStageLogs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WasAborted",
                table: "PipelineStageLogs",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApiCallIds",
                table: "PipelineStageLogs");

            migrationBuilder.DropColumn(
                name: "CacheReadTokens",
                table: "PipelineStageLogs");

            migrationBuilder.DropColumn(
                name: "CacheWriteTokens",
                table: "PipelineStageLogs");

            migrationBuilder.DropColumn(
                name: "DurationMs",
                table: "PipelineStageLogs");

            migrationBuilder.DropColumn(
                name: "FailedToolCallCount",
                table: "PipelineStageLogs");

            migrationBuilder.DropColumn(
                name: "Model",
                table: "PipelineStageLogs");

            migrationBuilder.DropColumn(
                name: "PremiumRequestCost",
                table: "PipelineStageLogs");

            migrationBuilder.DropColumn(
                name: "ProviderCallIds",
                table: "PipelineStageLogs");

            migrationBuilder.DropColumn(
                name: "ReachedIdle",
                table: "PipelineStageLogs");

            migrationBuilder.DropColumn(
                name: "ReasoningTokens",
                table: "PipelineStageLogs");

            migrationBuilder.DropColumn(
                name: "SessionErrorCount",
                table: "PipelineStageLogs");

            migrationBuilder.DropColumn(
                name: "ToolCallCount",
                table: "PipelineStageLogs");

            migrationBuilder.DropColumn(
                name: "TurnCount",
                table: "PipelineStageLogs");

            migrationBuilder.DropColumn(
                name: "WasAborted",
                table: "PipelineStageLogs");
        }
    }
}
