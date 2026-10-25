using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LutzPath.JobApplicationService.Migrations
{
    /// <inheritdoc />
    public partial class AddGoogleSheetSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GoogleSheetBindings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SpreadsheetId = table.Column<string>(type: "TEXT", nullable: false),
                    SheetId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceId = table.Column<string>(type: "TEXT", nullable: false),
                    JobId = table.Column<int>(type: "INTEGER", nullable: false),
                    ApplicationId = table.Column<int>(type: "INTEGER", nullable: false),
                    LastSourceHash = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoogleSheetBindings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GoogleSheetSyncStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LastAttemptUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastSuccessUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", nullable: true),
                    Created = table.Column<int>(type: "INTEGER", nullable: false),
                    Updated = table.Column<int>(type: "INTEGER", nullable: false),
                    Skipped = table.Column<int>(type: "INTEGER", nullable: false),
                    WarningsJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoogleSheetSyncStates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GoogleSheetBindings_SpreadsheetId_SheetId_SourceId",
                table: "GoogleSheetBindings",
                columns: new[] { "SpreadsheetId", "SheetId", "SourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GoogleSheetBindings_SpreadsheetId_SheetId_UserId_JobId",
                table: "GoogleSheetBindings",
                columns: new[] { "SpreadsheetId", "SheetId", "UserId", "JobId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GoogleSheetBindings");

            migrationBuilder.DropTable(
                name: "GoogleSheetSyncStates");
        }
    }
}
