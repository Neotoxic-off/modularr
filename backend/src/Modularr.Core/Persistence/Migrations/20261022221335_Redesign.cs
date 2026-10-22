using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modularr.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Redesign : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Episodes_AirDate",
                table: "Episodes",
                column: "AirDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Episodes_AirDate",
                table: "Episodes");
        }
    }
}
