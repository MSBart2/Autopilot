using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cyberpilot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSdkSessionPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ResumeBlockedReason",
                table: "PipelineStageLogs",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResumeEligibility",
                table: "PipelineStageLogs",
                type: "TEXT",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SdkSessionId",
                table: "PipelineStageLogs",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SessionCleanupAfter",
                table: "PipelineStageLogs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SessionState",
                table: "PipelineStageLogs",
                type: "TEXT",
                maxLength: 40,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResumeBlockedReason",
                table: "PipelineStageLogs");

            migrationBuilder.DropColumn(
                name: "ResumeEligibility",
                table: "PipelineStageLogs");

            migrationBuilder.DropColumn(
                name: "SdkSessionId",
                table: "PipelineStageLogs");

            migrationBuilder.DropColumn(
                name: "SessionCleanupAfter",
                table: "PipelineStageLogs");

            migrationBuilder.DropColumn(
                name: "SessionState",
                table: "PipelineStageLogs");
        }
    }
}
