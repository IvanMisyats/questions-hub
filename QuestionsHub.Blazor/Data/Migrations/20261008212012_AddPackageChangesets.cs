using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace QuestionsHub.Blazor.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPackageChangesets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PackageChangesets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PackageId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: true),
                    UserDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TokenId = table.Column<int>(type: "integer", nullable: true),
                    TokenName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OperationCount = table.Column<int>(type: "integer", nullable: false),
                    VersionBefore = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    VersionAfter = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TotalQuestionsAfter = table.Column<int>(type: "integer", nullable: false),
                    OperationsJson = table.Column<string>(type: "jsonb", nullable: false),
                    ChangesJson = table.Column<string>(type: "jsonb", nullable: false),
                    WarningsJson = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackageChangesets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PackageChangesets_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PackageChangesets_Packages_PackageId",
                        column: x => x.PackageId,
                        principalTable: "Packages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PackageChangesets_PersonalAccessTokens_TokenId",
                        column: x => x.TokenId,
                        principalTable: "PersonalAccessTokens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PackageChangesets_PackageId_CreatedAt",
                table: "PackageChangesets",
                columns: new[] { "PackageId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_PackageChangesets_TokenId_RequestId",
                table: "PackageChangesets",
                columns: new[] { "TokenId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PackageChangesets_UserId",
                table: "PackageChangesets",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PackageChangesets");
        }
    }
}
