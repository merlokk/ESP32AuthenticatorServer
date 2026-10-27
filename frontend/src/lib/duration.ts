/** Seconds as people read them: "45 s", "10 min", "8 h", "1 day", "1 h 30 min". */
export function formatDuration(seconds: number): string {
  if (seconds < 60) return `${seconds} s`;
  const units: [number, string][] = [
    [86_400, "day"],
    [3_600, "h"],
    [60, "min"],
  ];
  const parts: string[] = [];
  let rest = Math.floor(seconds);
  for (const [size, name] of units) {
    const count = Math.floor(rest / size);
    rest -= count * size;
    if (count > 0)
      parts.push(name === "day" ? `${count} ${count === 1 ? "day" : "days"}` : `${count} ${name}`);
  }
  return parts.join(" ");
}
