using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuestionsHub.Blazor.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFuzzySearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Note: Extensions and Ukrainian FTS config should be created by db-setup container (superuser)
            // If not present, this migration will create a fallback configuration

            // Step 1: Verify/create extensions (may fail if not superuser, but db-setup should handle this)
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    -- Check if extensions exist, provide helpful error if not
                    IF NOT EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'unaccent') THEN
                        RAISE EXCEPTION 'Extension unaccent not installed. Ensure db-setup container ran successfully.';
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'pg_trgm') THEN
                        RAISE EXCEPTION 'Extension pg_trgm not installed. Ensure db-setup container ran successfully.';
                    END IF;

                    -- Create fallback 'ukrainian' config if not exists (uses simple tokenization)
                    IF NOT EXISTS (SELECT 1 FROM pg_ts_config WHERE cfgname = 'ukrainian') THEN
                        CREATE TEXT SEARCH CONFIGURATION ukrainian (COPY = simple);
                        RAISE NOTICE 'Created fallback Ukrainian FTS configuration (no morphology)';
                    END IF;
                END $$;
            ");

            // Step 2: Create immutable unaccent wrapper
            // (unaccent is not immutable by default, but we need it for generated columns)
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION public.unaccent_immutable(text)
                RETURNS text
                LANGUAGE sql
                IMMUTABLE
                PARALLEL SAFE
                AS $$ SELECT public.unaccent('public.unaccent', $1) $$;
            ");

            // Step 3: Create normalization function (accents + ґ→г)
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION public.qh_normalize(text)
                RETURNS text
                LANGUAGE sql
                IMMUTABLE
                PARALLEL SAFE
                AS $$
                    SELECT translate(
                        lower(public.unaccent_immutable(COALESCE($1, ''))),
                        'ґ',
                        'г'
                    )
                $$;
            ");

            // Step 4: Add normalized search text column for trigram matching
            migrationBuilder.Sql(@"
                ALTER TABLE ""Questions""
                ADD COLUMN ""SearchTextNorm"" text GENERATED ALWAYS AS (
                    public.qh_normalize(
                        COALESCE(""Text"", '') || ' ' ||
                        COALESCE(""HandoutText"", '') || ' ' ||
                        COALESCE(""Answer"", '') || ' ' ||
                        COALESCE(""AcceptedAnswers"", '') || ' ' ||
                        COALESCE(""RejectedAnswers"", '') || ' ' ||
                        COALESCE(""Comment"", '')
                    )
                ) STORED;
            ");

            // Step 5: Add FTS vector with weighted fields
            migrationBuilder.Sql(@"
                ALTER TABLE ""Questions""
                ADD COLUMN ""SearchVector"" tsvector GENERATED ALWAYS AS (
                    setweight(to_tsvector('ukrainian', public.qh_normalize(COALESCE(""Text"", ''))), 'A') ||
                    setweight(to_tsvector('ukrainian', public.qh_normalize(COALESCE(""HandoutText"", ''))), 'B') ||
                    setweight(to_tsvector('ukrainian', public.qh_normalize(COALESCE(""Answer"", ''))), 'B') ||
                    setweight(to_tsvector('ukrainian', public.qh_normalize(COALESCE(""AcceptedAnswers"", ''))), 'C') ||
                    setweight(to_tsvector('ukrainian', public.qh_normalize(COALESCE(""RejectedAnswers"", ''))), 'C') ||
                    setweight(to_tsvector('ukrainian', public.qh_normalize(COALESCE(""Comment"", ''))), 'D')
                ) STORED;
            ");

            // Step 6: Create GIN index for trigram fuzzy matching
            migrationBuilder.Sql(@"
                CREATE INDEX ""IX_Questions_SearchTextNorm_Trgm""
                ON ""Questions"" USING gin (""SearchTextNorm"" gin_trgm_ops);
            ");

            // Step 7: Create GIN index for FTS
            migrationBuilder.Sql(@"
                CREATE INDEX ""IX_Questions_SearchVector""
                ON ""Questions"" USING gin (""SearchVector"");
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop indexes
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_Questions_SearchVector"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_Questions_SearchTextNorm_Trgm"";");

            // Drop generated columns
            migrationBuilder.Sql(@"ALTER TABLE ""Questions"" DROP COLUMN IF EXISTS ""SearchVector"";");
            migrationBuilder.Sql(@"ALTER TABLE ""Questions"" DROP COLUMN IF EXISTS ""SearchTextNorm"";");

            // Drop text search configuration and dictionary
            migrationBuilder.Sql("DROP TEXT SEARCH CONFIGURATION IF EXISTS ukrainian;");
            migrationBuilder.Sql("DROP TEXT SEARCH DICTIONARY IF EXISTS ukrainian_hunspell;");

            // Drop functions
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS public.qh_normalize(text);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS public.unaccent_immutable(text);");
        }
    }
}
