# 009. "Deploy" means local compose plus CI-published images (for now)

Status: accepted (2026-11-04)

## Context
The owner chose local plus CI only, with Vercel or Cloudflare considered later. The global rule that done means a
deployed URL is waived for this take-home and recorded in the goal brief.

## Decision
CI builds the `api` and `web` image targets, scans them with Trivy (fixable CRITICAL blocks), runs the full
Playwright suite against the compose stack, and on `main` publishes
`ghcr.io/maseko-lucky-9/dispute-portal-{api,web}:<sha>` with SBOM and provenance attestations.

## Consequences
The SPA could move to Vercel or Cloudflare Pages as static files (the nginx image already serves it unchanged); the
.NET API needs a container host such as Cloudflare Containers. Moving it would be a new ADR.
