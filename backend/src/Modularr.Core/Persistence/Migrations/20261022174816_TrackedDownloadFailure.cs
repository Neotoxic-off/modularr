using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modularr.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TrackedDownloadFailure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Message",
                table: "TrackedDownloads",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Regrabs",
                table: "TrackedDownloads",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Message",
                table: "TrackedDownloads");

            migrationBuilder.DropColumn(
                name: "Regrabs",
                table: "TrackedDownloads");
        }
    }
}
