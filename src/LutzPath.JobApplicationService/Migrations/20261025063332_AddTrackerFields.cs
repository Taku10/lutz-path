using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LutzPath.JobApplicationService.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackerFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AvailabilityCheckNotes",
                table: "Jobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "H1bVisaInformation",
                table: "Jobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastAvailabilityCheck",
                table: "Jobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LikelyInterviewProcess",
                table: "Jobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OptFriendlyInformation",
                table: "Jobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostingAvailability",
                table: "Jobs",
                type: "TEXT",
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.AddColumn<string>(
                name: "AdditionalSourceNotes",
                table: "Applications",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DateFound",
                table: "Applications",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DsaTopicsToKnow",
                table: "Applications",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InterviewPrepNotes",
                table: "Applications",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Priority",
                table: "Applications",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResumeTips",
                table: "Applications",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AvailabilityCheckNotes",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "H1bVisaInformation",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "LastAvailabilityCheck",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "LikelyInterviewProcess",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "OptFriendlyInformation",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "PostingAvailability",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "AdditionalSourceNotes",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "DateFound",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "DsaTopicsToKnow",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "InterviewPrepNotes",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "ResumeTips",
                table: "Applications");
        }
    }
}
