using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modularr.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PerUserRead : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Read",
                table: "Activities");

            migrationBuilder.AddColumn<int>(
                name: "ReadActivityUpTo",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReadActivityUpTo",
                table: "Users");

            migrationBuilder.AddColumn<bool>(
                name: "Read",
                table: "Activities",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }
    }
}
