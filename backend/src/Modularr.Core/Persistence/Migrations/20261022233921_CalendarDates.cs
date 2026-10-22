using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modularr.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CalendarDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "DigitalRelease",
                table: "Movies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "InCinemas",
                table: "Movies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PhysicalRelease",
                table: "Movies",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DigitalRelease",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "InCinemas",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "PhysicalRelease",
                table: "Movies");
        }
    }
}
