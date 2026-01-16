using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace QuestionsHub.Blazor.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBlocks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BlockId",
                table: "Questions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Blocks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Preamble = table.Column<string>(type: "text", nullable: true),
                    TourId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Blocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Blocks_Tours_TourId",
                        column: x => x.TourId,
                        principalTable: "Tours",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BlockEditors",
                columns: table => new
                {
                    BlocksId = table.Column<int>(type: "integer", nullable: false),
                    EditorsId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockEditors", x => new { x.BlocksId, x.EditorsId });
                    table.ForeignKey(
                        name: "FK_BlockEditors_Authors_EditorsId",
                        column: x => x.EditorsId,
                        principalTable: "Authors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BlockEditors_Blocks_BlocksId",
                        column: x => x.BlocksId,
                        principalTable: "Blocks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Questions_BlockId",
                table: "Questions",
                column: "BlockId");

            migrationBuilder.CreateIndex(
                name: "IX_BlockEditors_EditorsId",
                table: "BlockEditors",
                column: "EditorsId");

            migrationBuilder.CreateIndex(
                name: "IX_Blocks_TourId_OrderIndex",
                table: "Blocks",
                columns: new[] { "TourId", "OrderIndex" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Questions_Blocks_BlockId",
                table: "Questions",
                column: "BlockId",
                principalTable: "Blocks",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Questions_Blocks_BlockId",
                table: "Questions");

            migrationBuilder.DropTable(
                name: "BlockEditors");

            migrationBuilder.DropTable(
                name: "Blocks");

            migrationBuilder.DropIndex(
                name: "IX_Questions_BlockId",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "BlockId",
                table: "Questions");
        }
    }
}
