using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuestionsHub.Blazor.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddQuestionOrderIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OrderIndex",
                table: "Questions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Populate OrderIndex for existing questions
            // Try to parse Number as integer, fallback to row order within each tour
            migrationBuilder.Sql("""
                WITH numbered AS (
                    SELECT
                        "Id",
                        "TourId",
                        "Number",
                        CASE
                            WHEN "Number" ~ '^[0-9]+$' THEN CAST("Number" AS INTEGER) - 1
                            ELSE ROW_NUMBER() OVER (PARTITION BY "TourId" ORDER BY "Id") - 1
                        END AS computed_order
                    FROM "Questions"
                )
                UPDATE "Questions" q
                SET "OrderIndex" = n.computed_order
                FROM numbered n
                WHERE q."Id" = n."Id";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OrderIndex",
                table: "Questions");
        }
    }
}
