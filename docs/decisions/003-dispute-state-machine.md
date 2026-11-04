# 003. Dispute lifecycle as a table-driven state machine inside the aggregate

Status: accepted (2026-11-04)

## Context
A dispute is a lifecycle with actor-restricted transitions. Validating transitions in a service lets an illegal
state be reached by a forgotten check.

## Decision
`Dispute` owns its `Status` and a transition table keyed by (from, to, actor):

- Submitted -> UnderReview (agent), UnderReview -> Accepted | Rejected (agent)
- Submitted | UnderReview -> Withdrawn (customer)
- Accepted, Rejected, Withdrawn are terminal. A transaction can be disputed again only after Withdrawn.

Eligibility (posted, debit, within 120 days, amount <= transaction, caps) is a chain of `IDisputeEligibilityRule`
classes composed by `DisputeEligibilityPolicy` (Specification/Strategy). Scheme rules change; a new rule is a new
class, and the API reports every violation at once. Every transition appends a `DisputeStatusChange` row (the
historic view); the app role has INSERT only on that table and the database stamps `occurred_at`.

## Consequences
Illegal moves are unrepresentable. The transition matrix is a single unit-test theory. The frontend mirrors the
legal next states for the agent form.
