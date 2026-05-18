using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cyberpilot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPipelineToolFailures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PipelineToolFailures",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RunId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    StageLogId = table.Column<int>(type: "INTEGER", nullable: true),
                    StageName = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    ToolCallId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ToolName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PipelineToolFailures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PipelineToolFailures_PipelineRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "PipelineRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PipelineToolFailures_PipelineStageLogs_StageLogId",
                        column: x => x.StageLogId,
                        principalTable: "PipelineStageLogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PipelineToolFailures_RunId",
                table: "PipelineToolFailures",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_PipelineToolFailures_StageLogId",
                table: "PipelineToolFailures",
                column: "StageLogId");

            migrationBuilder.CreateIndex(
                name: "IX_PipelineToolFailures_StageName",
                table: "PipelineToolFailures",
                column: "StageName");

            migrationBuilder.CreateIndex(
                name: "IX_PipelineToolFailures_ToolName",
                table: "PipelineToolFailures",
                column: "ToolName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PipelineToolFailures");
        }
    }
}
