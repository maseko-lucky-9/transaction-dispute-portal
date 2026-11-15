/** Time-of-day greeting: morning before 12:00, afternoon before 18:00, evening otherwise. */
export function greetingFor(hour: number): string {
  if (hour < 12) return "Good morning";
  if (hour < 18) return "Good afternoon";
  return "Good evening";
}

/** The first word of a display name; the whole (trimmed) name when it has no words to split. */
export function firstNameOf(name: string): string {
  const trimmed = name.trim();
  return trimmed.split(/\s+/)[0] ?? trimmed;
}
