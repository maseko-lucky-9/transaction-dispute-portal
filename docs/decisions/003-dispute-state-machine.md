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

### Refinement (PR4)
- **Integrity invariant vs policy ceilings.** "One active dispute per transaction" is enforced by the database: the
  partial unique index `ux_disputes_one_active_per_transaction`. The 20-active-per-customer and 3-per-transaction caps
  are policy ceilings, counted just before the insert (read committed, no lock). Submits racing at a ceiling all read
  the same count, so the overshoot is bounded by the number of concurrent submits, not by one: the submit limit of
  10 a minute per user and replica caps it at roughly 10 times the replicas.
  - The design's alternative, a lock on the Customer row, needs UPDATE on `customers`, which `dispute_app` does not
    have. An advisory lock would serialise the customer's submits, which is not worth it at about one write per second.
  - The overshoot is bounded and visible, and the next submission is refused. The ceilings limit abuse; they are not
    money invariants, which is why only the one-active-per-transaction rule has a database backstop.
- **"Already disputed" is a state, not a rule.** It is outside the chain: 409 `dispute_exists` on submit, and the
  block reason `already_disputed` on the transaction list.
- **The most relevant dispute is always the latest.** The transaction list shows the active dispute first, else the
  latest. Because a new dispute can only follow a Withdrawn one, and nothing returns to an active status, the active
  dispute is always the latest. The list reads it as a sort-free top-1 on
  `(transaction_id, customer_id, created_at desc, id desc)`.

## Consequences
Illegal moves are unrepresentable. The transition matrix is a single unit-test theory. The frontend mirrors the
legal next states for the agent form.
- **Known limit: "latest" is ordered by the replica's clock.** `created_at` is stamped by the API's `TimeProvider`.
  Clock skew between replicas larger than the gap between a withdraw and a re-dispute could order the withdrawn
  dispute after the active one, so the list would offer Dispute while submit answers 409. Keep replicas on NTP; a
  database-stamped `created_at` (as `occurred_at` already is) is the upgrade.
