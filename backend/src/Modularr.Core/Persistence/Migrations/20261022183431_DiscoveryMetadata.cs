using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modularr.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DiscoveryMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Backdrop",
                table: "Series",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Certification",
                table: "Series",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Genres",
                table: "Series",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<decimal>(
                name: "Rating",
                table: "Series",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Backdrop",
                table: "Movies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Certification",
                table: "Movies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Genres",
                table: "Movies",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<decimal>(
                name: "Rating",
                table: "Movies",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Backdrop",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "Certification",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "Genres",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "Rating",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "Backdrop",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "Certification",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "Genres",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "Rating",
                table: "Movies");
        }
    }
}
