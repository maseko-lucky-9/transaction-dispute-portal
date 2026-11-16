# UI audit — frontend against `.impeccable.md`

Source-level audit of `frontend/src/` against the design context recorded in
[`.impeccable.md`](../../.impeccable.md). Nothing was run in a browser — see **Not verified**.

Ranked most severe first.

**Status.** Both P1s are fixed in the change that adds this file.
Reject is `outline` and Accept stays crimson (the first option under "One question for you" below —
a default, not a decision; flip it in `lib/transitions.ts` if you prefer neutral). The Cancel button
beside "Yes, reject" became `ghost` so the outlined confirm still outranks it. Inline links use the
new `LINK` constant. Checked in a real browser, light and dark. The P2s and P3s are untouched.

**New finding, now fixed.** The agent queue (`agent-queue-page.tsx:110`) and dispute pages
(`agent-dispute-page.tsx:43`) request `/api/v1/dispute-reasons`, and the API used to answer an agent
with `403`, logged to the console on every load. The route is now open to either role
(`Policies.PortalUser`), and the agent e2e test asserts that no `/api/` response on the agent pages is
an error. The mocked tests answer that route with `200` (`test/agent.ts:176`), so they could never see
it.

## Score against the recorded principles

| # | Principle | Verdict |
|---|---|---|
| 1 | The brand is settled; the craft is in the detail | **Pass.** No component reopens the palette, the radii or the card treatment. |
| 2 | One crimson action per page | **Fail.** The agent dispute page ships two (P1). |
| 3 | Status is the spine of the product | **Pass.** Always a pill with the word in it, icon decorative, every pair contrast-tested. |
| 4 | Never invent a number | **Pass.** No counts on filters, no totals on Home, and `formatMoney` refuses to round or guess (`lib/format.ts:46`). |
| 5 | Both themes, or it is not done | **Pass, with a hole.** Every `light-dark()` token pair is asserted in both themes; the Royal Blue panel's white text sits outside that net (P2), and no composed contrast has been checked in a browser at all. |
| 6 | Every surface has four states | **Pass.** All three paged lists route through `PagedResults`. Empty-state styling is inconsistent between them (P3). |
| 7 | Customer first on shared components | **Unverified.** Nothing contradicts it in the source, but the customer's stated primary context is a phone and the tables carry `min-w-[44rem]`, which was not tested at phone width. |

Separately, **fidelity to direction A is partial**: its defining link colour is implemented nowhere
(P1).


## P1 — Reject is styled identically to Accept, and both are crimson

`frontend/src/components/agent-dispute-page.tsx:294` renders every available transition as a bare
`<Button>`, so each takes the default crimson variant:

```tsx
{options.map((option) => (
  <Button key={option.to} type="button" ...>{option.label}</Button>
))}
```

`options` is `NEXT_TRANSITIONS[status]`, and `frontend/src/lib/transitions.ts:47` defines
`UnderReview: [ACCEPT, REJECT]`. So on any dispute under review the agent is shown **two identical
crimson buttons** for a decision that cannot be undone (`confirm: true`, `transitions.ts:30` and
`:37`) — one upholds the customer's claim, the other denies it.

- Breaks design principle 2, one crimson action per page.
- `withdraw-dispute.tsx:182` already uses `destructive` correctly for the customer's own
  irreversible action — but note that `destructive` is **not** the fix here: `--destructive` is
  `#b4121a` against `--primary` `#e51718` in light mode (`index.css:66`, `:75`), so Accept and
  Reject would still be two reds 8px apart. `outline` is the variant that actually reads as
  different.
- The labels do distinguish them ("Accept"/"Reject", then "Yes, reject"); colour, size and shape do
  not, on either screen. The confirmation at `agent-dispute-page.tsx:279` is crimson as well.

**Failure scenario.** An agent working the queue at speed on a dispute in `UnderReview` reaches for
Accept and hits Reject, because the two targets are the same size, the same shape and the same
colour, sitting 8px apart. The customer is told their claim was denied and the state is terminal
(`transitions.ts:49`) — no agent transition exists back out of `Rejected`.

**Fix.** Put the variant on the option rather than on the page: add a `variant` field to
`TransitionOption` and pass `option.variant` in both the choice row and the confirmation, so the
two screens cannot drift apart.

**One question for you before implementing it.** Reject should be `outline`. Whether *Accept*
keeps crimson is a product decision, not a styling one:

- `ACCEPT` crimson, `REJECT` outline — one crimson action per page, as principle 2 asks, but the
  interface then nudges the agent toward upholding the dispute.
- both `outline`, crimson used nowhere — a neutral adjudication, at the cost of the page having no
  primary action at all.

The second reads better for an impartial reviewer; the first is what the recorded principle says.
Pick one and the change is three lines in `lib/transitions.ts`.

## P1 — `--brand-gem` never reaches a pixel; direction A has no link colour

Direction A is specified as "Crimson for the one main action, **Blue Gem for links**"
(`docs/design/brand-mockups/index.html`). The token is defined (`index.css`, `--brand-gem`) and is
contrast-tested — `["brand-gem", "card"]` is in `PAIRS` at `frontend/src/lib/contrast.test.ts:44` —
but the only thing that references it is a Button variant nothing uses:

```
frontend/src/components/ui/button.tsx:19:  link: "text-brand-gem underline-offset-4 hover:underline",
```

Nothing uses it. `grep -rn 'brand-gem\|variant: *"link"\|variant="link"' frontend/src` returns
exactly three lines — the two token definitions in `index.css` and that one variant — and this
covers both call forms, since the codebase does use the object form elsewhere
(`buttonVariants({ variant: "outline" })` in `not-found.tsx:10`, `wrong-area.tsx:18`, `:22`,
`disputes-table.tsx:63`, `agent-queue-page.tsx:212`). Every inline text link instead hardcodes an
uncoloured underline and inherits `foreground`:

| File | Line |
|---|---|
| `components/disputes-page.tsx` | 126 |
| `components/dispute-detail-page.tsx` | 138 |
| `components/dispute-form.tsx` | 43 (`const LINK`) |
| `components/transactions-table.tsx` | 137 |
| `components/agent-dispute-page.tsx` | 54 |
| `components/dispute-form-page.tsx` | 110, 126 |

So the colour that distinguishes direction A from the four rejected directions is tested, documented
and absent from the running app.

**Fix.** Export one `LINK` constant from `lib/table-styles.ts` carrying `text-brand-gem underline
underline-offset-4`, and replace the seven literals. Keep `BACK_LINK` muted as it is.

## P2 — the Royal Blue panel is the one text pair no test covers

`frontend/src/components/sign-in.tsx:58` puts `text-white` directly on `bg-brand-royal`, and the
4.48:1 ratio lives in a comment at `:64` rather than in an assertion. Because `white` is not a
token, the pair is invisible to `contrast.test.ts`, which only walks `light-dark()` pairs it finds
in `index.css`.

The copy is currently compliant — `sign-in.tsx:65` is `text-[26px] font-bold`, above the 24px large
text threshold — but nothing fails if it is later reduced, and both documents warn about exactly this
(`brand-mockups/index.html`: Royal Blue is for fills and large text only; `index.css`: a fill only,
white on it legal at >= 24px or >= 19px bold). The same panel also carries `border-white/10`
at `:62`, decorative only.

**Fix.** Add `--royal-foreground: light-dark(#ffffff, #ffffff)`, use it at `:58`, and add a
large-text pair to `contrast.test.ts` asserting `>= 3:1` so the threshold is enforced rather than
remembered.

## P2 — no type scale: 8 one-off sizes, and measured drift from the approved mockup

`grep -rho 'text-\[[^]]*\]' frontend/src` yields 8 distinct arbitrary values across three files
(`sign-in.tsx` 13/15/17/26px and a clamp, `home-page.tsx` 22px and a clamp, `table-styles.ts` a
clamp). Every component restates its own size, so there is no scale to be consistent with — and the
restatements have already drifted from direction A's own stylesheet:

| Element | Mockup | Built | Reading |
|---|---|---|---|
| Royal panel headline | `22px/650` — `dir.css:44` | `26px/700` — `sign-in.tsx:65` | **Build is right, mockup is wrong.** White on Royal Blue is 4.48:1, which is only legal as large text: >= 24px, or >= 19px bold. 22px at weight 650 is neither. Keep 26px. |
| Fact label (`dt`) | `12.5px` — `dir.css:66` | `text-xs` = 12px — `fact.tsx:5` | Deliberate snap to Tailwind's scale. Fine. |
| Table column header | `12.5px` — `shared/base.css:82` | `text-xs` = 12px — `table-styles.ts:2` | Same, and consistent with the row above it. |
| Sign-in lede | `clamp(30px, 4.4vw, 46px)` — `dir.css:39` | `clamp(30px,4.4vw,44px)` — `sign-in.tsx:36` | 2px at the top end, no reason recorded either way. |
| Lifecycle panel h2 | `18px` — `dir.css:23` | `text-base` = 16px — `home-page.tsx:69` | 2px, no reason recorded. |

So the mockup is **not** the source of truth — one of its values would fail the contrast rule the
project enforces, and two of the differences are sensible snapping. The finding is not the drift,
it is that **there is no shared scale to drift from**: each component restates a size, so there is
no single place where "16px, not 18px" is either decided or visible, and no way to tell a contrast
fix from a typo.

**Fix.** Declare `--text-*` steps in `@theme` and replace the arbitrary values with them. Build the
scale from the sizes actually in use, keeping 26px for the royal panel and resolving the two 2px
gaps explicitly. This is also the slot the agreed self-hosted display face drops into — see
**Open decisions** in `.impeccable.md`.

## P3 — empty states use two different surfaces

Same role, two treatments:

- `transactions-page.tsx:96` and `disputes-page.tsx:111`: `rounded-xl bg-accent p-8 text-center`
- `agent-queue-page.tsx:124`: `rounded-xl border p-8 text-center`

**Fix.** One `EMPTY_STATE` constant beside `CARD` in `lib/table-styles.ts`; pick `bg-accent`, since
two of the three already use it.

## P3 — noted, not a defect: the sign-in `h1` is the 15px eyebrow

`sign-in.tsx:33` makes "Sign in" the `h1` at 15px uppercase, while the 44px headline at `:36` is a
`<p>`. This is the faithful implementation of two locked constraints at once — the brand-mockups
rule that the signed-out page's main heading reads "Sign in", and `dir.css:38-39`, which styles
`.signin-card h1` at exactly 15px/700/0.08em uppercase. Recorded here so a later pass does not
"fix" it.

## Checked and correct

- **Money alignment.** `tabular-nums` is on the amount cell (`lib/table-styles.ts:8`) and on the
  sample amount in the landing panel (`sign-in.tsx:71`).
- **Four states on every list.** All three paged lists go through `PagedResults` — transactions
  (`transactions-page.tsx:85`), disputes (`disputes-page.tsx`) and the agent queue
  (`agent-queue-page.tsx:113`) — which gives each one loading, empty, first-load error and
  next-page error, and keeps keyboard focus across all four (`paged-results.tsx:126-184`).
- **Status never relies on colour.** `status-pill.tsx:17` renders the word as one text node with a
  decorative icon; credits carry a sign, a colour *and* the word "Credit"
  (`transactions-table.tsx:80-92`).
- **White on crimson is 4.70:1**, passing AA for normal text, and the trap is already documented:
  `ui/button.tsx:12` records that `bg-primary/90` drops it below 4.5:1 mid-transition.
- **The three numbered feature cards are approved design, not a generic template.**
  `dir.css:48-49` specifies `.feature h2 { font-size: 17px }` and a 36px/10px-radius `.mark` tile —
  `sign-in.tsx:83-97` implements it, `size-9` being exactly 36px.
- **UX writing.** Every failure in `dispute-form.tsx:258-327` names what happened and what to do
  next, and the idempotency guarantee is stated in the customer's words ("Trying again will not
  create a second dispute", `:323`).

## Not verified

No browser was involved in this audit. Still outstanding, and only reachable through
`docker compose up --build` plus `playwright-cli` in both themes:

- Composed and translucent contrast: hover tints (`hover:bg-accent`), `ring-ring/50` focus rings,
  `bg-muted/50`, and every transition midpoint.
- The Royal Blue panel in dark mode, where `bg-card` (#1c1c22) sits on #2f70ef.
- Real spacing rhythm and the 960px/860px/760px/700px layout breaks.
- Whether the `min-w-[44rem]` and `min-w-[48rem]` tables scroll acceptably on a phone, which is the
  customer's stated primary context.
