using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuestionsHub.Blazor.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReplacePlayedAtWithPlayedPeriod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "PlayedAt",
                table: "Packages",
                newName: "PlayedTo");

            migrationBuilder.AddColumn<DateOnly>(
                name: "PlayedFrom",
                table: "Packages",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PlayedFrom",
                table: "Packages");

            migrationBuilder.RenameColumn(
                name: "PlayedTo",
                table: "Packages",
                newName: "PlayedAt");
        }
    }
}
