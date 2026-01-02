-- 01-extensions.sql
CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE EXTENSION IF NOT EXISTS unaccent;

-- Idempotent: safe to run multiple times
-- Run as postgres superuser
-- Creates required PostgreSQL extensions

