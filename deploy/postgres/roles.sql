-- Roles for the dispute portal. Idempotent. Passwords are NOT set here: the Postgres init wrapper
-- (compose) and the integration-test fixture set them with ALTER ROLE. Table grants live in the EF migrations.
DO $$
BEGIN
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'dispute_owner') THEN
    CREATE ROLE dispute_owner LOGIN;
  END IF;
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'dispute_app') THEN
    CREATE ROLE dispute_app LOGIN;
  END IF;
  EXECUTE format('ALTER DATABASE %I OWNER TO dispute_owner', current_database());
  EXECUTE format('REVOKE CONNECT, TEMP ON DATABASE %I FROM PUBLIC', current_database());
  EXECUTE format('GRANT CONNECT ON DATABASE %I TO dispute_app', current_database());
END
$$;
REVOKE ALL ON SCHEMA public FROM PUBLIC;
ALTER SCHEMA public OWNER TO dispute_owner;
GRANT USAGE ON SCHEMA public TO dispute_app;
