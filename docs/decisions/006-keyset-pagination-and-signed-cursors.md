# 006. Keyset pagination with protected cursors

Status: accepted (2026-11-04)

## Decision
Lists use keyset paging, never offset: `(transaction_date desc, id desc)` for transactions and
`(created_at desc, id desc)` for disputes, `limit <= 100`. The cursor is opaque, protected with DataProtection and
bound to the user and the filter set; a tampered or foreign cursor returns 400 `invalid_cursor`. When both `from`
and `to` are given, the range is capped at 366 days; an open-ended range (one bound or none) is not capped, because
keyset plus `LIMIT` already bounds each page to one customer's index prefix. Each bound must lie in
1900-01-01..2100-12-31. `transactions.customer_id` is denormalised so the owner filter and the order-by share one index
`(customer_id, transaction_date desc, id desc)`; an integration test asserts an index scan with no Sort on 50k rows.

## Consequences
Constant-time pages regardless of depth, and stable under inserts. No "jump to page 50" and no total count.
