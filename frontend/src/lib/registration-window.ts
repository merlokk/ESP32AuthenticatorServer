// The account's registration window (use-cases.md UC-3): while it is open, a device may register
// to the account; a successful registration closes it.

export type WindowState = { open: false } | { open: true; until: Date; minutesLeft: number };

export function windowState(openUntil: string | null | undefined, now: Date): WindowState {
  if (!openUntil) return { open: false };
  const until = new Date(openUntil);
  const left = until.getTime() - now.getTime();
  if (!(left > 0)) return { open: false };
  return { open: true, until, minutesLeft: Math.ceil(left / 60_000) };
}

/** "45 min", "1 h 5 min", "23 h". */
export function formatMinutes(minutes: number): string {
  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  if (hours === 0) return `${rest} min`;
  return rest === 0 ? `${hours} h` : `${hours} h ${rest} min`;
}

const timeOnly = new Intl.DateTimeFormat("en", { timeStyle: "short" });
const dayAndTime = new Intl.DateTimeFormat("en", {
  month: "short",
  day: "numeric",
  hour: "numeric",
  minute: "2-digit",
});

/** "8:35 PM" today, "Oct 28, 7:35 PM" on another day: a 24-hour window ends tomorrow. */
export function formatUntil(until: Date, now: Date): string {
  return until.toDateString() === now.toDateString()
    ? timeOnly.format(until)
    : dayAndTime.format(until);
}
