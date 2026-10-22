using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modularr.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProfilesAndLanguages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Language",
                table: "Series",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OriginalLanguage",
                table: "Series",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Language",
                table: "Movies",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OriginalLanguage",
                table: "Movies",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // A profile language other than Any becomes the override of the items using the profile, so their searches do not change.
            migrationBuilder.Sql("UPDATE Series SET Language = (SELECT p.Language FROM QualityProfiles p WHERE p.Id = Series.QualityProfileId AND p.Language <> -1);");
            migrationBuilder.Sql("UPDATE Movies SET Language = (SELECT p.Language FROM QualityProfiles p WHERE p.Id = Movies.QualityProfileId AND p.Language <> -1);");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "QualityProfiles");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Language",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "OriginalLanguage",
                table: "Series");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "OriginalLanguage",
                table: "Movies");

            migrationBuilder.AddColumn<int>(
                name: "Language",
                table: "QualityProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.UpdateData(
                table: "QualityProfiles",
                keyColumn: "Id",
                keyValue: 1,
                column: "Language",
                value: -1);

            migrationBuilder.UpdateData(
                table: "QualityProfiles",
                keyColumn: "Id",
                keyValue: 2,
                column: "Language",
                value: -1);

            migrationBuilder.UpdateData(
                table: "QualityProfiles",
                keyColumn: "Id",
                keyValue: 3,
                column: "Language",
                value: -1);

            migrationBuilder.UpdateData(
                table: "QualityProfiles",
                keyColumn: "Id",
                keyValue: 4,
                column: "Language",
                value: -1);

            migrationBuilder.UpdateData(
                table: "QualityProfiles",
                keyColumn: "Id",
                keyValue: 5,
                column: "Language",
                value: -1);

            migrationBuilder.UpdateData(
                table: "QualityProfiles",
                keyColumn: "Id",
                keyValue: 6,
                column: "Language",
                value: -1);
        }
    }
}
