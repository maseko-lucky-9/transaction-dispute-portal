#!/bin/sh
# One-shot and idempotent; local only (runs in the keycloak image on every `docker compose up`).
# Adds the demo users in demo-users.json to the dispute-portal realm through Keycloak's partial import. The realm import
# (`kc.sh start --import-realm`) runs only into an empty database and carries no users, so this is what puts users on a new
# stack and adds new ones to an existing stack. ifResourceExists=SKIP leaves a user that already exists alone: its password,
# roles and attributes are never rewritten. A hand-made user with a demo username but another id would be skipped too, and
# the API would then see a different `sub` (users that came from the realm import share these ids). Output is counts and
# user names, nothing else.
set -eu

kc=/opt/keycloak/bin/kcadm.sh
cfg=/tmp/kcadm.config
# kcadm reads the admin password from KC_CLI_PASSWORD, so it never appears on a command line.
KC_CLI_PASSWORD="$(cat /run/kc/admin_password)"
export KC_CLI_PASSWORD

attempt=1
until "$kc" config credentials --config "$cfg" --server http://localhost:8080 --realm master --user admin >/dev/null; do
  [ "$attempt" -lt 20 ] || { echo "seed-users: admin login failed after $attempt attempts" >&2; exit 1; }
  attempt=$((attempt + 1))
  sleep 3
done

echo "seed-users: $(date -u +%FT%TZ) partial import of /seed/demo-users.json into dispute-portal"
result="$("$kc" create partialImport --config "$cfg" -r dispute-portal -f /seed/demo-users.json -o -c)"
# The response is one line of JSON and the image has no jq or awk: print the totals, then one line per user.
echo "seed-users: $(printf '%s' "$result" | grep -Eo '"(added|skipped|overwritten)":[0-9]+' | tr -d '"' | tr '\n' ' ')"
printf '%s' "$result" | tr '{' '\n' | grep '"resourceType":"USER"' | while read -r record; do
  action="$(printf '%s' "$record" | sed -n 's/.*"action":"\([A-Z]*\)".*/\1/p')"
  name="$(printf '%s' "$record" | sed -n 's/.*"resourceName":"\([^"]*\)".*/\1/p')"
  echo "seed-users:   $name $action"
done
