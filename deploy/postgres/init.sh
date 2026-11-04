#!/bin/sh
# Runs once, on first volume creation (docker-entrypoint-initdb.d). Creates the roles and their passwords.
set -e
P=/run/pg
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" -f /roles/roles.sql
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<SQL
ALTER ROLE dispute_owner PASSWORD '$(cat $P/owner)';
ALTER ROLE dispute_app   PASSWORD '$(cat $P/app)';
CREATE ROLE keycloak LOGIN PASSWORD '$(cat $P/keycloak)';
CREATE DATABASE keycloak OWNER keycloak;
SQL
