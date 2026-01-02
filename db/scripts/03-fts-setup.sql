-- 03-fts-setup.sql
-- Configures Ukrainian full-text search with hunspell dictionary
-- Run as postgres superuser
-- Idempotent: safe to run multiple times
-- Requires: uk_ua.dict and uk_ua.affix files in PostgreSQL tsearch_data directory

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
  ELSE
    RAISE NOTICE 'ukrainian_hunspell dictionary already exists';
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
  ELSE
    RAISE NOTICE 'ukrainian FTS configuration already exists';
  END IF;
END $$;

