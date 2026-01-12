using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuestionsHub.Blazor.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPackageImportJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PackageImportJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CurrentStep = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Progress = table.Column<int>(type: "integer", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextRetryAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    InputFileName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    InputFilePath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    InputFileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ConvertedFilePath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    PackageId = table.Column<int>(type: "integer", nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ErrorDetails = table.Column<string>(type: "text", nullable: true),
                    WarningsJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackageImportJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PackageImportJobs_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PackageImportJobs_Packages_PackageId",
                        column: x => x.PackageId,
                        principalTable: "Packages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PackageImportJobs_OwnerId",
                table: "PackageImportJobs",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_PackageImportJobs_PackageId",
                table: "PackageImportJobs",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_PackageImportJobs_Status_CreatedAt",
                table: "PackageImportJobs",
                columns: new[] { "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PackageImportJobs");
        }
    }
}
