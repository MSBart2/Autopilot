using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cyberpilot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRetryCountToPipelineStageLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RetryCount",
                table: "PipelineStageLogs",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RetryCount",
                table: "PipelineStageLogs");
        }
    }
}
