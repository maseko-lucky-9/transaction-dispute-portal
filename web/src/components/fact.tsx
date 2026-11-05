/** One label/value pair inside a `<dl>`. */
export function Fact({ term, value }: { term: string; value: string }) {
  return (
    <div className="flex flex-col gap-0.5">
      <dt className="text-sm text-muted-foreground">{term}</dt>
      <dd className="font-medium">{value}</dd>
    </div>
  );
}
