// Gravatar addresses an avatar by the SHA-256 of the trimmed, lower-cased e-mail; `d=identicon`
// draws a pattern for an address without one.

export async function gravatarUrl(email: string, size: number): Promise<string> {
  const bytes = new TextEncoder().encode(email.trim().toLowerCase());
  const digest = await crypto.subtle.digest("SHA-256", bytes);
  const hash = Array.from(new Uint8Array(digest), (byte) =>
    byte.toString(16).padStart(2, "0"),
  ).join("");
  return `https://www.gravatar.com/avatar/${hash}?s=${size}&d=identicon`;
}

/** Up to two letters for the fallback: "ada.lovelace" → "AL". */
export function initials(name: string): string {
  const parts = name.split(/[\s._-]+/).filter(Boolean);
  return (parts.length > 1 ? parts[0][0] + parts[1][0] : name.slice(0, 2)).toUpperCase();
}
