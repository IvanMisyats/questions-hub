using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuestionsHub.Blazor.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPackageOwnershipAndStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OwnerId",
                table: "Packages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Packages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Packages_OwnerId",
                table: "Packages",
                column: "OwnerId");

            migrationBuilder.AddForeignKey(
                name: "FK_Packages_AspNetUsers_OwnerId",
                table: "Packages",
                column: "OwnerId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Packages_AspNetUsers_OwnerId",
                table: "Packages");

            migrationBuilder.DropIndex(
                name: "IX_Packages_OwnerId",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Packages");
        }
    }
}
