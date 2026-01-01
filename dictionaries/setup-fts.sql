-- Database setup script for Questions Hub
-- Run as postgres superuser

-- Create extensions for full-text search
CREATE EXTENSION IF NOT EXISTS unaccent;
CREATE EXTENSION IF NOT EXISTS pg_trgm;

-- Create Ukrainian hunspell dictionary
DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_ts_dict WHERE dictname = 'ukrainian_hunspell') THEN
    CREATE TEXT SEARCH DICTIONARY ukrainian_hunspell (
      TEMPLATE = ispell,
      DictFile = uk_ua,
      AffFile = uk_ua
    );
    RAISE NOTICE 'Created ukrainian_hunspell dictionary';
  END IF;
END $$;

-- Create Ukrainian text search configuration
DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_ts_config WHERE cfgname = 'ukrainian') THEN
    CREATE TEXT SEARCH CONFIGURATION ukrainian (COPY = simple);
    ALTER TEXT SEARCH CONFIGURATION ukrainian
      ALTER MAPPING FOR asciiword, asciihword, hword_asciipart, word, hword, hword_part
      WITH ukrainian_hunspell, simple;
    RAISE NOTICE 'Created ukrainian FTS configuration';
  END IF;
END $$;

