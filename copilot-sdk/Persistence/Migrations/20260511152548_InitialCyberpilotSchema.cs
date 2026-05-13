using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cyberpilot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCyberpilotSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PipelineRuns",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    IssueNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Repository = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    BranchName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Model = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    CurrentStage = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    PrUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Error = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    WorktreePath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TriggeredBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    SkipDeliver = table.Column<bool>(type: "INTEGER", nullable: false),
                    StageTimeoutMinutes = table.Column<double>(type: "REAL", nullable: false),
                    AllowMissingDocs = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PipelineRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PipelineStageLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RunId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    StageName = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Output = table.Column<string>(type: "TEXT", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PipelineStageLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PipelineStageLogs_PipelineRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "PipelineRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PipelineRuns_IssueNumber",
                table: "PipelineRuns",
                column: "IssueNumber");

            migrationBuilder.CreateIndex(
                name: "IX_PipelineRuns_Status",
                table: "PipelineRuns",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PipelineStageLogs_RunId",
                table: "PipelineStageLogs",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_PipelineStageLogs_StageName",
                table: "PipelineStageLogs",
                column: "StageName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PipelineStageLogs");

            migrationBuilder.DropTable(
                name: "PipelineRuns");
        }
    }
}
