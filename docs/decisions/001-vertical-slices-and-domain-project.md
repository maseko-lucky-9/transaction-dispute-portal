# 001. Vertical-slice Minimal API plus a pure Domain project

Status: accepted (2026-11-04)

## Context
The brief grades architecture, scalability and maintainability. SCAD's ADR-002 (the style reference for this
project) rejected four-project Clean Architecture as ceremony for a service this size.

## Decision
One deployable API organised by feature (`Features/Transactions`, `Disputes`, `Agent`, `Bff`), each slice owning its
endpoints, requests, validators, repository and read models. One pure `DisputePortal.Domain` project (no EF, no
ASP.NET) holds the Dispute aggregate, the eligibility rules and `Money`, so the business rules are unit-testable
without a database.

## Consequences
- A new feature touches one folder. A reviewer can read one slice end to end.
- The Domain boundary is enforced by project references, not by convention.
- The API is a modular monolith. It is stateless, so it scales horizontally (`--scale api=3`); extraction to
  services is possible along slice lines but not planned.
