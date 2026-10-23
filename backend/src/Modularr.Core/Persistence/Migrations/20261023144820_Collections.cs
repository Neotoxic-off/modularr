using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modularr.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Collections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CollectionName",
                table: "Movies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CollectionTmdbId",
                table: "Movies",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Collections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TmdbId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Poster = table.Column<string>(type: "TEXT", nullable: true),
                    Backdrop = table.Column<string>(type: "TEXT", nullable: true),
                    Monitored = table.Column<bool>(type: "INTEGER", nullable: false),
                    QualityProfileId = table.Column<int>(type: "INTEGER", nullable: true),
                    Added = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Collections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Collections_QualityProfiles_QualityProfileId",
                        column: x => x.QualityProfileId,
                        principalTable: "QualityProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Movies_CollectionTmdbId",
                table: "Movies",
                column: "CollectionTmdbId");

            migrationBuilder.CreateIndex(
                name: "IX_Collections_QualityProfileId",
                table: "Collections",
                column: "QualityProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_Collections_TmdbId",
                table: "Collections",
                column: "TmdbId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Collections");

            migrationBuilder.DropIndex(
                name: "IX_Movies_CollectionTmdbId",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "CollectionName",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "CollectionTmdbId",
                table: "Movies");
        }
    }
}
