/** Column header cell: small, muted, no fill (the card behind it is the surface). */
export const COLUMN_HEADER = "px-4 py-3 text-left text-xs font-semibold text-muted-foreground";

/** Body row: hairline separators come from the tbody's `divide-y`; the hover tint is here. */
export const TABLE_ROW = "align-top hover:bg-accent";

/** A money cell: right-aligned, tabular digits so columns of amounts line up. */
export const AMOUNT_CELL = "px-4 py-3 text-right whitespace-nowrap tabular-nums";

/** A white rounded card with soft shadow; the page's content sits on the grey canvas around it. */
export const CARD = "flex min-w-0 flex-col gap-4 rounded-card bg-card p-5 shadow-card";

/** The page's h1: large, semibold, tight tracking. Muted subtext goes under it. */
export const PAGE_TITLE =
  "text-[clamp(1.75rem,4vw,2.5rem)] leading-tight font-semibold tracking-tight";

/** A status filter link: a muted pill, with the current one outlined (links, not tabs). */
export const FILTER_LINK =
  "rounded-full px-3.5 py-1.5 text-sm font-semibold text-muted-foreground outline-none hover:bg-accent hover:text-foreground focus-visible:ring-[3px] focus-visible:ring-ring/50 aria-[current=page]:bg-card aria-[current=page]:text-foreground aria-[current=page]:ring-1 aria-[current=page]:ring-border";

/** An inline text link: Blue Gem (direction A's link colour, lightened in dark mode) and underlined. */
export const LINK = "text-brand-gem underline underline-offset-4";

/** The "Back to ..." link above a detail page's title. */
export const BACK_LINK =
  "text-sm font-semibold text-muted-foreground underline-offset-4 outline-none hover:underline focus-visible:underline";
