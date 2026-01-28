using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuestionsHub.Blazor.Data.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Normalizes apostrophe-like characters to the standard Ukrainian apostrophe (U+02BC).
    /// Replaces: ' (U+0027), ' (U+2019), ˈ (U+02C8) with ʼ (U+02BC).
    /// Excludes Source field as it may contain URLs where apostrophes are intentional.
    /// </summary>
    public partial class NormalizeApostrophes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Normalize Questions table (all text fields except Source)
            migrationBuilder.Sql(@"
                UPDATE ""Questions""
                SET
                    ""Text"" = TRANSLATE(""Text"", E'''ˈ', 'ʼʼʼ'),
                    ""Answer"" = TRANSLATE(""Answer"", E'''ˈ', 'ʼʼʼ'),
                    ""AcceptedAnswers"" = TRANSLATE(""AcceptedAnswers"", E'''ˈ', 'ʼʼʼ'),
                    ""RejectedAnswers"" = TRANSLATE(""RejectedAnswers"", E'''ˈ', 'ʼʼʼ'),
                    ""Comment"" = TRANSLATE(""Comment"", E'''ˈ', 'ʼʼʼ'),
                    ""HandoutText"" = TRANSLATE(""HandoutText"", E'''ˈ', 'ʼʼʼ'),
                    ""HostInstructions"" = TRANSLATE(""HostInstructions"", E'''ˈ', 'ʼʼʼ')
                WHERE
                    ""Text"" ~ E'[''ˈ]' OR
                    ""Answer"" ~ E'[''ˈ]' OR
                    ""AcceptedAnswers"" ~ E'[''ˈ]' OR
                    ""RejectedAnswers"" ~ E'[''ˈ]' OR
                    ""Comment"" ~ E'[''ˈ]' OR
                    ""HandoutText"" ~ E'[''ˈ]' OR
                    ""HostInstructions"" ~ E'[''ˈ]';
            ");

            // Normalize Authors table
            migrationBuilder.Sql(@"
                UPDATE ""Authors""
                SET
                    ""FirstName"" = TRANSLATE(""FirstName"", E'''ˈ', 'ʼʼʼ'),
                    ""LastName"" = TRANSLATE(""LastName"", E'''ˈ', 'ʼʼʼ')
                WHERE
                    ""FirstName"" ~ E'[''ˈ]' OR
                    ""LastName"" ~ E'[''ˈ]';
            ");

            // Normalize Packages table
            migrationBuilder.Sql(@"
                UPDATE ""Packages""
                SET
                    ""Title"" = TRANSLATE(""Title"", E'''ˈ', 'ʼʼʼ'),
                    ""Description"" = TRANSLATE(""Description"", E'''ˈ', 'ʼʼʼ'),
                    ""Preamble"" = TRANSLATE(""Preamble"", E'''ˈ', 'ʼʼʼ')
                WHERE
                    ""Title"" ~ E'[''ˈ]' OR
                    ""Description"" ~ E'[''ˈ]' OR
                    ""Preamble"" ~ E'[''ˈ]';
            ");

            // Normalize Tours table
            migrationBuilder.Sql(@"
                UPDATE ""Tours""
                SET
                    ""Comment"" = TRANSLATE(""Comment"", E'''ˈ', 'ʼʼʼ'),
                    ""Preamble"" = TRANSLATE(""Preamble"", E'''ˈ', 'ʼʼʼ')
                WHERE
                    ""Comment"" ~ E'[''ˈ]' OR
                    ""Preamble"" ~ E'[''ˈ]';
            ");

            // Normalize Blocks table
            migrationBuilder.Sql(@"
                UPDATE ""Blocks""
                SET
                    ""Name"" = TRANSLATE(""Name"", E'''ˈ', 'ʼʼʼ'),
                    ""Preamble"" = TRANSLATE(""Preamble"", E'''ˈ', 'ʼʼʼ')
                WHERE
                    ""Name"" ~ E'[''ˈ]' OR
                    ""Preamble"" ~ E'[''ˈ]';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // This migration is intentionally one-way.
            // Rolling back would require knowing which apostrophes were originally
            // which variant, which is not tracked.
        }
    }
}
