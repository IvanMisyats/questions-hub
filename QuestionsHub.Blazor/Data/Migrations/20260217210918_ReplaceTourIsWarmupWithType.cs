using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuestionsHub.Blazor.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceTourIsWarmupWithType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Step 1: Add new Type column (0 = Regular by default)
            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "Tours",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Step 2: Migrate data — set Type = 1 (Warmup) where IsWarmup was true
            migrationBuilder.Sql("""UPDATE "Tours" SET "Type" = 1 WHERE "IsWarmup" = true""");

            // Step 3: Drop the old IsWarmup column
            migrationBuilder.DropColumn(
                name: "IsWarmup",
                table: "Tours");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Step 1: Re-add IsWarmup column
            migrationBuilder.AddColumn<bool>(
                name: "IsWarmup",
                table: "Tours",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Step 2: Migrate data back — set IsWarmup = true where Type = 1 (Warmup)
            migrationBuilder.Sql("""UPDATE "Tours" SET "IsWarmup" = true WHERE "Type" = 1""");

            // Step 3: Drop Type column
            migrationBuilder.DropColumn(
                name: "Type",
                table: "Tours");
        }
    }
}
