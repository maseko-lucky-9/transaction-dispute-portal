# ADR-016: The API can log in to Vault with Kubernetes auth

Status: accepted (2026-11-11). Part D1b of the per-half rollout (ADR-012).

## Context

The API and the migrate job log in to Vault with AppRole, reading `role_id` and `secret_id` from files that compose's `bootstrap` container writes (ADR-005). On the homelab MicroK8s cluster nothing plays `bootstrap`, and delivering those files would recreate "secret zero". The pod already has a platform identity: its projected service-account token, which Vault's Kubernetes auth method can verify against the cluster.

## Decision

- `Vault:AuthMethod` selects the login: `AppRole` (the default, so compose and every existing setting behave as before) or `Kubernetes`.
- Kubernetes settings: `Vault:KubernetesRole` (required), `Vault:JwtFile` (default `/var/run/secrets/kubernetes.io/serviceaccount/token`), `Vault:KubernetesMountPoint` (default `kubernetes`). The cluster manifest sets `Vault__AuthMethod=Kubernetes`, `Vault__KubernetesRole=<role>` and, only if the token is projected elsewhere, `Vault__JwtFile`.
- The token file is read and trimmed on every attempt, never cached: the kubelet rotates projected tokens, and the file may not be mounted yet on the first attempt. A missing or empty file is retried like a missing AppRole file.
- Login uses VaultSharp's `KubernetesAuthMethodInfo(mountPoint, role, jwt)` (`POST auth/<mount>/login` with `{role, jwt}`), inside the same bounded retry and backoff. `VaultUnavailableException` names the auth method in use.
- Settings that can never work fail at once with `VaultMisconfiguredException`, before any request: `Kubernetes` with an empty role, or an undefined enum value. `Program.cs` logs one line and exits 1, without the "start the stack" hint. An unknown name such as `Ldap` is already rejected by the configuration binder when `AddVaultSecrets` reads the section, before the provider runs; that also stops startup at once.
- The token, role id and secret id never appear in a log line or an exception message.

## Consequences

- AppRole stays the default and the only method compose uses; `bootstrap.sh` and the compose files are unchanged. An AppRole setting with an empty `RoleIdFile` still fails on the first read, as before.
- The cluster needs a Vault Kubernetes auth mount and a role bound to the API's service account and namespace, attached to the existing `dispute-api` (and, for the migrate job, `dispute-migrate`) policies. That Vault configuration lives with the cluster manifests, not in this repository.
- A Vault served with a private CA is trusted with `SSL_CERT_FILE` pointing at a mounted CA bundle (.NET on Linux reads it as the root store); no code change and no `Vault:` setting is needed. The file replaces the default roots, so the bundle must also hold any public CA the API still needs (for example Keycloak's, if that is public).
- Not covered: token renewal. The Vault token is used once at startup, so rotating credentials is still a rolling restart (ADR-005).
