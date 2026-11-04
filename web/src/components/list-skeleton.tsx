import { Skeleton } from "@/components/ui/skeleton";

/**
 * `announce` makes it its own live region (the route's pending state, where nothing else
 * exists yet). Inside the page the persistent result summary announces loading instead, so
 * a second region would be read twice.
 */
export function ListSkeleton({ announce = true }: { announce?: boolean }) {
  return (
    <div
      {...(announce ? { role: "status", "aria-busy": true } : { "aria-hidden": true })}
      className="flex flex-col gap-3"
    >
      {announce ? <span className="sr-only">Loading transactions...</span> : null}
      {Array.from({ length: 6 }, (_, index) => (
        <Skeleton key={index} className="h-12 w-full" />
      ))}
    </div>
  );
}
