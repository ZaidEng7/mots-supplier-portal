-- The least-privilege role the application connects as at runtime.
--
-- WHY THIS EXISTS
--
-- Until this script, the application connected as the role that OWNS its tables. In PostgreSQL a
-- table's owner bypasses GRANT and REVOKE on that table, so no grant could constrain it and every
-- request ran with rights to DROP or ALTER the schema. The audit-log trigger in the initial migration
-- already says as much, and is a trigger rather than a REVOKE for exactly that reason: a REVOKE would
-- have read as a control in the diff and enforced nothing against the connection actually in use.
--
-- The model this establishes is two connections, not one:
--
--   the OWNER runs migrations, in the deploy step, and prepares the Hangfire schema
--   the APP ROLE runs the application, and can only read and write rows
--
-- WHAT THE APP ROLE MAY DO
--
-- Connect to the database; use the eight application schemas and Hangfire's; select, insert, update
-- and delete rows; use sequences. Nothing else. It may not create, alter or drop anything, which is
-- what turns a SQL-injection or a compromised handler from a schema-level incident into a row-level
-- one.
--
-- ALTER DEFAULT PRIVILEGES is the half that is easy to forget. Without it the grants below cover only
-- the tables that exist today, and the first table a future migration adds is invisible to the
-- application until somebody re-runs this by hand. The default privileges are attached to the OWNER,
-- because privileges follow the role that creates the object.
--
-- HANGFIRE needs its schema prepared by the owner, because preparing it is DDL. Set
-- Hangfire:PrepareSchema to false in any deployment using this role, and run the deploy step as the
-- owner at least once so the schema exists.
--
-- RUNNING IT
--
--   psql -v app_role=mots_app -v app_password="$APP_DB_PASSWORD" -v owner=postgres \
--        -d mots_supplier_portal -f ops/sql/app-role.sql
--
-- Idempotent: safe to re-run after every migration, and that is the intended cadence.

\set ON_ERROR_STOP on

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = :'app_role') THEN
        EXECUTE format('CREATE ROLE %I LOGIN PASSWORD %L', :'app_role', :'app_password');
    ELSE
        EXECUTE format('ALTER ROLE %I LOGIN PASSWORD %L', :'app_role', :'app_password');
    END IF;
END
$$;

GRANT CONNECT ON DATABASE :"DBNAME" TO :"app_role";

DO $$
DECLARE
    target_schema text;
BEGIN
    FOREACH target_schema IN ARRAY ARRAY[
        'identity', 'supplier', 'rfq', 'proposal', 'evaluation', 'award', 'reference', 'ops', 'hangfire'
    ]
    LOOP
        IF EXISTS (SELECT 1 FROM information_schema.schemata WHERE schema_name = target_schema) THEN
            EXECUTE format('GRANT USAGE ON SCHEMA %I TO %I', target_schema, :'app_role');
            EXECUTE format(
                'GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA %I TO %I',
                target_schema, :'app_role');
            EXECUTE format(
                'GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA %I TO %I',
                target_schema, :'app_role');
            EXECUTE format(
                'ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I '
                'GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO %I',
                :'owner', target_schema, :'app_role');
            EXECUTE format(
                'ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I '
                'GRANT USAGE, SELECT ON SEQUENCES TO %I',
                :'owner', target_schema, :'app_role');
        END IF;
    END LOOP;
END
$$;
