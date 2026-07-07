using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuestionsHub.Blazor.Data.Migrations
{
    /// <inheritdoc />
    public partial class AdjustResultsSourceDiagnostics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Label",
                table: "ResultsSources");

            migrationBuilder.AddColumn<string>(
                name: "LoadErrorDetail",
                table: "ResultsSources",
                type: "character varying(8000)",
                maxLength: 8000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LoadErrorDetail",
                table: "ResultsSources");

            migrationBuilder.AddColumn<string>(
                name: "Label",
                table: "ResultsSources",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }
    }
}
