-- 02-user-permissions.sql
-- Creates application user and grants permissions
-- Run as postgres superuser
-- Idempotent: safe to run multiple times
-- Note: QUESTIONSHUB_PASSWORD must be set via psql variable or environment

-- Create the login role if it does not exist yet. Not a DO block: psql does not substitute
-- :'variables' inside dollar-quoted text, so a DO block cannot see the password, and on a fresh
-- database the CREATE there was a syntax error (the role then never existed).
SELECT 'CREATE ROLE questionshub LOGIN'
WHERE NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'questionshub')\gexec

-- Set (or reset) the password to match the current configuration
ALTER ROLE questionshub WITH LOGIN PASSWORD :'QUESTIONSHUB_PASSWORD';

-- Grant connection privileges
GRANT CONNECT ON DATABASE questionshub TO questionshub;

-- Grant schema privileges
GRANT USAGE, CREATE ON SCHEMA public TO questionshub;

-- Grant table privileges (existing and future tables)
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO questionshub;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO questionshub;

-- Grant sequence privileges (existing and future sequences)
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO questionshub;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO questionshub;

