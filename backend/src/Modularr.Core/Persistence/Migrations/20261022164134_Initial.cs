using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Modularr.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QualityProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    UpgradeAllowed = table.Column<bool>(type: "INTEGER", nullable: false),
                    Cutoff = table.Column<int>(type: "INTEGER", nullable: false),
                    Items = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QualityProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Movies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TmdbId = table.Column<int>(type: "INTEGER", nullable: false),
                    ImdbId = table.Column<string>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    Monitored = table.Column<bool>(type: "INTEGER", nullable: false),
                    Path = table.Column<string>(type: "TEXT", nullable: false),
                    QualityProfileId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Movies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Movies_QualityProfiles_QualityProfileId",
                        column: x => x.QualityProfileId,
                        principalTable: "QualityProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Series",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TvdbId = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    SortTitle = table.Column<string>(type: "TEXT", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    SeriesType = table.Column<int>(type: "INTEGER", nullable: false),
                    Monitored = table.Column<bool>(type: "INTEGER", nullable: false),
                    Path = table.Column<string>(type: "TEXT", nullable: false),
                    QualityProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                    Added = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Seasons = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Series", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Series_QualityProfiles_QualityProfileId",
                        column: x => x.QualityProfileId,
                        principalTable: "QualityProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Blocklist",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: true),
                    EpisodeIds = table.Column<string>(type: "TEXT", nullable: false),
                    MovieId = table.Column<int>(type: "INTEGER", nullable: true),
                    SourceTitle = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    Quality = table.Column<string>(type: "TEXT", nullable: false),
                    Languages = table.Column<string>(type: "TEXT", nullable: false),
                    InfoHash = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    Indexer = table.Column<string>(type: "TEXT", nullable: true),
                    Reason = table.Column<string>(type: "TEXT", nullable: true),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Blocklist", x => x.Id);
                    table.CheckConstraint("CK_BlocklistItem_SeriesOrMovie", "(SeriesId IS NULL) <> (MovieId IS NULL)");
                    table.ForeignKey(
                        name: "FK_Blocklist_Movies_MovieId",
                        column: x => x.MovieId,
                        principalTable: "Movies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Blocklist_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MediaFiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: true),
                    MovieId = table.Column<int>(type: "INTEGER", nullable: true),
                    Path = table.Column<string>(type: "TEXT", nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    Quality = table.Column<string>(type: "TEXT", nullable: false),
                    Languages = table.Column<string>(type: "TEXT", nullable: false),
                    ReleaseGroup = table.Column<string>(type: "TEXT", nullable: true),
                    DateAdded = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaFiles", x => x.Id);
                    table.CheckConstraint("CK_MediaFile_SeriesOrMovie", "(SeriesId IS NULL) <> (MovieId IS NULL)");
                    table.ForeignKey(
                        name: "FK_MediaFiles_Movies_MovieId",
                        column: x => x.MovieId,
                        principalTable: "Movies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MediaFiles_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrackedDownloads",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    InfoHash = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: true),
                    EpisodeIds = table.Column<string>(type: "TEXT", nullable: false),
                    MovieId = table.Column<int>(type: "INTEGER", nullable: true),
                    ReleaseTitle = table.Column<string>(type: "TEXT", nullable: false),
                    Indexer = table.Column<string>(type: "TEXT", nullable: true),
                    State = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackedDownloads", x => x.Id);
                    table.CheckConstraint("CK_TrackedDownload_SeriesOrMovie", "(SeriesId IS NULL) <> (MovieId IS NULL)");
                    table.ForeignKey(
                        name: "FK_TrackedDownloads_Movies_MovieId",
                        column: x => x.MovieId,
                        principalTable: "Movies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TrackedDownloads_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Episodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: false),
                    SeasonNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    EpisodeNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    AbsoluteEpisodeNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    AirDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", nullable: true),
                    Monitored = table.Column<bool>(type: "INTEGER", nullable: false),
                    MediaFileId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Episodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Episodes_MediaFiles_MediaFileId",
                        column: x => x.MediaFileId,
                        principalTable: "MediaFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Episodes_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "History",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EventType = table.Column<int>(type: "INTEGER", nullable: false),
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: true),
                    EpisodeId = table.Column<int>(type: "INTEGER", nullable: true),
                    MovieId = table.Column<int>(type: "INTEGER", nullable: true),
                    SourceTitle = table.Column<string>(type: "TEXT", nullable: false),
                    Quality = table.Column<string>(type: "TEXT", nullable: false),
                    Languages = table.Column<string>(type: "TEXT", nullable: false),
                    InfoHash = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    Data = table.Column<string>(type: "TEXT", nullable: false),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_History", x => x.Id);
                    table.CheckConstraint("CK_HistoryItem_SeriesOrMovie", "(SeriesId IS NULL) <> (MovieId IS NULL)");
                    table.ForeignKey(
                        name: "FK_History_Episodes_EpisodeId",
                        column: x => x.EpisodeId,
                        principalTable: "Episodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_History_Movies_MovieId",
                        column: x => x.MovieId,
                        principalTable: "Movies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_History_Series_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "Series",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "QualityProfiles",
                columns: new[] { "Id", "Cutoff", "Items", "Name", "UpgradeAllowed" },
                values: new object[,]
                {
                    { 1, 20, "[{\"Name\":null,\"Qualities\":[0],\"Allowed\":false},{\"Name\":null,\"Qualities\":[24],\"Allowed\":true},{\"Name\":null,\"Qualities\":[25],\"Allowed\":true},{\"Name\":null,\"Qualities\":[26],\"Allowed\":true},{\"Name\":null,\"Qualities\":[27],\"Allowed\":true},{\"Name\":null,\"Qualities\":[29],\"Allowed\":true},{\"Name\":null,\"Qualities\":[28],\"Allowed\":true},{\"Name\":null,\"Qualities\":[1],\"Allowed\":true},{\"Name\":null,\"Qualities\":[2],\"Allowed\":true},{\"Name\":null,\"Qualities\":[23],\"Allowed\":true},{\"Name\":\"WEB 480p\",\"Qualities\":[8,12],\"Allowed\":true},{\"Name\":null,\"Qualities\":[20],\"Allowed\":true},{\"Name\":null,\"Qualities\":[21],\"Allowed\":true},{\"Name\":null,\"Qualities\":[4],\"Allowed\":true},{\"Name\":\"WEB 720p\",\"Qualities\":[5,14],\"Allowed\":true},{\"Name\":null,\"Qualities\":[6],\"Allowed\":true},{\"Name\":null,\"Qualities\":[9],\"Allowed\":true},{\"Name\":\"WEB 1080p\",\"Qualities\":[3,15],\"Allowed\":true},{\"Name\":null,\"Qualities\":[7],\"Allowed\":true},{\"Name\":null,\"Qualities\":[30],\"Allowed\":true},{\"Name\":null,\"Qualities\":[16],\"Allowed\":true},{\"Name\":\"WEB 2160p\",\"Qualities\":[18,17],\"Allowed\":true},{\"Name\":null,\"Qualities\":[19],\"Allowed\":true},{\"Name\":null,\"Qualities\":[31],\"Allowed\":true},{\"Name\":null,\"Qualities\":[22],\"Allowed\":true},{\"Name\":null,\"Qualities\":[10],\"Allowed\":false}]", "Any", false },
                    { 2, 20, "[{\"Name\":null,\"Qualities\":[0],\"Allowed\":false},{\"Name\":null,\"Qualities\":[24],\"Allowed\":true},{\"Name\":null,\"Qualities\":[25],\"Allowed\":true},{\"Name\":null,\"Qualities\":[26],\"Allowed\":true},{\"Name\":null,\"Qualities\":[27],\"Allowed\":true},{\"Name\":null,\"Qualities\":[29],\"Allowed\":true},{\"Name\":null,\"Qualities\":[28],\"Allowed\":true},{\"Name\":null,\"Qualities\":[1],\"Allowed\":true},{\"Name\":null,\"Qualities\":[2],\"Allowed\":true},{\"Name\":null,\"Qualities\":[23],\"Allowed\":false},{\"Name\":\"WEB 480p\",\"Qualities\":[8,12],\"Allowed\":true},{\"Name\":null,\"Qualities\":[20],\"Allowed\":true},{\"Name\":null,\"Qualities\":[21],\"Allowed\":true},{\"Name\":null,\"Qualities\":[4],\"Allowed\":false},{\"Name\":\"WEB 720p\",\"Qualities\":[5,14],\"Allowed\":false},{\"Name\":null,\"Qualities\":[6],\"Allowed\":false},{\"Name\":null,\"Qualities\":[9],\"Allowed\":false},{\"Name\":\"WEB 1080p\",\"Qualities\":[3,15],\"Allowed\":false},{\"Name\":null,\"Qualities\":[7],\"Allowed\":false},{\"Name\":null,\"Qualities\":[30],\"Allowed\":false},{\"Name\":null,\"Qualities\":[16],\"Allowed\":false},{\"Name\":\"WEB 2160p\",\"Qualities\":[18,17],\"Allowed\":false},{\"Name\":null,\"Qualities\":[19],\"Allowed\":false},{\"Name\":null,\"Qualities\":[31],\"Allowed\":false},{\"Name\":null,\"Qualities\":[22],\"Allowed\":false},{\"Name\":null,\"Qualities\":[10],\"Allowed\":false}]", "SD", false },
                    { 3, 6, "[{\"Name\":null,\"Qualities\":[0],\"Allowed\":false},{\"Name\":null,\"Qualities\":[24],\"Allowed\":false},{\"Name\":null,\"Qualities\":[25],\"Allowed\":false},{\"Name\":null,\"Qualities\":[26],\"Allowed\":false},{\"Name\":null,\"Qualities\":[27],\"Allowed\":false},{\"Name\":null,\"Qualities\":[29],\"Allowed\":false},{\"Name\":null,\"Qualities\":[28],\"Allowed\":false},{\"Name\":null,\"Qualities\":[1],\"Allowed\":false},{\"Name\":null,\"Qualities\":[2],\"Allowed\":false},{\"Name\":null,\"Qualities\":[23],\"Allowed\":false},{\"Name\":\"WEB 480p\",\"Qualities\":[8,12],\"Allowed\":false},{\"Name\":null,\"Qualities\":[20],\"Allowed\":false},{\"Name\":null,\"Qualities\":[21],\"Allowed\":false},{\"Name\":null,\"Qualities\":[4],\"Allowed\":true},{\"Name\":\"WEB 720p\",\"Qualities\":[5,14],\"Allowed\":true},{\"Name\":null,\"Qualities\":[6],\"Allowed\":true},{\"Name\":null,\"Qualities\":[9],\"Allowed\":false},{\"Name\":\"WEB 1080p\",\"Qualities\":[3,15],\"Allowed\":false},{\"Name\":null,\"Qualities\":[7],\"Allowed\":false},{\"Name\":null,\"Qualities\":[30],\"Allowed\":false},{\"Name\":null,\"Qualities\":[16],\"Allowed\":false},{\"Name\":\"WEB 2160p\",\"Qualities\":[18,17],\"Allowed\":false},{\"Name\":null,\"Qualities\":[19],\"Allowed\":false},{\"Name\":null,\"Qualities\":[31],\"Allowed\":false},{\"Name\":null,\"Qualities\":[22],\"Allowed\":false},{\"Name\":null,\"Qualities\":[10],\"Allowed\":false}]", "HD-720p", false },
                    { 4, 7, "[{\"Name\":null,\"Qualities\":[0],\"Allowed\":false},{\"Name\":null,\"Qualities\":[24],\"Allowed\":false},{\"Name\":null,\"Qualities\":[25],\"Allowed\":false},{\"Name\":null,\"Qualities\":[26],\"Allowed\":false},{\"Name\":null,\"Qualities\":[27],\"Allowed\":false},{\"Name\":null,\"Qualities\":[29],\"Allowed\":false},{\"Name\":null,\"Qualities\":[28],\"Allowed\":false},{\"Name\":null,\"Qualities\":[1],\"Allowed\":false},{\"Name\":null,\"Qualities\":[2],\"Allowed\":false},{\"Name\":null,\"Qualities\":[23],\"Allowed\":false},{\"Name\":\"WEB 480p\",\"Qualities\":[8,12],\"Allowed\":false},{\"Name\":null,\"Qualities\":[20],\"Allowed\":false},{\"Name\":null,\"Qualities\":[21],\"Allowed\":false},{\"Name\":null,\"Qualities\":[4],\"Allowed\":false},{\"Name\":\"WEB 720p\",\"Qualities\":[5,14],\"Allowed\":false},{\"Name\":null,\"Qualities\":[6],\"Allowed\":false},{\"Name\":null,\"Qualities\":[9],\"Allowed\":true},{\"Name\":\"WEB 1080p\",\"Qualities\":[3,15],\"Allowed\":true},{\"Name\":null,\"Qualities\":[7],\"Allowed\":true},{\"Name\":null,\"Qualities\":[30],\"Allowed\":true},{\"Name\":null,\"Qualities\":[16],\"Allowed\":false},{\"Name\":\"WEB 2160p\",\"Qualities\":[18,17],\"Allowed\":false},{\"Name\":null,\"Qualities\":[19],\"Allowed\":false},{\"Name\":null,\"Qualities\":[31],\"Allowed\":false},{\"Name\":null,\"Qualities\":[22],\"Allowed\":false},{\"Name\":null,\"Qualities\":[10],\"Allowed\":false}]", "HD-1080p", false },
                    { 5, 31, "[{\"Name\":null,\"Qualities\":[0],\"Allowed\":false},{\"Name\":null,\"Qualities\":[24],\"Allowed\":false},{\"Name\":null,\"Qualities\":[25],\"Allowed\":false},{\"Name\":null,\"Qualities\":[26],\"Allowed\":false},{\"Name\":null,\"Qualities\":[27],\"Allowed\":false},{\"Name\":null,\"Qualities\":[29],\"Allowed\":false},{\"Name\":null,\"Qualities\":[28],\"Allowed\":false},{\"Name\":null,\"Qualities\":[1],\"Allowed\":false},{\"Name\":null,\"Qualities\":[2],\"Allowed\":false},{\"Name\":null,\"Qualities\":[23],\"Allowed\":false},{\"Name\":\"WEB 480p\",\"Qualities\":[8,12],\"Allowed\":false},{\"Name\":null,\"Qualities\":[20],\"Allowed\":false},{\"Name\":null,\"Qualities\":[21],\"Allowed\":false},{\"Name\":null,\"Qualities\":[4],\"Allowed\":false},{\"Name\":\"WEB 720p\",\"Qualities\":[5,14],\"Allowed\":false},{\"Name\":null,\"Qualities\":[6],\"Allowed\":false},{\"Name\":null,\"Qualities\":[9],\"Allowed\":false},{\"Name\":\"WEB 1080p\",\"Qualities\":[3,15],\"Allowed\":false},{\"Name\":null,\"Qualities\":[7],\"Allowed\":false},{\"Name\":null,\"Qualities\":[30],\"Allowed\":false},{\"Name\":null,\"Qualities\":[16],\"Allowed\":true},{\"Name\":\"WEB 2160p\",\"Qualities\":[18,17],\"Allowed\":true},{\"Name\":null,\"Qualities\":[19],\"Allowed\":true},{\"Name\":null,\"Qualities\":[31],\"Allowed\":true},{\"Name\":null,\"Qualities\":[22],\"Allowed\":false},{\"Name\":null,\"Qualities\":[10],\"Allowed\":false}]", "Ultra-HD", false },
                    { 6, 6, "[{\"Name\":null,\"Qualities\":[0],\"Allowed\":false},{\"Name\":null,\"Qualities\":[24],\"Allowed\":false},{\"Name\":null,\"Qualities\":[25],\"Allowed\":false},{\"Name\":null,\"Qualities\":[26],\"Allowed\":false},{\"Name\":null,\"Qualities\":[27],\"Allowed\":false},{\"Name\":null,\"Qualities\":[29],\"Allowed\":false},{\"Name\":null,\"Qualities\":[28],\"Allowed\":false},{\"Name\":null,\"Qualities\":[1],\"Allowed\":false},{\"Name\":null,\"Qualities\":[2],\"Allowed\":false},{\"Name\":null,\"Qualities\":[23],\"Allowed\":false},{\"Name\":\"WEB 480p\",\"Qualities\":[8,12],\"Allowed\":false},{\"Name\":null,\"Qualities\":[20],\"Allowed\":false},{\"Name\":null,\"Qualities\":[21],\"Allowed\":false},{\"Name\":null,\"Qualities\":[4],\"Allowed\":true},{\"Name\":\"WEB 720p\",\"Qualities\":[5,14],\"Allowed\":true},{\"Name\":null,\"Qualities\":[6],\"Allowed\":true},{\"Name\":null,\"Qualities\":[9],\"Allowed\":true},{\"Name\":\"WEB 1080p\",\"Qualities\":[3,15],\"Allowed\":true},{\"Name\":null,\"Qualities\":[7],\"Allowed\":true},{\"Name\":null,\"Qualities\":[30],\"Allowed\":true},{\"Name\":null,\"Qualities\":[16],\"Allowed\":false},{\"Name\":\"WEB 2160p\",\"Qualities\":[18,17],\"Allowed\":false},{\"Name\":null,\"Qualities\":[19],\"Allowed\":false},{\"Name\":null,\"Qualities\":[31],\"Allowed\":false},{\"Name\":null,\"Qualities\":[22],\"Allowed\":false},{\"Name\":null,\"Qualities\":[10],\"Allowed\":false}]", "HD - 720p/1080p", false }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Blocklist_InfoHash",
                table: "Blocklist",
                column: "InfoHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Blocklist_MovieId",
                table: "Blocklist",
                column: "MovieId");

            migrationBuilder.CreateIndex(
                name: "IX_Blocklist_SeriesId",
                table: "Blocklist",
                column: "SeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_MediaFileId",
                table: "Episodes",
                column: "MediaFileId");

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_SeriesId_SeasonNumber_EpisodeNumber",
                table: "Episodes",
                columns: new[] { "SeriesId", "SeasonNumber", "EpisodeNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_History_EpisodeId",
                table: "History",
                column: "EpisodeId");

            migrationBuilder.CreateIndex(
                name: "IX_History_InfoHash",
                table: "History",
                column: "InfoHash");

            migrationBuilder.CreateIndex(
                name: "IX_History_MovieId",
                table: "History",
                column: "MovieId");

            migrationBuilder.CreateIndex(
                name: "IX_History_SeriesId",
                table: "History",
                column: "SeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaFiles_MovieId",
                table: "MediaFiles",
                column: "MovieId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaFiles_Path",
                table: "MediaFiles",
                column: "Path",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaFiles_SeriesId",
                table: "MediaFiles",
                column: "SeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_Movies_QualityProfileId",
                table: "Movies",
                column: "QualityProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_Movies_TmdbId",
                table: "Movies",
                column: "TmdbId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QualityProfiles_Name",
                table: "QualityProfiles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Series_QualityProfileId",
                table: "Series",
                column: "QualityProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_Series_TvdbId",
                table: "Series",
                column: "TvdbId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrackedDownloads_InfoHash",
                table: "TrackedDownloads",
                column: "InfoHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrackedDownloads_MovieId",
                table: "TrackedDownloads",
                column: "MovieId");

            migrationBuilder.CreateIndex(
                name: "IX_TrackedDownloads_SeriesId",
                table: "TrackedDownloads",
                column: "SeriesId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Blocklist");

            migrationBuilder.DropTable(
                name: "History");

            migrationBuilder.DropTable(
                name: "TrackedDownloads");

            migrationBuilder.DropTable(
                name: "Episodes");

            migrationBuilder.DropTable(
                name: "MediaFiles");

            migrationBuilder.DropTable(
                name: "Movies");

            migrationBuilder.DropTable(
                name: "Series");

            migrationBuilder.DropTable(
                name: "QualityProfiles");
        }
    }
}
