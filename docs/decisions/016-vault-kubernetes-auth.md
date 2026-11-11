# ADR-016: The API can log in to Vault with Kubernetes auth

Status: accepted (2026-11-11). Part D1b of the per-half rollout (ADR-012). Amended 2026-11-11: Vault TLS trust (see
the pinned server certificate below); the original "`SSL_CERT_FILE`, no code change" consequence was wrong for the cluster.

## Context

The API and the migrate job log in to Vault with AppRole, reading `role_id` and `secret_id` from files that compose's `bootstrap` container writes (ADR-005). On the homelab MicroK8s cluster nothing plays `bootstrap`, and delivering those files would recreate "secret zero". The pod already has a platform identity: its projected service-account token, which Vault's Kubernetes auth method can verify against the cluster.

## Decision

- `Vault:AuthMethod` selects the login: `AppRole` (the default, so compose and every existing setting behave as before) or `Kubernetes`.
- Kubernetes settings: `Vault:KubernetesRole` (required), `Vault:JwtFile` (default `/var/run/secrets/kubernetes.io/serviceaccount/token`), `Vault:KubernetesMountPoint` (default `kubernetes`). The cluster manifest sets `Vault__AuthMethod=Kubernetes`, `Vault__KubernetesRole=<role>` and, only if the token is projected elsewhere, `Vault__JwtFile`.
- The token file is read and trimmed on every attempt, never cached: the kubelet rotates projected tokens, and the file may not be mounted yet on the first attempt. A missing, empty or unreadable file is retried like a missing AppRole file.
- The token is a replayable credential, so Kubernetes auth requires an `https://` `Vault:Address` (any case). AppRole is not affected; compose keeps `http://vault:8200` on the internal network.
- Login uses VaultSharp's `KubernetesAuthMethodInfo(mountPoint, role, jwt)` (`POST auth/<mount>/login` with `{role, jwt}`), inside the same bounded retry and backoff. `VaultUnavailableException` names the auth method in use.
- Settings that can never work fail at once with `VaultMisconfiguredException`, before any request: `Kubernetes` with an empty role or a non-https address, or an undefined enum value. `Program.cs` logs one line and exits 1, without the "start the stack" hint. An unknown name such as `Kubernets` is rejected by the configuration binder when `AddVaultSecrets` reads the section; that failure is rethrown as `VaultMisconfiguredException`, so it ends in the same one-line exit instead of a stack trace.
- The token, role id and secret id never appear in a log line or an exception message.

## Consequences

- AppRole stays the default and the only method compose uses; `bootstrap.sh` and the compose files are unchanged. An AppRole setting with an empty `RoleIdFile` still fails on the first read, as before.
- The cluster needs a Vault Kubernetes auth mount and a role bound to the API's service account and namespace, attached to the existing `dispute-api` (and, for the migrate job, `dispute-migrate`) policies. That Vault configuration lives with the cluster manifests, not in this repository.
- Vault TLS trust (amended 2026-11-11). `SSL_CERT_FILE` pointing at a mounted bundle works only when that file holds a real, self-signed root CA: .NET on Linux reads it as the root store and must build a chain ending in a self-signed root. The file replaces the default roots, so it must also hold any public CA the API still needs (for example Keycloak's, if that is public).
- On the homelab cluster that is not the case. The `vault-tls-ca` secret's `ca.crt` is Vault's own server certificate (`CN=vault.local`, `CA:FALSE`, SANs include `vault.vault.svc.cluster.local`), issued by a "Vault Local CA" whose certificate is not published anywhere. Go clients (ESO, vault-config-operator) and curl accept a pinned leaf; .NET does not, and fails with `AuthenticationException: ... PartialChain`. For this case set `Vault__PinnedServerCertificateFile` to the mounted certificate (one PEM or DER certificate; `VaultServerCertificatePin`):
  - VaultSharp's handler then accepts the connection iff the presented certificate's SHA-256 (of its DER encoding, fixed-time compared) equals the pinned one, there is no `RemoteCertificateNameMismatch` (the host name must still be in the SANs), and the certificate is within its NotBefore/NotAfter now. Chain errors alone are ignored: the pin replaces CA-chain validation for Vault and only for Vault. Applies to both AppRole and Kubernetes auth.
  - The file is read once at startup, before the retry. A missing, unreadable or unparseable file, or one with more than one certificate or a non-certificate PEM block, is `VaultMisconfiguredException` (exit 1, no retry). Empty (the default) keeps normal system trust.
  - The pin must be rotated together with the certificate: when Vault's certificate is reissued (this one expires 2027-03-16), mount the new one before or with Vault's switch, or every API start fails the TLS handshake.
- Startup failures now log the inner-exception chain (`VaultFailureDetail`: type and message, at most 4 levels, 200 characters each, one line) as the `Cause` field of the Critical line; the outer message alone hid this TLS failure behind "unreachable". Only exception types and messages appear, never request bodies, headers or the token.
- Not covered: token renewal. The Vault token is used once at startup, so rotating credentials is still a rolling restart (ADR-005).
