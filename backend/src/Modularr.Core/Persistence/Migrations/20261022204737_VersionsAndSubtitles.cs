using System;
using System.Globalization;
using System.Linq;
using Microsoft.EntityFrameworkCore.Migrations;
using Modularr.Core.Languages;

#nullable disable

namespace Modularr.Core.Persistence.Migrations
{
    // The per-item language override becomes the version override: French -> VF, Original -> VO, Any and the other languages
    // keep their name (an audio language version). Going down, the French scene versions without a language (VFQ, VOSTFR,
    // MULTi) are lost.
    /// <inheritdoc />
    public partial class VersionsAndSubtitles : Migration
    {
        private static readonly Language[] Wantable = [.. Language.All.Where(l => l != Language.Unknown)];

        private static string VersionOf(string languageColumn) =>
            $"CASE {languageColumn} {string.Concat(Wantable.Select(l => string.Create(CultureInfo.InvariantCulture, $"WHEN {l.Id} THEN '{ReleaseVersion.FindByName(l.Name).Name}' ")))}END";

        private static string LanguageOf(string versionColumn) =>
            $"CASE {versionColumn} {string.Concat(Wantable.Select(l => string.Create(CultureInfo.InvariantCulture, $"WHEN '{ReleaseVersion.FindByName(l.Name).Name}' THEN {l.Id} ")))}END";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (string table in new[] { "Series", "Movies" })
            {
                migrationBuilder.AddColumn<string>(name: "Subtitles", table: table, type: "TEXT", nullable: true);
                migrationBuilder.AddColumn<string>(name: "Version", table: table, type: "TEXT", nullable: true);
                migrationBuilder.Sql($"UPDATE \"{table}\" SET \"Version\" = {VersionOf("\"Language\"")} WHERE \"Language\" IS NOT NULL;");
                migrationBuilder.DropColumn(name: "Language", table: table);
            }

            migrationBuilder.AddColumn<DateTime>(name: "SubtitlesSearched", table: "MediaFiles", type: "TEXT", nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (string table in new[] { "Series", "Movies" })
            {
                migrationBuilder.AddColumn<int>(name: "Language", table: table, type: "INTEGER", nullable: true);
                migrationBuilder.Sql($"UPDATE \"{table}\" SET \"Language\" = {LanguageOf("\"Version\"")} WHERE \"Version\" IS NOT NULL;");
                migrationBuilder.DropColumn(name: "Subtitles", table: table);
                migrationBuilder.DropColumn(name: "Version", table: table);
            }

            migrationBuilder.DropColumn(name: "SubtitlesSearched", table: "MediaFiles");
        }
    }
}
