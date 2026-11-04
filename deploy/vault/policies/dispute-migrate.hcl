# The one-shot migrate job reads only the schema-owner credentials.
path "kv/data/dispute-portal/database-owner" { capabilities = ["read"] }
