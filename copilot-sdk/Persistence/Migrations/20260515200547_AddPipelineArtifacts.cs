using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cyberpilot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPipelineArtifacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PipelineArtifacts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RunId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    StageLogId = table.Column<int>(type: "INTEGER", nullable: true),
                    StageName = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Uri = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    MediaType = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ContractVersion = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PipelineArtifacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PipelineArtifacts_PipelineRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "PipelineRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PipelineArtifacts_PipelineStageLogs_StageLogId",
                        column: x => x.StageLogId,
                        principalTable: "PipelineStageLogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PipelineArtifacts_Name",
                table: "PipelineArtifacts",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_PipelineArtifacts_RunId",
                table: "PipelineArtifacts",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_PipelineArtifacts_StageLogId",
                table: "PipelineArtifacts",
                column: "StageLogId");

            migrationBuilder.CreateIndex(
                name: "IX_PipelineArtifacts_StageName",
                table: "PipelineArtifacts",
                column: "StageName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PipelineArtifacts");
        }
    }
}
