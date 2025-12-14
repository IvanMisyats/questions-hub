#!/bin/bash
set -e

# Create questionshub user with limited privileges
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-EOSQL
    -- Create questionshub user if not exists
    DO
    \$\$
    BEGIN
       IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'questionshub') THEN
          CREATE USER questionshub WITH PASSWORD '$QUESTIONSHUB_PASSWORD';
       END IF;
    END
    \$\$;

    -- Grant privileges to questionshub user
    GRANT CONNECT ON DATABASE questionshub TO questionshub;
    GRANT USAGE, CREATE ON SCHEMA public TO questionshub;
    GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO questionshub;
    GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO questionshub;
    ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO questionshub;
    ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO questionshub;
EOSQL

echo "Database user 'questionshub' created successfully"

