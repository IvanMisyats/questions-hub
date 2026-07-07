using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuestionsHub.Blazor.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddResultsSourceTitle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "ResultsSources",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Title",
                table: "ResultsSources");
        }
    }
}
