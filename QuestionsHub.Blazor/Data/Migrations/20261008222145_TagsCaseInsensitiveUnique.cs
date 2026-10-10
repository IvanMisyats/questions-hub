using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuestionsHub.Blazor.Data.Migrations
{
    /// <summary>
    /// Makes tag names unique case-insensitively. The existing IX_Tags_Name_CI uses the deterministic
    /// "und-x-icu" collation, so "Кубок" and "кубок" could coexist; lookups (TagService, the changeset
    /// engine) match case-insensitively, and concurrent writers rely on the database to reject the
    /// second insert. Tags that already differ only by case are merged into the oldest one first.
    /// </summary>
    public partial class TagsCaseInsensitiveUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TEMP TABLE tag_merge ON COMMIT DROP AS
                    SELECT "Id" AS id, min("Id") OVER (PARTITION BY lower("Name")) AS keep FROM "Tags";

                INSERT INTO "PackageTags" ("PackagesId", "TagsId")
                    SELECT pt."PackagesId", m.keep
                    FROM "PackageTags" pt JOIN tag_merge m ON m.id = pt."TagsId"
                    WHERE m.id <> m.keep
                    ON CONFLICT DO NOTHING;

                -- Deleting a duplicate cascades its remaining PackageTags rows.
                DELETE FROM "Tags" t USING tag_merge m WHERE t."Id" = m.id AND m.id <> m.keep;

                CREATE UNIQUE INDEX "IX_Tags_Name_Lower" ON "Tags" (lower("Name"));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_Tags_Name_Lower";""");
        }
    }
}
