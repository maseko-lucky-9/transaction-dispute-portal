#!/bin/sh
# One-shot, idempotent bootstrap (runs in the vault image on every `docker compose up`).
# 1) generate any missing secret into the `secrets` volume  2) init/unseal Vault  3) write KV, policies, AppRoles.
# Locally this container plays the trusted orchestrator that delivers "secret zero" (the AppRole role_id/secret_id
# files). In production, platform identity (e.g. Kubernetes auth) replaces it. See docs/decisions.
set -eu
export VAULT_ADDR="${VAULT_ADDR:-http://vault:8200}"
S=/secrets
CIDR="${BACKEND_CIDR:-10.231.28.0/24}"

# A previous run locked the tree read-only (see the end of this script). This container runs without CAP_DAC_OVERRIDE,
# so as the owner it must make the tree writable again before regenerating anything. Idempotent re-runs depend on it.
chmod -R u+w "$S" 2>/dev/null || true

gen() { # gen <relative path>: random 32-char alphanumeric secret, created only when missing
  f="$S/$1"
  if [ ! -s "$f" ]; then
    mkdir -p "$(dirname "$f")"
    head -c 96 /dev/urandom | base64 | tr -dc 'A-Za-z0-9' | head -c 32 > "$f"
  fi
}
put() { # put <relative path> <value>: always rewritten (derived files)
  mkdir -p "$(dirname "$S/$1")"
  printf '%s' "$2" > "$S/$1"
}

for p in pg/superuser pg/owner pg/app pg/keycloak redis/password keycloak/admin_password keycloak/db_password keycloak/client_secret; do gen "$p"; done
put keycloak/db_password "$(cat $S/pg/keycloak)"
put redis/users.acl "user default off
user dispute_api on >$(cat $S/redis/password) ~bff:* resetchannels +@all -@admin -@dangerous +info +client|setname +client|setinfo +client|id
"

echo "bootstrap: waiting for vault"
i=0
while :; do
  rc=0; vault status >/dev/null 2>&1 || rc=$?
  [ "$rc" -ne 1 ] && break
  i=$((i + 1)); [ "$i" -gt 90 ] && { echo "bootstrap: vault unreachable at $VAULT_ADDR" >&2; exit 1; }
  sleep 1
done

if ! vault status -format=json 2>/dev/null | grep -q '"initialized": *true'; then
  echo "bootstrap: initialising vault (1 key share: local development only)"
  mkdir -p "$S/vault"
  # Persist the raw init output first, so a crash between `init` and the key files never loses the unseal key.
  vault operator init -key-shares=1 -key-threshold=1 > "$S/vault/init.txt"
  sed -n 's/^Unseal Key 1: //p' "$S/vault/init.txt" | tr -d '\n' > "$S/vault/unseal_key"
  sed -n 's/^Initial Root Token: //p' "$S/vault/init.txt" | tr -d '\n' > "$S/vault/root_token"
fi
if vault status -format=json 2>/dev/null | grep -q '"sealed": *true'; then
  echo "bootstrap: unsealing vault"
  vault operator unseal "$(cat $S/vault/unseal_key)" >/dev/null
fi
VAULT_TOKEN="$(cat $S/vault/root_token)"; export VAULT_TOKEN

vault secrets list -format=json | grep -q '"kv/"' || vault secrets enable -path=kv kv-v2
vault auth list -format=json | grep -q '"approle/"' || vault auth enable approle

vault kv put kv/dispute-portal/database       Host=postgres Port=5432 Name=disputes Username=dispute_app   Password="$(cat $S/pg/app)"   >/dev/null
vault kv put kv/dispute-portal/database-owner Host=postgres Port=5432 Name=disputes Username=dispute_owner Password="$(cat $S/pg/owner)" >/dev/null
vault kv put kv/dispute-portal/redis          Host=redis Port=6379 Username=dispute_api Password="$(cat $S/redis/password)" >/dev/null
vault kv put kv/dispute-portal/keycloak       ClientSecret="$(cat $S/keycloak/client_secret)" >/dev/null

for r in api migrate; do
  vault policy write "dispute-$r" "/policies/dispute-$r.hcl" >/dev/null
  vault write "auth/approle/role/dispute-$r" token_policies="dispute-$r" token_ttl=10m token_max_ttl=30m \
    secret_id_ttl=24h secret_id_bound_cidrs="$CIDR" token_bound_cidrs="$CIDR" >/dev/null
  put "approle/$r/role_id" "$(vault read -field=role_id "auth/approle/role/dispute-$r/role-id")"
  put "approle/$r/secret_id" "$(vault write -f -field=secret_id "auth/approle/role/dispute-$r/secret-id")"
done
# Each `up` issues fresh secret_ids; drop the expired and orphaned ones so they do not pile up.
vault write -f auth/approle/tidy/secret-id >/dev/null 2>&1 || true

# Readable by the service users that mount the sub-directories read-only. The CIDR binding and TTL are the control.
find "$S" -type d -exec chmod 0555 {} +
find "$S" -type f ! -path "$S/vault/*" -exec chmod 0444 {} +
chmod 0400 "$S"/vault/*
echo "bootstrap: done"
