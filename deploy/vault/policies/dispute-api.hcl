# The API reads its runtime credentials. It cannot see the owner (migration) credentials.
path "kv/data/dispute-portal/database"  { capabilities = ["read"] }
path "kv/data/dispute-portal/redis"     { capabilities = ["read"] }
path "kv/data/dispute-portal/keycloak"  { capabilities = ["read"] }
