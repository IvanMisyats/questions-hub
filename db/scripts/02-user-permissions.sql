-- 02-user-permissions.sql
-- Creates application user and grants permissions
-- Run as postgres superuser
-- Idempotent: safe to run multiple times
-- Note: QUESTIONSHUB_PASSWORD must be set via psql variable or environment

-- Create user if not exists
DO $$
BEGIN
  IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'questionshub') THEN
    CREATE USER questionshub WITH PASSWORD :'QUESTIONSHUB_PASSWORD';
    RAISE NOTICE 'Created user questionshub';
  ELSE
    -- Update password to ensure it matches current configuration
    EXECUTE format('ALTER USER questionshub WITH PASSWORD %L', :'QUESTIONSHUB_PASSWORD');
    RAISE NOTICE 'Updated password for user questionshub';
  END IF;
END $$;

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

