using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace QuestionsHub.Blazor.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPackageResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QuestionStats",
                columns: table => new
                {
                    QuestionId = table.Column<int>(type: "integer", nullable: false),
                    CorrectCount = table.Column<int>(type: "integer", nullable: false),
                    TotalTeams = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionStats", x => x.QuestionId);
                    table.ForeignKey(
                        name: "FK_QuestionStats_Questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ResultsSources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PackageId = table.Column<int>(type: "integer", nullable: false),
                    Platform = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    Url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ExternalId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    DisplayUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    LoadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LoadError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ResultsAvailableAfter = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TeamsCount = table.Column<int>(type: "integer", nullable: true),
                    StatsMapped = table.Column<bool>(type: "boolean", nullable: false),
                    WarningsJson = table.Column<string>(type: "text", nullable: true),
                    RawPayload = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResultsSources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResultsSources_Packages_PackageId",
                        column: x => x.PackageId,
                        principalTable: "Packages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeamResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ResultsSourceId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Town = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ExternalTeamId = table.Column<int>(type: "integer", nullable: true),
                    Points = table.Column<decimal>(type: "numeric", nullable: false),
                    Position = table.Column<decimal>(type: "numeric", nullable: true),
                    ResultsByQuestionJson = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamResults_ResultsSources_ResultsSourceId",
                        column: x => x.ResultsSourceId,
                        principalTable: "ResultsSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ResultsSources_PackageId",
                table: "ResultsSources",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamResults_ResultsSourceId",
                table: "TeamResults",
                column: "ResultsSourceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuestionStats");

            migrationBuilder.DropTable(
                name: "TeamResults");

            migrationBuilder.DropTable(
                name: "ResultsSources");
        }
    }
}
