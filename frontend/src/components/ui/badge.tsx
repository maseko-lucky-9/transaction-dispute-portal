import type * as React from "react";
import { cva, type VariantProps } from "class-variance-authority";

import { cn } from "@/lib/utils";

const badgeVariants = cva(
  "inline-flex w-fit shrink-0 items-center justify-center gap-1 rounded-md border px-2 py-0.5 text-xs font-medium whitespace-nowrap",
  {
    variants: {
      variant: {
        default: "border-transparent bg-primary text-primary-foreground",
        secondary: "border-transparent bg-secondary text-secondary-foreground",
        destructive: "border-transparent bg-destructive text-destructive-foreground",
        outline: "text-foreground",
        // Status pills: tinted background, coloured text, always shown with the status word.
        submitted:
          "rounded-full border-transparent bg-status-submitted-bg font-semibold text-status-submitted",
        review:
          "rounded-full border-transparent bg-status-review-bg font-semibold text-status-review",
        accepted:
          "rounded-full border-transparent bg-status-accepted-bg font-semibold text-status-accepted",
        rejected:
          "rounded-full border-transparent bg-status-rejected-bg font-semibold text-status-rejected",
        withdrawn:
          "rounded-full border-transparent bg-status-withdrawn-bg font-semibold text-status-withdrawn",
      },
    },
    defaultVariants: {
      variant: "default",
    },
  },
);

function Badge({
  className,
  variant,
  ...props
}: React.ComponentProps<"span"> & VariantProps<typeof badgeVariants>) {
  return (
    <span data-slot="badge" className={cn(badgeVariants({ variant }), className)} {...props} />
  );
}

export { Badge, badgeVariants };
