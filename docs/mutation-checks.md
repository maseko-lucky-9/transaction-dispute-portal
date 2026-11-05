# Manual mutation checks

> Path map (ADR-012): this document predates the folder split. Read `src/` as `backend/src/`, `tests/` as `backend/tests/` and `web/` as `frontend/`.

Short version: for each guard the plan names (the 120-day window, the Owner filter, `If-Match`) and for a few more
(sensitive-data check, CSRF, the unique-violation path, optimistic concurrency), I broke the code on purpose, ran the
unit and integration suites, and recorded which tests went red. Every mutation was temporary: applied, tested,
reverted with `git checkout -- .`, and the suites were re-run green. Nothing mutated is committed. The runs were made on the base commit below (475 unit and 307 integration tests at the
time); the suites have grown since (579 unit and 363 integration tests on `feat/backchannel-logout`, see the README's
measured results).

Why: a test that still passes against broken source proves nothing. The project plan (Testing, "Manual mutation check")
asks for this check with the output pasted, and that output was not in the repo. Stryker (Domain only, report-only in CI)
does not exercise the API, the database or the pipeline, so these were done by hand.

- Date: 2026-11-04
- Base commit: `0e882b3` (`docs(docs): correct the HSTS claim in ADR-008 (#18)`), 
- Environment: macOS arm64, .NET SDK 10.0.201, Docker Desktop 4.98.0. Integration tests use Testcontainers
  (`postgres:17-alpine`, `redis:7-alpine`); the API connects as `dispute_app`. Docker was already running; `docker compose` was not used.
- Both suites ran in full for every mutation (`dotnet test` rebuilds first, and the repo builds with warnings as errors,
  so every mutation compiled cleanly), so the "red" lists are complete, not a hand-picked subset.
- Trimming: stack traces are dropped (they hold local paths), assertion messages are cut to their first lines (non-printable characters are shown as `?`), and very
  long red lists are summarised by class. Test names and summary lines are verbatim. Durations are machine-specific.
- Helper scripts and the full raw logs were kept outside the repo.

Baseline on the clean tree, before any mutation:

```
$ dotnet test tests/DisputePortal.UnitTests
Passed!  - Failed:     0, Passed:   475, Skipped:     0, Total:   475, Duration: 235 ms - DisputePortal.UnitTests.dll (net10.0)
$ dotnet test tests/DisputePortal.IntegrationTests
Passed!  - Failed:     0, Passed:   307, Skipped:     0, Total:   307, Duration: 16 s - DisputePortal.IntegrationTests.dll (net10.0)
```

## Results

| # | Mutation | File:line | Tests that went red | Survived? |
|---|---|---|---|---|
| M1 | Dispute window: day 121 becomes eligible | `src/DisputePortal.Domain/Eligibility/DisputeWindowRule.cs:16` | unit 4 (DisputeEligibilityPolicyTests x4); integration 1 (TransactionEligibilityTests x1) | no |
| M1b | Dispute window: day 120 becomes ineligible | `src/DisputePortal.Domain/Eligibility/DisputeWindowRule.cs:16` | unit 3 (DisputeEligibilityPolicyTests x3); integration 1 (TransactionEligibilityTests x1) | no |
| M2a | Owner query filter fails open | `src/DisputePortal.Api/Shared/Persistence/DisputePortalDbContext.cs:39-44` (all four entities) | unit 0; integration 2 (OwnerFilterTests x2) | no |
| M2b | Owner query filter dropped for Dispute | `src/DisputePortal.Api/Shared/Persistence/DisputePortalDbContext.cs:41` | unit 0; integration 31 (ArchitectureTests x2, Bola404MetricTests x1, DisputeConcurrencyTests x4, DisputeHistoryOrderTests x1, DisputeSecurityTests x11, DisputesTests x9, OwnerFilterTests x2, TransactionPlanTests x1) | no |
| M2c | IgnoreQueryFilters used outside Features/Agent | `src/DisputePortal.Api/Features/Disputes/DisputeRepository.cs:11` | unit 0; integration 4 (ArchitectureTests x1, Bola404MetricTests x1, DisputeSecurityTests x2) | no |
| M3a | If-Match checks removed from the customer withdraw | `src/DisputePortal.Api/Features/Disputes/DisputeCommands.cs:89-97` | unit 0; integration 6 (DisputeSecurityTests x6) | no |
| M3b | If-Match checks removed from the agent transition | `src/DisputePortal.Api/Features/Agent/AgentCommands.cs:29-37` | unit 0; integration 7 (AgentTransitionTests x6, ResolutionDurationMetricTests x1) | no |
| M4a | Sensitive-data rule removed from the description validator | `src/DisputePortal.Api/Features/Disputes/CreateDisputeRequestValidator.cs:38-43` | unit 7 (CreateDisputeRequestValidatorTests x7); integration 3 (DisputesTests x3) | no |
| M4a2 | Luhn check never passes | `src/DisputePortal.Domain/Disputes/SensitiveNumberDetector.cs:87` | unit 90 (CreateDisputeRequestValidatorTests x7, DisputeNoteTests x31, SensitiveNumberDetectorTests x48, TransitionRequestValidatorTests x4); integration 7 (AgentTransitionTests x4, DisputesTests x3) | no |
| M4b | CSRF middleware not registered | `src/DisputePortal.Api/Program.cs:98` | unit 0; integration 9 (AuthHardeningTests x4, ProductionAuthStackTests x5) | no |
| M4b2 | CSRF no longer required on non-GET /bff | `src/DisputePortal.Api/Shared/Security/CsrfMiddleware.cs:31-33` | unit 2 (CsrfRuleTests x2); integration 1 (ProductionAuthStackTests x1) | no |
| M4c1 | 23505 (unique violation) translation removed | `src/DisputePortal.Api/Shared/Errors/DbErrorTranslator.cs:18` | unit 2 (DbErrorTranslatorTests x2); integration 4 (DisputeConcurrencyTests x3, DisputesTests x1) | no |
| M4c2 | Partial unique index made non-unique (database backstop) | `src/DisputePortal.Api/Shared/Persistence/Migrations/20261104113825_Disputes.cs:148` | unit 0; integration 2 (DisputeConcurrencyTests x1, ReadModelConstraintTests x1) | no |
| M4d | Optimistic-concurrency conflict is overwritten ("client wins") | `src/DisputePortal.Api/Shared/Persistence/EfUnitOfWork.cs:12` | unit 0; integration 3 (AgentTransitionTests x1, DisputeConcurrencyTests x1, ResolutionDurationMetricTests x1) | no |
| B1 | Probe: history filter loses its own owner clause | `src/DisputePortal.Api/Shared/Persistence/DisputePortalDbContext.cs:44` | unit 0; integration 0 | **YES** |

"Survived" means both suites stayed green with the mutation in place.


## M1 - Dispute window: day 121 becomes eligible

File: `src/DisputePortal.Domain/Eligibility/DisputeWindowRule.cs:16`

The rule says day 120 is eligible and day 121 is not. `<= WindowDays + 1` lets a 121-day-old transaction through.

```diff
diff --git a/src/DisputePortal.Domain/Eligibility/DisputeWindowRule.cs b/src/DisputePortal.Domain/Eligibility/DisputeWindowRule.cs
index 2bae2f1..13ca299 100644
--- a/src/DisputePortal.Domain/Eligibility/DisputeWindowRule.cs
+++ b/src/DisputePortal.Domain/Eligibility/DisputeWindowRule.cs
@@ -15,3 +15,3 @@ public sealed class DisputeWindowRule(DisputePolicyOptions options) : IDisputeEl
             - DateOnly.FromDateTime(context.Transaction.TransactionDate.UtcDateTime).DayNumber;
-        return ageDays <= options.WindowDays
+        return ageDays <= options.WindowDays + 1
             ? null
```

Note: The first integration run after restoring this mutation failed on an unrelated flaky test; see Run notes, item 1. The green integration line below is the immediate re-run on the same clean tree.

With the mutation applied:

```
$ dotnet test tests/DisputePortal.UnitTests
Failed!  - Failed:     4, Passed:   471, Skipped:     0, Total:   475, Duration: 242 ms - DisputePortal.UnitTests.dll (net10.0)
$ dotnet test tests/DisputePortal.IntegrationTests
Failed!  - Failed:     1, Passed:   306, Skipped:     0, Total:   307, Duration: 13 s - DisputePortal.IntegrationTests.dll (net10.0)
```

Red tests (verbatim):

```
unit:
Failed DisputePortal.UnitTests.DisputeEligibilityPolicyTests.TheWindowIncludesDay120AndEndsOnDay121(daysLater: 121, disputable: False) [< 1 ms]
Failed DisputePortal.UnitTests.DisputeEligibilityPolicyTests.TheWindowCountsUtcDaysEvenForATransactionWithAnOffset [5 ms]
Failed DisputePortal.UnitTests.DisputeEligibilityPolicyTests.TheWindowCountsCalendarDaysNotElapsedHours [< 1 ms]
Failed DisputePortal.UnitTests.DisputeEligibilityPolicyTests.TheWindowLengthComesFromTheOptions [< 1 ms]

integration:
Failed DisputePortal.IntegrationTests.TransactionEligibilityTests.FlagsAndReasonsFollowTheRulesIncludingTheDay120Boundary [128 ms]
```

First assertion messages (stack traces dropped):

```
unit:
DisputeEligibilityPolicyTests.TheWindowIncludesDay120AndEndsOnDay121(daysLater: 121, disputable: False)
    Assert.Equal() Failure: Values differ
    Expected: False
    Actual:   True
DisputeEligibilityPolicyTests.TheWindowCountsUtcDaysEvenForATransactionWithAnOffset
    Assert.Equal() Failure: Collections differ
    ↓ (pos 0)
    Expected: <generated>                                       ["window_expired"]
integration:
TransactionEligibilityTests.FlagsAndReasonsFollowTheRulesIncludingTheDay120Boundary
    Assert.Equal() Failure: Collections differ
    ↓ (pos 0)
    Expected: ["window_expired"]
```

Restore: `git checkout -- .`; `git status --porcelain` printed nothing; same suites again:

```
$ dotnet test tests/DisputePortal.UnitTests
Passed!  - Failed:     0, Passed:   475, Skipped:     0, Total:   475, Duration: 219 ms - DisputePortal.UnitTests.dll (net10.0)
$ dotnet test tests/DisputePortal.IntegrationTests
Passed!  - Failed:     0, Passed:   307, Skipped:     0, Total:   307, Duration: 16 s - DisputePortal.IntegrationTests.dll (net10.0)
```

## M1b - Dispute window: day 120 becomes ineligible

File: `src/DisputePortal.Domain/Eligibility/DisputeWindowRule.cs:16`

The other side of the boundary: `< WindowDays` refuses day 120, which the rule says is still eligible.

```diff
diff --git a/src/DisputePortal.Domain/Eligibility/DisputeWindowRule.cs b/src/DisputePortal.Domain/Eligibility/DisputeWindowRule.cs
index 2bae2f1..b624098 100644
--- a/src/DisputePortal.Domain/Eligibility/DisputeWindowRule.cs
+++ b/src/DisputePortal.Domain/Eligibility/DisputeWindowRule.cs
@@ -15,3 +15,3 @@ public sealed class DisputeWindowRule(DisputePolicyOptions options) : IDisputeEl
             - DateOnly.FromDateTime(context.Transaction.TransactionDate.UtcDateTime).DayNumber;
-        return ageDays <= options.WindowDays
+        return ageDays < options.WindowDays
             ? null
```

With the mutation applied:

```
$ dotnet test tests/DisputePortal.UnitTests
Failed!  - Failed:     3, Passed:   472, Skipped:     0, Total:   475, Duration: 237 ms - DisputePortal.UnitTests.dll (net10.0)
$ dotnet test tests/DisputePortal.IntegrationTests
Failed!  - Failed:     1, Passed:   306, Skipped:     0, Total:   307, Duration: 13 s - DisputePortal.IntegrationTests.dll (net10.0)
```

Red tests (verbatim):

```
unit:
Failed DisputePortal.UnitTests.DisputeEligibilityPolicyTests.TheWindowIncludesDay120AndEndsOnDay121(daysLater: 120, disputable: True) [1 ms]
Failed DisputePortal.UnitTests.DisputeEligibilityPolicyTests.TheWindowCountsUtcDaysEvenForATransactionWithAnOffset [< 1 ms]
Failed DisputePortal.UnitTests.DisputeEligibilityPolicyTests.TheWindowCountsCalendarDaysNotElapsedHours [< 1 ms]

integration:
Failed DisputePortal.IntegrationTests.TransactionEligibilityTests.FlagsAndReasonsFollowTheRulesIncludingTheDay120Boundary [124 ms]
```

First assertion messages (stack traces dropped):

```
unit:
DisputeEligibilityPolicyTests.TheWindowIncludesDay120AndEndsOnDay121(daysLater: 120, disputable: True)
    Assert.Equal() Failure: Values differ
    Expected: True
    Actual:   False
DisputeEligibilityPolicyTests.TheWindowCountsUtcDaysEvenForATransactionWithAnOffset
    Assert.Empty() Failure: Collection was not empty
    Collection: [EligibilityViolation { Code = window_expired, Message = Transactions can be disputed for 120 days. }]
integration:
TransactionEligibilityTests.FlagsAndReasonsFollowTheRulesIncludingTheDay120Boundary
    Assert.Empty() Failure: Collection was not empty
    Collection: ["window_expired"]
```

Restore: `git checkout -- .`; `git status --porcelain` printed nothing; same suites again:

```
$ dotnet test tests/DisputePortal.UnitTests
Passed!  - Failed:     0, Passed:   475, Skipped:     0, Total:   475, Duration: 228 ms - DisputePortal.UnitTests.dll (net10.0)
$ dotnet test tests/DisputePortal.IntegrationTests
Passed!  - Failed:     0, Passed:   307, Skipped:     0, Total:   307, Duration: 13 s - DisputePortal.IntegrationTests.dll (net10.0)
```

## M2a - Owner query filter fails open

File: `src/DisputePortal.Api/Shared/Persistence/DisputePortalDbContext.cs:39-44` (all four entities)

`CurrentCustomerId != null && ...` becomes `CurrentCustomerId == null || ...`: with no current customer the app sees every row instead of none.

```diff
diff --git a/src/DisputePortal.Api/Shared/Persistence/DisputePortalDbContext.cs b/src/DisputePortal.Api/Shared/Persistence/DisputePortalDbContext.cs
index 707a02c..42e1c60 100644
--- a/src/DisputePortal.Api/Shared/Persistence/DisputePortalDbContext.cs
+++ b/src/DisputePortal.Api/Shared/Persistence/DisputePortalDbContext.cs
@@ -39,3 +39,3 @@ public sealed class DisputePortalDbContext(DbContextOptions<DisputePortalDbConte
-        modelBuilder.Entity<Account>().HasQueryFilter(OwnerFilter, account => CurrentCustomerId != null && account.CustomerId == CurrentCustomerId);
-        modelBuilder.Entity<Transaction>().HasQueryFilter(OwnerFilter, transaction => CurrentCustomerId != null && transaction.CustomerId == CurrentCustomerId);
-        modelBuilder.Entity<Dispute>().HasQueryFilter(OwnerFilter, dispute => CurrentCustomerId != null && dispute.CustomerId == CurrentCustomerId);
+        modelBuilder.Entity<Account>().HasQueryFilter(OwnerFilter, account => CurrentCustomerId == null || account.CustomerId == CurrentCustomerId);
+        modelBuilder.Entity<Transaction>().HasQueryFilter(OwnerFilter, transaction => CurrentCustomerId == null || transaction.CustomerId == CurrentCustomerId);
+        modelBuilder.Entity<Dispute>().HasQueryFilter(OwnerFilter, dispute => CurrentCustomerId == null || dispute.CustomerId == CurrentCustomerId);
@@ -43,2 +43,2 @@ public sealed class DisputePortalDbContext(DbContextOptions<DisputePortalDbConte
-        modelBuilder.Entity<DisputeStatusChange>().HasQueryFilter(OwnerFilter, change => CurrentCustomerId != null
-            && Set<Dispute>().Any(dispute => dispute.Id == change.DisputeId && dispute.CustomerId == CurrentCustomerId));
+        modelBuilder.Entity<DisputeStatusChange>().HasQueryFilter(OwnerFilter, change => CurrentCustomerId == null
+            || Set<Dispute>().Any(dispute => dispute.Id == change.DisputeId && dispute.CustomerId == CurrentCustomerId));
```

With the mutation applied:

```
$ dotnet test tests/DisputePortal.UnitTests
Passed!  - Failed:     0, Passed:   475, Skipped:     0, Total:   475, Duration: 243 ms - DisputePortal.UnitTests.dll (net10.0)
$ dotnet test tests/DisputePortal.IntegrationTests
Failed!  - Failed:     2, Passed:   305, Skipped:     0, Total:   307, Duration: 13 s - DisputePortal.IntegrationTests.dll (net10.0)
```

Red tests (verbatim):

```
unit:
none

integration:
Failed DisputePortal.IntegrationTests.OwnerFilterTests.WithNoCurrentCustomerTheAppSeesNoDisputesOrHistory [25 ms]
Failed DisputePortal.IntegrationTests.OwnerFilterTests.WithNoCurrentCustomerTheAppSeesNoTransactionsOrAccounts [21 ms]
```

First assertion messages (stack traces dropped):

```
integration:
OwnerFilterTests.WithNoCurrentCustomerTheAppSeesNoDisputesOrHistory
    Assert.Equal() Failure: Values differ
    Expected: 0
    Actual:   8
OwnerFilterTests.WithNoCurrentCustomerTheAppSeesNoTransactionsOrAccounts
    Assert.Equal() Failure: Values differ
    Expected: 0
    Actual:   70181
```

Restore: `git checkout -- .`; `git status --porcelain` printed nothing; same suites again:

```
$ dotnet test tests/DisputePortal.UnitTests
Passed!  - Failed:     0, Passed:   475, Skipped:     0, Total:   475, Duration: 247 ms - DisputePortal.UnitTests.dll (net10.0)
$ dotnet test tests/DisputePortal.IntegrationTests
Passed!  - Failed:     0, Passed:   307, Skipped:     0, Total:   307, Duration: 18 s - DisputePortal.IntegrationTests.dll (net10.0)
```

## M2b - Owner query filter dropped for Dispute

File: `src/DisputePortal.Api/Shared/Persistence/DisputePortalDbContext.cs:41`

The `Dispute` entity loses its Owner filter, so customers can read and act on each other's disputes.

```diff
diff --git a/src/DisputePortal.Api/Shared/Persistence/DisputePortalDbContext.cs b/src/DisputePortal.Api/Shared/Persistence/DisputePortalDbContext.cs
index 707a02c..995fdf1 100644
--- a/src/DisputePortal.Api/Shared/Persistence/DisputePortalDbContext.cs
+++ b/src/DisputePortal.Api/Shared/Persistence/DisputePortalDbContext.cs
@@ -41 +40,0 @@ public sealed class DisputePortalDbContext(DbContextOptions<DisputePortalDbConte
-        modelBuilder.Entity<Dispute>().HasQueryFilter(OwnerFilter, dispute => CurrentCustomerId != null && dispute.CustomerId == CurrentCustomerId);
```

Note: 31 red tests. Most of them are knock-on failures (hypothesis, not separately verified: the active-dispute count in `GetSubmissionFactsAsync` is no longer limited to the caller, so submissions return 422 instead of 201 - 16 of the failures read `Expected: Created / Actual: UnprocessableEntity`). The tests that name the property directly are `OwnerFilterTests.*` and `ArchitectureTests.EveryEntityWithACustomerIdHasTheOwnerFilter` / `EveryEntityThatReferencesAnOwnedEntityHasTheOwnerFilter`.

With the mutation applied:

```
$ dotnet test tests/DisputePortal.UnitTests
Passed!  - Failed:     0, Passed:   475, Skipped:     0, Total:   475, Duration: 235 ms - DisputePortal.UnitTests.dll (net10.0)
$ dotnet test tests/DisputePortal.IntegrationTests
Failed!  - Failed:    31, Passed:   276, Skipped:     0, Total:   307, Duration: 13 s - DisputePortal.IntegrationTests.dll (net10.0)
```

Red tests (verbatim):

```
unit:
none

integration:
Failed DisputePortal.IntegrationTests.TransactionPlanTests.TheKeysetQueryIsAnIndexScanWithNoSortOn50kRows [1 s]
Failed DisputePortal.IntegrationTests.OwnerFilterTests.WithNoCurrentCustomerTheAppSeesNoDisputesOrHistory [11 ms]
Failed DisputePortal.IntegrationTests.OwnerFilterTests.WithACurrentCustomerTheAppSeesOnlyThatCustomersDisputesAndHistory [13 ms]
Failed DisputePortal.IntegrationTests.DisputesTests.TheHistoryWalksEveryStatusNewestFirstAndFiltersByStatus [68 ms]
Failed DisputePortal.IntegrationTests.DisputesTests.ReusingAnIdForAnotherTransactionIs409IdempotencyKeyReuse [7 ms]
Failed DisputePortal.IntegrationTests.DisputesTests.ReplayingTheSameIdReturnsTheOriginalWithoutASecondRow [10 ms]
Failed DisputePortal.IntegrationTests.DisputesTests.DisputesWithIdenticalCreatedAtArePagedWithoutDuplicatesOrGaps(limit: 1) [92 ms]
Failed DisputePortal.IntegrationTests.DisputesTests.DisputesWithIdenticalCreatedAtArePagedWithoutDuplicatesOrGaps(limit: 2) [47 ms]
Failed DisputePortal.IntegrationTests.DisputesTests.AnotherCustomersDisputeIdIs409DisputeIdConflictWithoutLeaking [8 ms]
Failed DisputePortal.IntegrationTests.DisputesTests.SubmittingCreatesADisputeWithLocationETagAndOneTimelineEntry [9 ms]
Failed DisputePortal.IntegrationTests.DisputesTests.AZeroAmountIs422AmountNotPositive [9 ms]
Failed DisputePortal.IntegrationTests.DisputesTests.TheDisputeShowsInMyHistoryAndBlocksTheTransaction [7 ms]
Failed DisputePortal.IntegrationTests.DisputeSecurityTests.AStaleOrLooseIfMatchIs412WithTheCurrentETag(ifMatchTemplate: "W/\"{0}\"") [8 ms]
Failed DisputePortal.IntegrationTests.DisputeSecurityTests.AStaleOrLooseIfMatchIs412WithTheCurrentETag(ifMatchTemplate: "\"1\"") [8 ms]
Failed DisputePortal.IntegrationTests.DisputeSecurityTests.AStaleOrLooseIfMatchIs412WithTheCurrentETag(ifMatchTemplate: "not-an-etag") [7 ms]
Failed DisputePortal.IntegrationTests.DisputeSecurityTests.AStaleOrLooseIfMatchIs412WithTheCurrentETag(ifMatchTemplate: "*") [7 ms]
Failed DisputePortal.IntegrationTests.DisputeSecurityTests.WithdrawingAForeignOrMissingIdWithoutIfMatchIs404Not428 [9 ms]
Failed DisputePortal.IntegrationTests.DisputeSecurityTests.WithdrawingWithoutIfMatchIs428 [9 ms]
Failed DisputePortal.IntegrationTests.DisputeSecurityTests.TheDetailCarriesTheSameETagAsTheSubmission [9 ms]
Failed DisputePortal.IntegrationTests.DisputeSecurityTests.TheTimelineNeverCarriesActorIds [9 ms]
Failed DisputePortal.IntegrationTests.DisputeSecurityTests.AnotherCustomersDisputeIs404OnReadAndWithdrawLikeAMissingOne [12 ms]
Failed DisputePortal.IntegrationTests.DisputeSecurityTests.ATamperedForeignOrRefilteredCursorIs400InvalidCursor [9 ms]
Failed DisputePortal.IntegrationTests.DisputeSecurityTests.AfterAWithdrawalTheOldETagIs412AndTheNewOne409InvalidTransition [7 ms]
Failed DisputePortal.IntegrationTests.DisputeHistoryOrderTests.TiesOnCreatedAtComeOutByIdDescendingEvenWithoutTheIndex [34 ms]
Failed DisputePortal.IntegrationTests.DisputeConcurrencyTests.AnEfRetryAfterALostCommitAcknowledgementReplaysInsteadOfInsertingTwice [115 ms]
Failed DisputePortal.IntegrationTests.DisputeConcurrencyTests.TwoWithdrawalsWithTheSameETagGiveOne200AndOne412WithTheNewETag [106 ms]
Failed DisputePortal.IntegrationTests.DisputeConcurrencyTests.TheSameSubmissionRacingItselfGivesOne201AndOne200WithTheSameBody [111 ms]
Failed DisputePortal.IntegrationTests.DisputeConcurrencyTests.TwoSubmitsForOneTransactionHeldTogetherGiveOne201AndOne409DisputeExists [103 ms]
Failed DisputePortal.IntegrationTests.Bola404MetricTests.EveryNotFoundAnsweredToACustomerCountsExactlyOne(path: "foreign-dispute-withdraw") [6 ms]
Failed DisputePortal.IntegrationTests.ArchitectureTests.EveryEntityWithACustomerIdHasTheOwnerFilter [18 ms]
Failed DisputePortal.IntegrationTests.ArchitectureTests.EveryEntityThatReferencesAnOwnedEntityHasTheOwnerFilter [1 ms]
```

First assertion messages (stack traces dropped):

```
integration:
TransactionPlanTests.TheKeysetQueryIsAnIndexScanWithNoSortOn50kRows
    Assert.DoesNotContain() Failure: Filter matched in collection
    ↓ (pos 5)
    Collection: [···, {
OwnerFilterTests.WithNoCurrentCustomerTheAppSeesNoDisputesOrHistory
    Assert.Equal() Failure: Values differ
    Expected: 0
    Actual:   8
```

Restore: `git checkout -- .`; `git status --porcelain` printed nothing; same suites again:

```
$ dotnet test tests/DisputePortal.UnitTests
Passed!  - Failed:     0, Passed:   475, Skipped:     0, Total:   475, Duration: 261 ms - DisputePortal.UnitTests.dll (net10.0)
$ dotnet test tests/DisputePortal.IntegrationTests
Passed!  - Failed:     0, Passed:   307, Skipped:     0, Total:   307, Duration: 13 s - DisputePortal.IntegrationTests.dll (net10.0)
```

## M2c - IgnoreQueryFilters used outside Features/Agent

File: `src/DisputePortal.Api/Features/Disputes/DisputeRepository.cs:11`

The customer-facing `FindAsync` (used by withdraw) bypasses the filters. Only `Features/Agent` is allowed to.

```diff
diff --git a/src/DisputePortal.Api/Features/Disputes/DisputeRepository.cs b/src/DisputePortal.Api/Features/Disputes/DisputeRepository.cs
index 930839a..970297f 100644
--- a/src/DisputePortal.Api/Features/Disputes/DisputeRepository.cs
+++ b/src/DisputePortal.Api/Features/Disputes/DisputeRepository.cs
@@ -11 +11 @@ public sealed class DisputeRepository(DisputePortalDbContext db) : IDisputeRepos
-        db.Disputes.Include(dispute => dispute.StatusChanges).SingleOrDefaultAsync(dispute => dispute.Id == id, cancellationToken);
+        db.Disputes.IgnoreQueryFilters().Include(dispute => dispute.StatusChanges).SingleOrDefaultAsync(dispute => dispute.Id == id, cancellationToken);
```

Note: `DisputeSecurityTests.AnotherCustomersDisputeIs404OnReadAndWithdrawLikeAMissingOne` shows the actual leak (`Expected: "Submitted"  Actual: "Withdrawn"`: another customer's dispute was withdrawn). `ArchitectureTests.OnlyTheAgentFeatureMayBypassQueryFilters` fails independently of any behaviour, because it scans source text for the string `IgnoreQueryFilters`.

With the mutation applied:

```
$ dotnet test tests/DisputePortal.UnitTests
Passed!  - Failed:     0, Passed:   475, Skipped:     0, Total:   475, Duration: 332 ms - DisputePortal.UnitTests.dll (net10.0)
$ dotnet test tests/DisputePortal.IntegrationTests
Failed!  - Failed:     4, Passed:   303, Skipped:     0, Total:   307, Duration: 15 s - DisputePortal.IntegrationTests.dll (net10.0)
```

Red tests (verbatim):

```
unit:
none

integration:
Failed DisputePortal.IntegrationTests.DisputeSecurityTests.WithdrawingAForeignOrMissingIdWithoutIfMatchIs404Not428 [15 ms]
Failed DisputePortal.IntegrationTests.DisputeSecurityTests.AnotherCustomersDisputeIs404OnReadAndWithdrawLikeAMissingOne [22 ms]
Failed DisputePortal.IntegrationTests.Bola404MetricTests.EveryNotFoundAnsweredToACustomerCountsExactlyOne(path: "foreign-dispute-withdraw") [4 ms]
Failed DisputePortal.IntegrationTests.ArchitectureTests.OnlyTheAgentFeatureMayBypassQueryFilters [38 ms]
```

First assertion messages (stack traces dropped):

```
integration:
DisputeSecurityTests.WithdrawingAForeignOrMissingIdWithoutIfMatchIs404Not428
    Assert.Equal() Failure: Values differ
    Expected: NotFound
    Actual:   PreconditionRequired
DisputeSecurityTests.AnotherCustomersDisputeIs404OnReadAndWithdrawLikeAMissingOne
    Assert.Equal() Failure: Strings differ
    ↓ (pos 0)
    Expected: "Submitted"
```

Restore: `git checkout -- .`; `git status --porcelain` printed nothing; same suites again:

```
$ dotnet test tests/DisputePortal.UnitTests
Passed!  - Failed:     0, Passed:   475, Skipped:     0, Total:   475, Duration: 248 ms - DisputePortal.UnitTests.dll (net10.0)
$ dotnet test tests/DisputePortal.IntegrationTests
Passed!  - Failed:     0, Passed:   307, Skipped:     0, Total:   307, Duration: 13 s - DisputePortal.IntegrationTests.dll (net10.0)
```

## M3a - If-Match checks removed from the customer withdraw

File: `src/DisputePortal.Api/Features/Disputes/DisputeCommands.cs:89-97`

Both the 428 (header missing) and the 412 (stale, weak, `*`) checks are deleted from `WithdrawAsync`.

```diff
diff --git a/src/DisputePortal.Api/Features/Disputes/DisputeCommands.cs b/src/DisputePortal.Api/Features/Disputes/DisputeCommands.cs
index 599de90..f208f10 100644
--- a/src/DisputePortal.Api/Features/Disputes/DisputeCommands.cs
+++ b/src/DisputePortal.Api/Features/Disputes/DisputeCommands.cs
@@ -89,10 +88,0 @@ public sealed class DisputeCommands(
-        if (ifMatch is null)
-        {
-            throw new PreconditionRequiredException();
-        }
-
-        if (!DisputeETag.Matches(ifMatch, dispute.Version))
-        {
-            throw new PreconditionFailedException(DisputeETag.For(dispute.Version));
-        }
-
```

Note: `DisputeConcurrencyTests` stayed green: the `xmin` check at write time is a separate layer, and the forced-interleaving test does not depend on the precondition check (see M4d for the reverse).

With the mutation applied:

```
$ dotnet test tests/DisputePortal.UnitTests
Passed!  - Failed:     0, Passed:   475, Skipped:     0, Total:   475, Duration: 235 ms - DisputePortal.UnitTests.dll (net10.0)
$ dotnet test tests/DisputePortal.IntegrationTests
Failed!  - Failed:     6, Passed:   301, Skipped:     0, Total:   307, Duration: 14 s - DisputePortal.IntegrationTests.dll (net10.0)
```

Red tests (verbatim):

```
unit:
none

integration:
Failed DisputePortal.IntegrationTests.DisputeSecurityTests.AStaleOrLooseIfMatchIs412WithTheCurrentETag(ifMatchTemplate: "W/\"{0}\"") [13 ms]
Failed DisputePortal.IntegrationTests.DisputeSecurityTests.AStaleOrLooseIfMatchIs412WithTheCurrentETag(ifMatchTemplate: "\"1\"") [13 ms]
Failed DisputePortal.IntegrationTests.DisputeSecurityTests.AStaleOrLooseIfMatchIs412WithTheCurrentETag(ifMatchTemplate: "not-an-etag") [12 ms]
Failed DisputePortal.IntegrationTests.DisputeSecurityTests.AStaleOrLooseIfMatchIs412WithTheCurrentETag(ifMatchTemplate: "*") [13 ms]
Failed DisputePortal.IntegrationTests.DisputeSecurityTests.WithdrawingWithoutIfMatchIs428 [13 ms]
Failed DisputePortal.IntegrationTests.DisputeSecurityTests.AfterAWithdrawalTheOldETagIs412AndTheNewOne409InvalidTransition [15 ms]
```

First assertion messages (stack traces dropped):

```
integration:
DisputeSecurityTests.AStaleOrLooseIfMatchIs412WithTheCurrentETag(ifMatchTemplate: "W/\"{0}\"")
    Assert.Equal() Failure: Values differ
    Expected: PreconditionFailed
    Actual:   OK
DisputeSecurityTests.AStaleOrLooseIfMatchIs412WithTheCurrentETag(ifMatchTemplate: "\"1\"")
    Assert.Equal() Failure: Values differ
    Expected: PreconditionFailed
    Actual:   OK
```

Restore: `git checkout -- .`; `git status --porcelain` printed nothing; same suites again:

```
$ dotnet test tests/DisputePortal.UnitTests
Passed!  - Failed:     0, Passed:   475, Skipped:     0, Total:   475, Duration: 230 ms - DisputePortal.UnitTests.dll (net10.0)
$ dotnet test tests/DisputePortal.IntegrationTests
Passed!  - Failed:     0, Passed:   307, Skipped:     0, Total:   307, Duration: 16 s - DisputePortal.IntegrationTests.dll (net10.0)
```

## M3b - If-Match checks removed from the agent transition

File: `src/DisputePortal.Api/Features/Agent/AgentCommands.cs:29-37`

Same removal in `TransitionAsync`.

```diff
diff --git a/src/DisputePortal.Api/Features/Agent/AgentCommands.cs b/src/DisputePortal.Api/Features/Agent/AgentCommands.cs
index 86ded65..f2ad1e3 100644
--- a/src/DisputePortal.Api/Features/Agent/AgentCommands.cs
+++ b/src/DisputePortal.Api/Features/Agent/AgentCommands.cs
@@ -29,10 +28,0 @@ public sealed class AgentCommands(
-        if (ifMatch is null)
-        {
-            throw new PreconditionRequiredException();
-        }
-
-        if (!DisputeETag.Matches(ifMatch, dispute.Version))
-        {
-            throw new PreconditionFailedException(DisputeETag.For(dispute.Version));
-        }
-
```

With the mutation applied:

```
$ dotnet test tests/DisputePortal.UnitTests
Passed!  - Failed:     0, Passed:   475, Skipped:     0, Total:   475, Duration: 245 ms - DisputePortal.UnitTests.dll (net10.0)
$ dotnet test tests/DisputePortal.IntegrationTests
Failed!  - Failed:     7, Passed:   300, Skipped:     0, Total:   307, Duration: 13 s - DisputePortal.IntegrationTests.dll (net10.0)
```

Red tests (verbatim):

```
unit:
none

integration:
Failed DisputePortal.IntegrationTests.ResolutionDurationMetricTests.AReviewAStaleTagAndAnIllegalMoveRecordNoDuration [113 ms]
Failed DisputePortal.IntegrationTests.AgentTransitionTests.AStaleETagIs412EvenForAnIllegalMove [5 ms]
Failed DisputePortal.IntegrationTests.AgentTransitionTests.WithoutIfMatchTheMoveIs428AndNothingChanges [7 ms]
Failed DisputePortal.IntegrationTests.AgentTransitionTests.AStaleWeakOrLooseIfMatchIs412WithTheCurrentETag(ifMatchTemplate: "not-an-etag") [7 ms]
Failed DisputePortal.IntegrationTests.AgentTransitionTests.AStaleWeakOrLooseIfMatchIs412WithTheCurrentETag(ifMatchTemplate: "*") [8 ms]
Failed DisputePortal.IntegrationTests.AgentTransitionTests.AStaleWeakOrLooseIfMatchIs412WithTheCurrentETag(ifMatchTemplate: "\"1\"") [8 ms]
Failed DisputePortal.IntegrationTests.AgentTransitionTests.AStaleWeakOrLooseIfMatchIs412WithTheCurrentETag(ifMatchTemplate: "W/\"{0}\"") [7 ms]
```

First assertion messages (stack traces dropped):

```
integration:
ResolutionDurationMetricTests.AReviewAStaleTagAndAnIllegalMoveRecordNoDuration
    Assert.Equal() Failure: Values differ
    Expected: PreconditionFailed
    Actual:   Conflict
AgentTransitionTests.AStaleETagIs412EvenForAnIllegalMove
    Assert.Equal() Failure: Values differ
    Expected: PreconditionFailed
    Actual:   Conflict
```

Restore: `git checkout -- .`; `git status --porcelain` printed nothing; same suites again:

```
$ dotnet test tests/DisputePortal.UnitTests
Passed!  - Failed:     0, Passed:   475, Skipped:     0, Total:   475, Duration: 234 ms - DisputePortal.UnitTests.dll (net10.0)
$ dotnet test tests/DisputePortal.IntegrationTests
Passed!  - Failed:     0, Passed:   307, Skipped:     0, Total:   307, Duration: 16 s - DisputePortal.IntegrationTests.dll (net10.0)
```

## M4a - Sensitive-data rule removed from the description validator

File: `src/DisputePortal.Api/Features/Disputes/CreateDisputeRequestValidator.cs:38-43`

A card or ID number in a dispute description is no longer refused with `sensitive_data`.

```diff
diff --git a/src/DisputePortal.Api/Features/Disputes/CreateDisputeRequestValidator.cs b/src/DisputePortal.Api/Features/Disputes/CreateDisputeRequestValidator.cs
index 4cf9dda..473d0e1 100644
--- a/src/DisputePortal.Api/Features/Disputes/CreateDisputeRequestValidator.cs
+++ b/src/DisputePortal.Api/Features/Disputes/CreateDisputeRequestValidator.cs
@@ -38,6 +37,0 @@ public sealed partial class CreateDisputeRequestValidator : AbstractValidator<Cr
-        RuleFor(request => request.Description)
-            .Must(description => !SensitiveNumberDetector.ContainsSensitiveNumber(description!.Trim()))
-            .When(request => request.Description?.Trim() is { Length: <= Dispute.DescriptionMaxLength }
-                && !SensitiveNumberDetector.ContainsInvalidCharacters(request.Description))
-            .WithMessage("description must not contain a card or ID number; describe the problem without it.")
-            .WithState(_ => new ProblemCode(SensitiveNumberDetector.Code));
```

With the mutation applied:

```
$ dotnet test tests/DisputePortal.UnitTests
Failed!  - Failed:     7, Passed:   468, Skipped:     0, Total:   475, Duration: 248 ms - DisputePortal.UnitTests.dll (net10.0)
$ dotnet test tests/DisputePortal.IntegrationTests
Failed!  - Failed:     3, Passed:   304, Skipped:     0, Total:   307, Duration: 13 s - DisputePortal.IntegrationTests.dll (net10.0)
```

Red tests (verbatim):

```
unit:
Failed DisputePortal.UnitTests.CreateDisputeRequestValidatorTests.ACardNumberInTheDescriptionIsSensitiveData [53 ms]
Failed DisputePortal.UnitTests.CreateDisputeRequestValidatorTests.APaddedCardNumberIsStillSensitiveData(leading: 0, trailing: 5000) [< 1 ms]
Failed DisputePortal.UnitTests.CreateDisputeRequestValidatorTests.APaddedCardNumberIsStillSensitiveData(leading: 20000, trailing: 20000) [2 ms]
Failed DisputePortal.UnitTests.CreateDisputeRequestValidatorTests.APaddedCardNumberIsStillSensitiveData(leading: 0, trailing: 0) [< 1 ms]
Failed DisputePortal.UnitTests.CreateDisputeRequestValidatorTests.APaddedCardNumberIsStillSensitiveData(leading: 10001, trailing: 0) [< 1 ms]
Failed DisputePortal.UnitTests.CreateDisputeRequestValidatorTests.APaddedCardNumberIsStillSensitiveData(leading: 5000, trailing: 0) [< 1 ms]
Failed DisputePortal.UnitTests.CreateDisputeRequestValidatorTests.APaddedCardNumberIsStillSensitiveData(leading: 10000, trailing: 0) [< 1 ms]

integration:
Failed DisputePortal.IntegrationTests.DisputesTests.InvalidBodiesAre400WithTheFieldNamed(invalid: "two-space-card", field: "description", code: "sensitive_data") [50 ms]
Failed DisputePortal.IntegrationTests.DisputesTests.InvalidBodiesAre400WithTheFieldNamed(invalid: "card-number", field: "description", code: "sensitive_data") [8 ms]
Failed DisputePortal.IntegrationTests.DisputesTests.InvalidBodiesAre400WithTheFieldNamed(invalid: "sa-id-number", field: "description", code: "sensitive_data") [7 ms]
```

First assertion messages (stack traces dropped):

```
unit:
CreateDisputeRequestValidatorTests.ACardNumberInTheDescriptionIsSensitiveData
    Assert.Single() Failure: The collection was empty
CreateDisputeRequestValidatorTests.APaddedCardNumberIsStillSensitiveData(leading: 0, trailing: 5000)
    Assert.Single() Failure: The collection was empty
integration:
DisputesTests.InvalidBodiesAre400WithTheFieldNamed(invalid: "two-space-card", field: "description", code: "sensitive_data")
    Assert.Equal() Failure: Values differ
    Expected: BadRequest
    Actual:   Created
DisputesTests.InvalidBodiesAre400WithTheFieldNamed(invalid: "card-number", field: "description", code: "sensitive_data")
    Assert.Equal() Failure: Values differ
    Expected: BadRequest
    Actual:   Created
```

Restore: `git checkout -- .`; `git status --porcelain` printed nothing; same suites again:

```
$ dotnet test tests/DisputePortal.UnitTests
Passed!  - Failed:     0, Passed:   475, Skipped:     0, Total:   475, Duration: 240 ms - DisputePortal.UnitTests.dll (net10.0)
$ dotnet test tests/DisputePortal.IntegrationTests
Passed!  - Failed:     0, Passed:   307, Skipped:     0, Total:   307, Duration: 14 s - DisputePortal.IntegrationTests.dll (net10.0)
```

## M4a2 - Luhn check never passes

File: `src/DisputePortal.Domain/Disputes/SensitiveNumberDetector.cs:87`

`PassesLuhn` returns `false` for every input, so the detector never flags a card or SA ID number (descriptions and agent notes).

```diff
diff --git a/src/DisputePortal.Domain/Disputes/SensitiveNumberDetector.cs b/src/DisputePortal.Domain/Disputes/SensitiveNumberDetector.cs
index 51a2123..8357f34 100644
--- a/src/DisputePortal.Domain/Disputes/SensitiveNumberDetector.cs
+++ b/src/DisputePortal.Domain/Disputes/SensitiveNumberDetector.cs
@@ -87 +87 @@ public static partial class SensitiveNumberDetector
-        return sum % 10 == 0;
+        return false;
```

Note: Unit list trimmed (90 red tests: 7 in `CreateDisputeRequestValidatorTests`, 31 in `DisputeNoteTests`, 48 in `SensitiveNumberDetectorTests`, 4 in `TransitionRequestValidatorTests`).

With the mutation applied:

```
$ dotnet test tests/DisputePortal.UnitTests
Failed!  - Failed:    90, Passed:   385, Skipped:     0, Total:   475, Duration: 251 ms - DisputePortal.UnitTests.dll (net10.0)
$ dotnet test tests/DisputePortal.IntegrationTests
Failed!  - Failed:     7, Passed:   300, Skipped:     0, Total:   307, Duration: 18 s - DisputePortal.IntegrationTests.dll (net10.0)
```

Red tests (verbatim):

```
unit:
90 red tests (list trimmed). By class: CreateDisputeRequestValidatorTests x7, DisputeNoteTests x31, SensitiveNumberDetectorTests x48, TransitionRequestValidatorTests x4
First 8 ASCII-only names, verbatim:
Failed DisputePortal.UnitTests.SensitiveNumberDetectorTests.UpToFourSeparatorsJoinGroups(text: "4111    1111 1111 1111", sensitive: True) [1 ms]
Failed DisputePortal.UnitTests.DisputeNoteTests.ANoteAgreesWithTheSharedCases(name: "card split across a blank line", input: "4111 1111\n\n1111 1111", valid: False, code: "sensitive_data") [1 ms]
Failed DisputePortal.UnitTests.SensitiveNumberDetectorTests.UpToFourSeparatorsJoinGroups(text: "4111_1111_1111_1111", sensitive: True) [< 1 ms]
Failed DisputePortal.UnitTests.DisputeNoteTests.ANoteAgreesWithTheSharedCases(name: "card, underscore separated", input: "4111_1111_1111_1111", valid: False, code: "sensitive_data") [< 1 ms]
Failed DisputePortal.UnitTests.SensitiveNumberDetectorTests.UpToFourSeparatorsJoinGroups(text: "4111.1111.1111.1111", sensitive: True) [< 1 ms]
Failed DisputePortal.UnitTests.SensitiveNumberDetectorTests.UpToFourSeparatorsJoinGroups(text: "4111 -/ 1111(1111)1111", sensitive: True) [< 1 ms]
Failed DisputePortal.UnitTests.DisputeNoteTests.ANoteAgreesWithTheSharedCases(name: "card split across an indented wrapped line", input: "4111 1111\n   1111 1111", valid: False, code: "sensitive_data") [< 1 ms]
Failed DisputePortal.UnitTests.SensitiveNumberDetectorTests.UpToFourSeparatorsJoinGroups(text: "4111  1111 1111 1111", sensitive: True) [< 1 ms]

integration:
Failed DisputePortal.IntegrationTests.DisputesTests.InvalidBodiesAre400WithTheFieldNamed(invalid: "two-space-card", field: "description", code: "sensitive_data") [53 ms]
Failed DisputePortal.IntegrationTests.DisputesTests.InvalidBodiesAre400WithTheFieldNamed(invalid: "card-number", field: "description", code: "sensitive_data") [9 ms]
Failed DisputePortal.IntegrationTests.DisputesTests.InvalidBodiesAre400WithTheFieldNamed(invalid: "sa-id-number", field: "description", code: "sensitive_data") [7 ms]
Failed DisputePortal.IntegrationTests.AgentTransitionTests.TheNoteTextIsNeverLoggedOnlyIdsAndCodes [104 ms]
Failed DisputePortal.IntegrationTests.AgentTransitionTests.ABadMoveBodyIs400WithTheFieldNamed(toStatus: "Accepted", note: "Refunded. Card 4111 1111 1111 1111 on file.", field: "note", code: "sensitive_data") [9 ms]
Failed DisputePortal.IntegrationTests.AgentTransitionTests.ABadMoveBodyIs400WithTheFieldNamed(toStatus: "Accepted", note: "ID 800101 5009 087", field: "note", code: "sensitive_data") [8 ms]
Failed DisputePortal.IntegrationTests.AgentTransitionTests.APaddedCardNumberIsStillSensitiveData [12 ms]
```

First assertion messages (stack traces dropped):

```
unit:
SensitiveNumberDetectorTests.UpToFourSeparatorsJoinGroups(text: "4111    1111 1111 1111", sensitive: True)
    Assert.Equal() Failure: Values differ
    Expected: True
    Actual:   False
DisputeNoteTests.ANoteAgreesWithTheSharedCases(name: "card split across a blank line", input: "4111 1111\n\n1111 1111", valid: False, code: "sensitive_data")
    Assert.Throws() Failure: No exception was thrown
    Expected: typeof(DisputePortal.Domain.Common.DomainException)
integration:
DisputesTests.InvalidBodiesAre400WithTheFieldNamed(invalid: "two-space-card", field: "description", code: "sensitive_data")
    Assert.Equal() Failure: Values differ
    Expected: BadRequest
    Actual:   Created
DisputesTests.InvalidBodiesAre400WithTheFieldNamed(invalid: "card-number", field: "description", code: "sensitive_data")
    Assert.Equal() Failure: Values differ
    Expected: BadRequest
    Actual:   Created
```

Restore: `git checkout -- .`; `git status --porcelain` printed nothing; same suites again:

```
$ dotnet test tests/DisputePortal.UnitTests
Passed!  - Failed:     0, Passed:   475, Skipped:     0, Total:   475, Duration: 239 ms - DisputePortal.UnitTests.dll (net10.0)
$ dotnet test tests/DisputePortal.IntegrationTests
Passed!  - Failed:     0, Passed:   307, Skipped:     0, Total:   307, Duration: 17 s - DisputePortal.IntegrationTests.dll (net10.0)
```

## M4b - CSRF middleware not registered

File: `src/DisputePortal.Api/Program.cs:98`

`app.UseMiddleware<CsrfMiddleware>();` is deleted, so no request needs `X-CSRF: 1`.

```diff
diff --git a/src/DisputePortal.Api/Program.cs b/src/DisputePortal.Api/Program.cs
index eb479d7..ca07ff3 100644
--- a/src/DisputePortal.Api/Program.cs
+++ b/src/DisputePortal.Api/Program.cs
@@ -98 +97,0 @@ app.UseMiddleware<NoStoreMiddleware>();
-app.UseMiddleware<CsrfMiddleware>();
```

Note: The unit suite stays green (475/475) because `CsrfRuleTests` tests the rule function, not that the pipeline registers it. Only the integration tests catch this one.

With the mutation applied:

```
$ dotnet test tests/DisputePortal.UnitTests
Passed!  - Failed:     0, Passed:   475, Skipped:     0, Total:   475, Duration: 370 ms - DisputePortal.UnitTests.dll (net10.0)
$ dotnet test tests/DisputePortal.IntegrationTests
Failed!  - Failed:     9, Passed:   298, Skipped:     0, Total:   307, Duration: 14 s - DisputePortal.IntegrationTests.dll (net10.0)
```

Red tests (verbatim):

```
unit:
none

integration:
Failed DisputePortal.IntegrationTests.ProductionAuthStackTests.ASignedInRequestWithoutTheCsrfHeaderIsStillRefused [5 ms]
Failed DisputePortal.IntegrationTests.ProductionAuthStackTests.ARequestWithoutTheCsrfHeaderIs403CsrfRequired(method: "POST", path: "/bff/user") [< 1 ms]
Failed DisputePortal.IntegrationTests.ProductionAuthStackTests.ARequestWithoutTheCsrfHeaderIs403CsrfRequired(method: "POST", path: "/api/v1/me") [< 1 ms]
Failed DisputePortal.IntegrationTests.ProductionAuthStackTests.ARequestWithoutTheCsrfHeaderIs403CsrfRequired(method: "GET", path: "/api/v1/me") [< 1 ms]
Failed DisputePortal.IntegrationTests.ProductionAuthStackTests.AMissingCsrfHeaderWithAnHtmlAcceptIsStillA403Problem [< 1 ms]
Failed DisputePortal.IntegrationTests.AuthHardeningTests.AnyCsrfHeaderValueOtherThanExactlyOneIsRefused(value: "") [3 ms]
Failed DisputePortal.IntegrationTests.AuthHardeningTests.AnyCsrfHeaderValueOtherThanExactlyOneIsRefused(value: "true") [2 ms]
Failed DisputePortal.IntegrationTests.AuthHardeningTests.AnyCsrfHeaderValueOtherThanExactlyOneIsRefused(value: "0") [3 ms]
Failed DisputePortal.IntegrationTests.AuthHardeningTests.AnyCsrfHeaderValueOtherThanExactlyOneIsRefused(value: "1, 1") [3 ms]
```

First assertion messages (stack traces dropped):

```
integration:
ProductionAuthStackTests.ASignedInRequestWithoutTheCsrfHeaderIsStillRefused
    Assert.Equal() Failure: Values differ
    Expected: Forbidden
    Actual:   OK
ProductionAuthStackTests.ARequestWithoutTheCsrfHeaderIs403CsrfRequired(method: "POST", path: "/bff/user")
    Assert.Equal() Failure: Values differ
    Expected: Forbidden
    Actual:   Unauthorized
```

Restore: `git checkout -- .`; `git status --porcelain` printed nothing; same suites again:

```
$ dotnet test tests/DisputePortal.UnitTests
Passed!  - Failed:     0, Passed:   475, Skipped:     0, Total:   475, Duration: 253 ms - DisputePortal.UnitTests.dll (net10.0)
$ dotnet test tests/DisputePortal.IntegrationTests
Passed!  - Failed:     0, Passed:   307, Skipped:     0, Total:   307, Duration: 18 s - DisputePortal.IntegrationTests.dll (net10.0)
```

## M4b2 - CSRF no longer required on non-GET /bff

File: `src/DisputePortal.Api/Shared/Security/CsrfMiddleware.cs:31-33`

`RequiresHeader` keeps `/api` and drops the `/bff` POST/DELETE clause (logout, session delete).

```diff
diff --git a/src/DisputePortal.Api/Shared/Security/CsrfMiddleware.cs b/src/DisputePortal.Api/Shared/Security/CsrfMiddleware.cs
index af2381a..63ff030 100644
--- a/src/DisputePortal.Api/Shared/Security/CsrfMiddleware.cs
+++ b/src/DisputePortal.Api/Shared/Security/CsrfMiddleware.cs
@@ -31,3 +31 @@ public sealed class CsrfMiddleware(RequestDelegate next, SecurityMetrics metrics
-        request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
-        || (request.Path.StartsWithSegments("/bff", StringComparison.OrdinalIgnoreCase)
-            && !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method));
+        request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase);
```

With the mutation applied:

```
$ dotnet test tests/DisputePortal.UnitTests
Failed!  - Failed:     2, Passed:   473, Skipped:     0, Total:   475, Duration: 245 ms - DisputePortal.UnitTests.dll (net10.0)
$ dotnet test tests/DisputePortal.IntegrationTests
Failed!  - Failed:     1, Passed:   306, Skipped:     0, Total:   307, Duration: 13 s - DisputePortal.IntegrationTests.dll (net10.0)
```

Red tests (verbatim):

```
unit:
Failed DisputePortal.UnitTests.CsrfRuleTests.TheHeaderIsRequiredOnApiAndOnNonGetBff(method: "POST", path: "/bff/user", required: True) [14 ms]
Failed DisputePortal.UnitTests.CsrfRuleTests.TheHeaderIsRequiredOnApiAndOnNonGetBff(method: "DELETE", path: "/bff/session", required: True) [< 1 ms]

integration:
Failed DisputePortal.IntegrationTests.ProductionAuthStackTests.ARequestWithoutTheCsrfHeaderIs403CsrfRequired(method: "POST", path: "/bff/user") [< 1 ms]
```

First assertion messages (stack traces dropped):

```
unit:
CsrfRuleTests.TheHeaderIsRequiredOnApiAndOnNonGetBff(method: "POST", path: "/bff/user", required: True)
    Assert.Equal() Failure: Values differ
    Expected: True
    Actual:   False
CsrfRuleTests.TheHeaderIsRequiredOnApiAndOnNonGetBff(method: "DELETE", path: "/bff/session", required: True)
    Assert.Equal() Failure: Values differ
    Expected: True
    Actual:   False
integration:
ProductionAuthStackTests.ARequestWithoutTheCsrfHeaderIs403CsrfRequired(method: "POST", path: "/bff/user")
    Assert.Equal() Failure: Values differ
    Expected: Forbidden
    Actual:   Unauthorized
```

Restore: `git checkout -- .`; `git status --porcelain` printed nothing; same suites again:

```
$ dotnet test tests/DisputePortal.UnitTests
Passed!  - Failed:     0, Passed:   475, Skipped:     0, Total:   475, Duration: 236 ms - DisputePortal.UnitTests.dll (net10.0)
$ dotnet test tests/DisputePortal.IntegrationTests
Passed!  - Failed:     0, Passed:   307, Skipped:     0, Total:   307, Duration: 13 s - DisputePortal.IntegrationTests.dll (net10.0)
```

## M4c1 - 23505 (unique violation) translation removed

File: `src/DisputePortal.Api/Shared/Errors/DbErrorTranslator.cs:18`

A unique violation is no longer translated to `DuplicateKeyException`, so the idempotent-submit and 409 paths become 500s.

```diff
diff --git a/src/DisputePortal.Api/Shared/Errors/DbErrorTranslator.cs b/src/DisputePortal.Api/Shared/Errors/DbErrorTranslator.cs
index 6eeb6ba..6662be0 100644
--- a/src/DisputePortal.Api/Shared/Errors/DbErrorTranslator.cs
+++ b/src/DisputePortal.Api/Shared/Errors/DbErrorTranslator.cs
@@ -18 +17,0 @@ public static class DbErrorTranslator
-        { InnerException: PostgresException { SqlState: UniqueViolation } unique } => new DuplicateKeyException(unique.ConstraintName, exception),
```

Note: The first integration run of this mutation was invalid (Testcontainers reaper failure, not a test result); the numbers below are the re-run. See Run notes, item 2.

With the mutation applied:

```
$ dotnet test tests/DisputePortal.UnitTests
Failed!  - Failed:     2, Passed:   473, Skipped:     0, Total:   475, Duration: 240 ms - DisputePortal.UnitTests.dll (net10.0)
$ dotnet test tests/DisputePortal.IntegrationTests
Failed!  - Failed:     4, Passed:   303, Skipped:     0, Total:   307, Duration: 14 s - DisputePortal.IntegrationTests.dll (net10.0)
```

Red tests (verbatim):

```
unit:
Failed DisputePortal.UnitTests.DbErrorTranslatorTests.AUniqueViolationIsA409Duplicate [4 ms]
Failed DisputePortal.UnitTests.DbErrorTranslatorTests.TheCauseIsKeptForTheLog [< 1 ms]

integration:
Failed DisputePortal.IntegrationTests.DisputesTests.AnotherCustomersDisputeIdIs409DisputeIdConflictWithoutLeaking [35 ms]
Failed DisputePortal.IntegrationTests.DisputeConcurrencyTests.AnEfRetryAfterALostCommitAcknowledgementReplaysInsteadOfInsertingTwice [241 ms]
Failed DisputePortal.IntegrationTests.DisputeConcurrencyTests.TheSameSubmissionRacingItselfGivesOne201AndOne200WithTheSameBody [109 ms]
Failed DisputePortal.IntegrationTests.DisputeConcurrencyTests.TwoSubmitsForOneTransactionHeldTogetherGiveOne201AndOne409DisputeExists [107 ms]
```

First assertion messages (stack traces dropped):

```
unit:
DbErrorTranslatorTests.AUniqueViolationIsA409Duplicate
    Assert.Equal() Failure: Values differ
    Expected: Tuple (409, "duplicate")
    Actual:   Tuple (null, null)
DbErrorTranslatorTests.TheCauseIsKeptForTheLog
    Assert.Same() Failure: Values are not the same instance
    Expected: Microsoft.EntityFrameworkCore.DbUpdateException: save
    ---> Npgsql.PostgresException (0x80004005): 23505: message
integration:
DisputesTests.AnotherCustomersDisputeIdIs409DisputeIdConflictWithoutLeaking
    Assert.Equal() Failure: Values differ
    Expected: Conflict
    Actual:   InternalServerError
DisputeConcurrencyTests.AnEfRetryAfterALostCommitAcknowledgementReplaysInsteadOfInsertingTwice
    Assert.Equal() Failure: Values differ
    Expected: OK
    Actual:   InternalServerError
```

Restore: `git checkout -- .`; `git status --porcelain` printed nothing; same suites again:

```
$ dotnet test tests/DisputePortal.UnitTests
Passed!  - Failed:     0, Passed:   475, Skipped:     0, Total:   475, Duration: 232 ms - DisputePortal.UnitTests.dll (net10.0)
$ dotnet test tests/DisputePortal.IntegrationTests
Passed!  - Failed:     0, Passed:   307, Skipped:     0, Total:   307, Duration: 16 s - DisputePortal.IntegrationTests.dll (net10.0)
```

## M4c2 - Partial unique index made non-unique (database backstop)

File: `src/DisputePortal.Api/Shared/Persistence/Migrations/20261104113825_Disputes.cs:148`

`ux_disputes_one_active_per_transaction` is created with `unique: false`, so the database no longer refuses a second active dispute on one transaction.

```diff
diff --git a/src/DisputePortal.Api/Shared/Persistence/Migrations/20261104113825_Disputes.cs b/src/DisputePortal.Api/Shared/Persistence/Migrations/20261104113825_Disputes.cs
index d55d30c..8964771 100644
--- a/src/DisputePortal.Api/Shared/Persistence/Migrations/20261104113825_Disputes.cs
+++ b/src/DisputePortal.Api/Shared/Persistence/Migrations/20261104113825_Disputes.cs
@@ -148 +148 @@ namespace DisputePortal.Api.Shared.Persistence.Migrations
-                unique: true,
+                unique: false,
```

With the mutation applied:

```
$ dotnet test tests/DisputePortal.UnitTests
Passed!  - Failed:     0, Passed:   475, Skipped:     0, Total:   475, Duration: 231 ms - DisputePortal.UnitTests.dll (net10.0)
$ dotnet test tests/DisputePortal.IntegrationTests
Failed!  - Failed:     2, Passed:   305, Skipped:     0, Total:   307, Duration: 16 s - DisputePortal.IntegrationTests.dll (net10.0)
```

Red tests (verbatim):

```
unit:
none

integration:
Failed DisputePortal.IntegrationTests.ReadModelConstraintTests.ASecondActiveDisputeOnOneTransactionIsRefusedByThePartialUniqueIndex [3 ms]
Failed DisputePortal.IntegrationTests.DisputeConcurrencyTests.TwoSubmitsForOneTransactionHeldTogetherGiveOne201AndOne409DisputeExists [103 ms]
```

First assertion messages (stack traces dropped):

```
integration:
ReadModelConstraintTests.ASecondActiveDisputeOnOneTransactionIsRefusedByThePartialUniqueIndex
    Assert.Throws() Failure: No exception was thrown
    Expected: typeof(Npgsql.PostgresException)
DisputeConcurrencyTests.TwoSubmitsForOneTransactionHeldTogetherGiveOne201AndOne409DisputeExists
    Assert.Single() Failure: The collection contained 2 matching items
    Expected:      (predicate expression)
    Collection:    [StatusCode: 201, ReasonPhrase: 'Created', Version: 1.1, Content: System.Net.Http.StreamContent, Headers:
```

Restore: `git checkout -- .`; `git status --porcelain` printed nothing; same suites again:

```
$ dotnet test tests/DisputePortal.UnitTests
Passed!  - Failed:     0, Passed:   475, Skipped:     0, Total:   475, Duration: 228 ms - DisputePortal.UnitTests.dll (net10.0)
$ dotnet test tests/DisputePortal.IntegrationTests
Passed!  - Failed:     0, Passed:   307, Skipped:     0, Total:   307, Duration: 14 s - DisputePortal.IntegrationTests.dll (net10.0)
```

## M4d - Optimistic-concurrency conflict is overwritten ("client wins")

File: `src/DisputePortal.Api/Shared/Persistence/EfUnitOfWork.cs:12`

On `DbUpdateConcurrencyException` the unit of work copies the database's current values into the original values and saves again, so the stale writer silently wins instead of getting 412.

```diff
diff --git a/src/DisputePortal.Api/Shared/Persistence/EfUnitOfWork.cs b/src/DisputePortal.Api/Shared/Persistence/EfUnitOfWork.cs
index 39c6f7d..ec97313 100644
--- a/src/DisputePortal.Api/Shared/Persistence/EfUnitOfWork.cs
+++ b/src/DisputePortal.Api/Shared/Persistence/EfUnitOfWork.cs
@@ -12 +12,18 @@ public sealed class EfUnitOfWork(DisputePortalDbContext db) : IUnitOfWork
-            await db.SaveChangesAsync(cancellationToken);
+            try
+            {
+                await db.SaveChangesAsync(cancellationToken);
+            }
+            catch (DbUpdateConcurrencyException conflict)
+            {
+                // "Client wins": take the database's current values as the originals and write again.
+                foreach (Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry in conflict.Entries)
+                {
+                    Microsoft.EntityFrameworkCore.ChangeTracking.PropertyValues? current = await entry.GetDatabaseValuesAsync(cancellationToken);
+                    if (current is not null)
+                    {
+                        entry.OriginalValues.SetValues(current);
+                    }
+                }
+
+                await db.SaveChangesAsync(cancellationToken);
+            }
```

Note: Only the race tests went red; the 428/412 precondition tests stayed green. Two earlier attempts at this mutation are described in Run notes, item 3.

With the mutation applied:

```
$ dotnet test tests/DisputePortal.UnitTests
Passed!  - Failed:     0, Passed:   475, Skipped:     0, Total:   475, Duration: 232 ms - DisputePortal.UnitTests.dll (net10.0)
$ dotnet test tests/DisputePortal.IntegrationTests
Failed!  - Failed:     3, Passed:   304, Skipped:     0, Total:   307, Duration: 13 s - DisputePortal.IntegrationTests.dll (net10.0)
```

Red tests (verbatim):

```
unit:
none

integration:
Failed DisputePortal.IntegrationTests.ResolutionDurationMetricTests.TwoAgentsDecidingAtOnceRecordOneMeasurementForTheDecisionThatWasStored [139 ms]
Failed DisputePortal.IntegrationTests.DisputeConcurrencyTests.TwoWithdrawalsWithTheSameETagGiveOne200AndOne412WithTheNewETag [99 ms]
Failed DisputePortal.IntegrationTests.AgentTransitionTests.TwoAgentsWithTheSameETagGiveOne200AndOne412WithTheNewETag [106 ms]
```

First assertion messages (stack traces dropped):

```
integration:
ResolutionDurationMetricTests.TwoAgentsDecidingAtOnceRecordOneMeasurementForTheDecisionThatWasStored
    Assert.Single() Failure: The collection contained 2 matching items
    Expected:      (predicate expression)
    Collection:    [StatusCode: 200, ReasonPhrase: 'OK', Version: 1.1, Content: System.Net.Http.StreamContent, Headers:
DisputeConcurrencyTests.TwoWithdrawalsWithTheSameETagGiveOne200AndOne412WithTheNewETag
    Assert.Single() Failure: The collection contained 2 matching items
    Expected:      (predicate expression)
    Collection:    [StatusCode: 200, ReasonPhrase: 'OK', Version: 1.1, Content: System.Net.Http.StreamContent, Headers:
```

Restore: `git checkout -- .`; `git status --porcelain` printed nothing; same suites again:

```
$ dotnet test tests/DisputePortal.UnitTests
Passed!  - Failed:     0, Passed:   475, Skipped:     0, Total:   475, Duration: 228 ms - DisputePortal.UnitTests.dll (net10.0)
$ dotnet test tests/DisputePortal.IntegrationTests
Passed!  - Failed:     0, Passed:   307, Skipped:     0, Total:   307, Duration: 16 s - DisputePortal.IntegrationTests.dll (net10.0)
```

## B1 - Probe: history filter loses its own owner clause

File: `src/DisputePortal.Api/Shared/Persistence/DisputePortalDbContext.cs:44`

Extra probe, chosen because it looked redundant: the `DisputeStatusChange` filter checks `dispute.CustomerId == CurrentCustomerId` inside `Set<Dispute>().Any(...)`. Remove that clause and rely on the Dispute filter alone.

```diff
diff --git a/src/DisputePortal.Api/Shared/Persistence/DisputePortalDbContext.cs b/src/DisputePortal.Api/Shared/Persistence/DisputePortalDbContext.cs
index 707a02c..bbcde32 100644
--- a/src/DisputePortal.Api/Shared/Persistence/DisputePortalDbContext.cs
+++ b/src/DisputePortal.Api/Shared/Persistence/DisputePortalDbContext.cs
@@ -44 +44 @@ public sealed class DisputePortalDbContext(DbContextOptions<DisputePortalDbConte
-            && Set<Dispute>().Any(dispute => dispute.Id == change.DisputeId && dispute.CustomerId == CurrentCustomerId));
+            && Set<Dispute>().Any(dispute => dispute.Id == change.DisputeId));
```

Note: **This mutation survived.** See Survivors and gaps.

With the mutation applied:

```
$ dotnet test tests/DisputePortal.UnitTests
Passed!  - Failed:     0, Passed:   475, Skipped:     0, Total:   475, Duration: 239 ms - DisputePortal.UnitTests.dll (net10.0)
$ dotnet test tests/DisputePortal.IntegrationTests
Passed!  - Failed:     0, Passed:   307, Skipped:     0, Total:   307, Duration: 14 s - DisputePortal.IntegrationTests.dll (net10.0)
```

Red tests (verbatim):

```
unit:
none

integration:
none
```

Restore: `git checkout -- .`; `git status --porcelain` printed nothing; same suites again:

```
$ dotnet test tests/DisputePortal.UnitTests
Passed!  - Failed:     0, Passed:   475, Skipped:     0, Total:   475, Duration: 243 ms - DisputePortal.UnitTests.dll (net10.0)
$ dotnet test tests/DisputePortal.IntegrationTests
Passed!  - Failed:     0, Passed:   307, Skipped:     0, Total:   307, Duration: 18 s - DisputePortal.IntegrationTests.dll (net10.0)
```

## Run notes (things that did not go to plan)

1. **A flaky test failed once on a clean tree.** The first integration run after restoring M1 failed with one red test
   and no mutation in place (`git status --porcelain` was empty):

   ```
   ProductionAuthStackTests.TheSessionTicketInRedisIsEncryptedAndExpiresWithTheAbsoluteLifetime
       Assert.DoesNotContain() Failure: Sub-string found
       ↓ (pos 1378)
       String: ···"?жY?YyJ?????WiMz???=eyJ??:?!P|k???????$z?"···
       Found:  "eyJ"
   Failed!  - Failed:     1, Passed:   306, Skipped:     0, Total:   307, Duration: 17 s - DisputePortal.IntegrationTests.dll (net10.0)
   ```

   The immediate re-run on the same tree passed:

   ```
   Passed!  - Failed:     0, Passed:   307, Skipped:     0, Total:   307, Duration: 16 s - DisputePortal.IntegrationTests.dll (net10.0)
   ```

   Hypothesis (not proven): the test decodes the AES ciphertext of the Redis session ticket as UTF-8 and asserts it does
   not contain the three characters `eyJ` (`ProductionAuthStackTests.cs:137`); random bytes contain that sequence now and
   then, so the assertion has a small false-failure rate. Across the 17 integration runs I did on a clean tree it failed
   once. It is not related to any mutation. The test was left alone while the mutation runs were made. It was fixed afterwards in the same pull request: it now asserts only on the
   ticket its own sign-in created, and a check that stores tickets unencrypted still turns it red.

2. **One invalid integration run (infrastructure, not a test result).** The first integration run of M4c1 reported
   `Failed: 275, Passed: 32` after `1 m 4 s`, and all 275 failures carried
   `DotNet.Testcontainers.Containers.ResourceReaperException : Initialization has been cancelled.`, thrown from
   `ApiFactory.InitializeAsync` (the Testcontainers reaper container did not start in time). I re-ran it on the same
   mutated tree, without changing anything, and the re-run numbers are the ones reported for M4c1 above.

3. **M4d took three attempts.** (a) Disabling the `xmin` concurrency token in the model
   (`.IsRowVersion().IsConcurrencyToken(false)` in `DisputeConfiguration.cs:47`): the integration suite reported
   `Failed!  - Failed:   275, Passed:    32, Skipped:     0, Total:   307, Duration: 3 s - DisputePortal.IntegrationTests.dll (net10.0)` (seen twice, identical). The fixture itself failed (`Assert.Equal() Failure: Expected: 0 / Actual: 1` at
   `ApiFactory.InitializeAsync`, the migrate step), including `DatabasePrivilegeTests.TheModelHasNoChangesMissingFromTheMigrations`.
   A debugging run with the snapshot also edited showed the migrate step throwing EF's `PendingModelChangesWarning`
   exception, so the model-drift guard stops the run before any behaviour test can say anything. Caught, but not by the
   tests that are supposed to prove the concurrency behaviour. (b) Same change plus silencing the pending-changes
   warning: `Failed!  - Failed:    63, Passed:   244, Skipped:     0, Total:   307, Duration: 12 s - DisputePortal.IntegrationTests.dll (net10.0)`, mostly 500s (the app does not work with that mapping; I did not dig into why),
   so it cannot isolate the concurrency check. (c) A refresh of the original `xmin` just before `SaveChangesAsync` stayed
   green, but tracing showed both racing requests refreshed before either committed, so `WHERE xmin = N` still applied: a
   flawed mutation, not a surviving one, and discarded. The "client wins" retry reported as M4d is the mutation that
   actually removes the protection.


## Survivors and gaps

**Survivor: B1 (probe).** Removing `&& dispute.CustomerId == CurrentCustomerId` from the `DisputeStatusChange` filter
left both suites green (475/475 and 307/307). Most likely an equivalent mutant: the inner `Set<Dispute>()` is itself
filtered by the Dispute Owner filter, and `OwnerFilterTests.WithACurrentCustomerTheAppSeesOnlyThatCustomersDisputesAndHistory`
queries history directly as customer A with customer B's dispute present and still sees only A's. That is
evidence, not proof; I did not inspect the generated SQL. If it is equivalent, the clause is defence in depth that no test can
distinguish: no test fails when it is removed, and if the Dispute filter were later loosened, that clause would be the only
thing keeping history private. None of the M-series mutations survived.

**Gaps and weak spots found while doing this** (facts from the runs above; remedies are suggestions, nothing was changed):

1. *Fail-open ownership rests on two tests.* M2a (every Owner filter inverted to fail open) is caught only by
   `OwnerFilterTests.WithNoCurrentCustomerTheAppSeesNoDisputesOrHistory` and
   `...SeesNoTransactionsOrAccounts`. All HTTP-level BOLA tests stayed green (presumably because an authenticated request
   always has a current customer by the time the filter runs; not verified). Delete those two tests and this regression is invisible.
2. *Several guards are invisible to the unit suite.* M2a, M2b, M2c, M3a, M3b, M4b, M4c2 and M4d leave the unit suite at 475/475.
   They are caught only by the Docker-based integration suite. In particular M4b (CSRF middleware not registered) is
   caught only by integration tests; `CsrfRuleTests` checks the rule function, not the registration.
3. *The "no IgnoreQueryFilters outside Features/Agent" rule is a source-text scan.* It caught M2c, but it matches the
   literal string. I did not try other ways of bypassing the filters, so that remains untested here.
4. *Concurrency is protected at two independent layers, and each layer's tests are narrow.* M3a (precondition check
   removed) left the race tests green; M4d (xmin conflict overwritten) left the 428/412 precondition tests green. That is
   good, since each layer is tested on its own, but it also means the "stale writer wins" mutation was caught by
   exactly three tests (`DisputeConcurrencyTests.TwoWithdrawalsWithTheSameETagGiveOne200AndOne412WithTheNewETag`,
   `AgentTransitionTests.TwoAgentsWithTheSameETagGiveOne200AndOne412WithTheNewETag`,
   `ResolutionDurationMetricTests.TwoAgentsDecidingAtOnceRecordOneMeasurementForTheDecisionThatWasStored`).
5. *The model-level removal of the `xmin` token is caught by the model-drift guard, not by behaviour tests.* Fine in
   practice (a real change would need a migration), but it means that mutation could not be used to measure the
   concurrency tests (Run notes, item 3).
6. *One flaky integration test* (Run notes, item 1): `ProductionAuthStackTests.TheSessionTicketInRedisIsEncryptedAndExpiresWithTheAbsoluteLifetime`
   failed once in 17 clean-tree runs. Status: fixed in the same pull request after these runs (it now looks only at the ticket its own
   sign-in created, instead of every ticket in the shared Redis). The failure was never reproduced, so the root cause above is a
   hypothesis and the fix targets the shared-state reading of it.
7. *Environment sensitivity.* The Testcontainers reaper failed to start once during this session (Run notes,
   item 2). When it does, the report is 275 red tests rather than one clear infrastructure error.

**Not covered by this exercise:** the frontend (Vitest) and E2E (Playwright) suites were not mutated; the Stryker score
is a separate artifact; the "drop the whole 120-day rule" variant was not run separately (the two boundary mutations M1 and M1b
cover the rule's edges).

## Back-channel logout (ADR-011), branch `feat/backchannel-logout`

Same idea, run by a script (kept outside the repo) instead of by hand: each mutation is an exact-once text replacement
in one source file, the tests that should catch it run (`dotnet test` with a filter on the named test classes, not the
full suites), and the original text is written back in a `finally` block. `git status` was clean on `src/` and `tests/`
after every batch. Date 2026-11-04, same machine as above, against the code of commit `4ddb4be` (the M3 to M5, I1 and
I23 reruns: the code of `a18a2f0`). "N of M red" is
the summary line of the filtered run (V and M rows: `LogoutTokenValidatorTests`, `ManagementPortMiddlewareTests`,
`CsrfRuleTests`; I rows: `BackchannelLogoutTests`, plus `SessionStoreTests`, `OpenApiTests`, `ProductionAuthStackTests`
or `AuthHardeningTests` where the row's code is shared with them).

| Id | Mutation | File | Verdict | Filtered run |
|---|---|---|---|---|
| V1 | signature never checked | `LogoutTokenValidator.cs` | CAUGHT | 2 of 41 red |
| V2 | alg pre-check removed | `LogoutTokenValidator.cs` | CAUGHT | 2 of 41 red |
| V3 | typ logout+jwt not required | `LogoutTokenValidator.cs` | CAUGHT | 2 of 41 red |
| V4 | iss not checked | `LogoutTokenValidator.cs` | CAUGHT | 2 of 41 red |
| V5 | aud not checked | `LogoutTokenValidator.cs` | CAUGHT | 1 of 41 red |
| V6 | exp ignored | `LogoutTokenValidator.cs` | CAUGHT | 1 of 41 red |
| V7 | iat in the future accepted | `LogoutTokenValidator.cs` | CAUGHT | 2 of 41 red |
| V8 | stale iat accepted | `LogoutTokenValidator.cs` | CAUGHT | 2 of 41 red |
| V9 | iat optional | `LogoutTokenValidator.cs` | CAUGHT | 1 of 41 red |
| V10 | events not checked | `LogoutTokenValidator.cs` | CAUGHT | 4 of 41 red |
| V11 | nonce allowed | `LogoutTokenValidator.cs` | CAUGHT | 1 of 41 red |
| V12 | jti optional | `LogoutTokenValidator.cs` | CAUGHT | 2 of 41 red |
| V13 | sid optional | `LogoutTokenValidator.cs` | CAUGHT | 2 of 41 red |
| V14 | replay ignored | `LogoutTokenValidator.cs` | CAUGHT | 1 of 41 red |
| V15 | no token size limit | `LogoutTokenValidator.cs` | CAUGHT | 1 of 41 red |
| M1 | marked endpoint served on public port | `ManagementPortMiddleware.cs` | CAUGHT | 4 of 24 red |
| M2 | internal host accepted on public port | `ManagementPortMiddleware.cs` | CAUGHT | 2 of 24 red |
| M3 | any peer on the management port | `ManagementPortMiddleware.cs` | CAUGHT | 4 of 25 red |
| M4 | loopback (the container's own probe) refused | `ManagementPortMiddleware.cs` | CAUGHT | 3 of 25 red |
| C1 | CSRF exemption without the port check | `CsrfMiddleware.cs` | CAUGHT | 1 of 15 red |
| M5 | a missing peer address admitted | `ManagementPortMiddleware.cs` | CAUGHT | 1 of 25 red |
| I1 | no CSRF exemption on the management port | `CsrfMiddleware.cs` | CAUGHT | 23 of 33 red |
| I2 | endpoint not served on the management port | `ManagementPortMiddleware.cs` | CAUGHT | 27 of 33 red |
| I3 | endpoint served on the public port | `ManagementPortMiddleware.cs` | CAUGHT | 4 of 33 red |
| I4 | internal host on public port | `ManagementPortMiddleware.cs` | CAUGHT | 2 of 33 red |
| I5 | peer network unchecked | `ManagementPortMiddleware.cs` | CAUGHT | 1 of 33 red |
| I6 | X-Forwarded-For honoured on management port | `Program.cs` | CAUGHT | 1 of 33 red |
| I7 | endpoint not anonymous | `BackchannelLogoutEndpoint.cs` | CAUGHT | 23 of 33 red |
| I8 | no rate limit | `BackchannelLogoutEndpoint.cs` | CAUGHT | 1 of 33 red |
| I9 | route in the OpenAPI document | `BackchannelLogoutEndpoint.cs` | CAUGHT | 2 of 37 red |
| I10 | any content type | `BackchannelLogoutEndpoint.cs` | CAUGHT | 3 of 33 red |
| I11 | duplicate logout_token accepted | `BackchannelLogoutEndpoint.cs` | CAUGHT | 1 of 33 red |
| I12 | valid token deletes nothing | `BackchannelLogoutEndpoint.cs` | CAUGHT | 4 of 33 red |
| I13 | unknown sid is 400 | `BackchannelLogoutEndpoint.cs` | CAUGHT | 2 of 33 red |
| I14 | sid in the log | `BackchannelLogoutEndpoint.cs` | CAUGHT | 1 of 33 red |
| I15 | accepted token not counted | `BackchannelLogoutEndpoint.cs` | CAUGHT | 1 of 33 red |
| I16 | refusal body names the rule (oracle) | `BackchannelLogoutEndpoint.cs` | CAUGHT | 6 of 33 red |
| I17 | jti claim is not exclusive | `RedisLogoutTokenReplayCache.cs` | CAUGHT | 3 of 33 red |
| I18 | ticket not indexed | `RedisTicketStore.cs` | CAUGHT | 11 of 78 red |
| I19 | index not cleaned on removal | `RedisTicketStore.cs` | CAUGHT | 2 of 42 red |
| I20 | index left after back-channel delete | `RedisTicketStore.cs` | CAUGHT | 3 of 42 red |
| I21 | renewal resurrects a removed ticket | `RedisTicketStore.cs` | CAUGHT | 2 of 9 red |
| I22 | index without TTL | `RedisTicketStore.cs` | CAUGHT | 1 of 33 red |
| I23 | no body size bound (form limits remain) | `BackchannelLogoutEndpoint.cs` | CAUGHT | 1 of 33 red |
| I24 | host filtering opened to any host | `HostingRegistration.cs` | CAUGHT | 1 of 46 red |
| N1 | nginx `location ~* ^/bff/backchannel-logout` removed (web image rebuilt) | `web/nginx/default.conf` | CAUGHT | e2e `edge: ...` red: `Expected substring: "text/html"`, `Received string: "application/problem+json"` (the request reached the API, whose public port still answered 404) |
| E1 | Keycloak's client `backchannel.logout.url` pointed at a wrong path (kcadm, at runtime) | realm client | CAUGHT | both tests in `e2e/tests/backchannel-logout.spec.ts` red: `Expected: 401`, `Received: 200`; URL restored and both green again |

Notes:
- M4 first ran as "IPv4-mapped peer not normalised" and **survived**: on .NET 10, `IPAddress.IsLoopback` and
  `IPNetwork.Contains` already match `::ffff:a.b.c.d` (checked with a scratch program), so the normalisation was an
  equivalent mutant. It was deleted from the code; the unit tests keep `::ffff:` cases (including `::ffff:127.0.0.1`,
  the container's own probe) to pin the behaviour, and M4/M5 above were re-run on the simplified code.
- I23 first **survived**: the oversized-body test padded one field past the form's value limit, so the second layer
  caught it. The test now sends a valid token plus fields that each pass the form limits; only the declared-length bound
  refuses it, and the rerun is red.
- I1 first did not build (removing the condition left a constructor parameter unread, a warning as error); it was
  re-run as "the exemption compares against the public port" and is red.

### Review fixes (same branch, later commits)

After an independent review: the jti claim is released when deleting the tickets fails, `exp` is required, the
back-channel rate limit defaults to 6000 a minute, and start-up fails when `Ports:InternalHost` is set without
`Ports:InternalNetwork`. Same runner and method; V6 and I8 were re-run because their tests changed.

| Id | Mutation | File | Verdict | Filtered run |
|---|---|---|---|---|
| V6 | exp ignored | `LogoutTokenValidator.cs` | CAUGHT | 1 of 41 red |
| I8 | no rate limit | `BackchannelLogoutEndpoint.cs` | CAUGHT | 1 of 37 red |
| R1 | claim not released after a failed deletion | `BackchannelLogoutEndpoint.cs` | CAUGHT | 1 of 37 red |
| R2 | release not best effort (throws) | `RedisLogoutTokenReplayCache.cs` | CAUGHT | 1 of 37 red |
| X1 | exp optional again | `LogoutTokenValidator.cs` | CAUGHT | 1 of 41 red |
| S1 | configured back-channel limit ignored | `RateLimits.cs` | CAUGHT | 1 of 37 red |
| S2 | default limit back to 60 a minute | `RateLimits.cs` | CAUGHT | 1 of 37 red |
| K1 | internal host without network accepted | `ManagementSettingsValidator.cs` | CAUGHT | 3 of 8 red |
| K2 | settings validator not registered | `HostingRegistration.cs` | CAUGHT | 1 of 37 red |
| K3 | malformed network accepted | `ManagementSettingsValidator.cs` | CAUGHT | 2 of 8 red |

Notes:
- R2 first **survived**: the test took DEL away from the Redis user to make the release fail, but StackExchange.Redis 3
  deletes with UNLINK, so the release still worked. The test now denies both; the rerun above is red.
- R1 to R2 use the real Redis: the ACL user loses `eval`/`evalsha` (and `del`/`unlink` for R2) for one request, so the
  deletion fails while the claim and its release still reach Redis; the commands are given back in a `finally` block.
