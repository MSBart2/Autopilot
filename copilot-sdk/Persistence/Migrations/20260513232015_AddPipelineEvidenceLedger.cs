using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cyberpilot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPipelineEvidenceLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PipelineEvidence",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RunId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    StageLogId = table.Column<int>(type: "INTEGER", nullable: true),
                    StageName = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Uri = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    MediaType = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PipelineEvidence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PipelineEvidence_PipelineRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "PipelineRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PipelineEvidence_PipelineStageLogs_StageLogId",
                        column: x => x.StageLogId,
                        principalTable: "PipelineStageLogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PipelineEvidence_Kind",
                table: "PipelineEvidence",
                column: "Kind");

            migrationBuilder.CreateIndex(
                name: "IX_PipelineEvidence_RunId",
                table: "PipelineEvidence",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_PipelineEvidence_StageLogId",
                table: "PipelineEvidence",
                column: "StageLogId");

            migrationBuilder.CreateIndex(
                name: "IX_PipelineEvidence_StageName",
                table: "PipelineEvidence",
                column: "StageName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PipelineEvidence");
        }
    }
}
