import { z } from "zod";

// The backend's rules (AccountFields), checked here first so a typo needs no round trip.
export const USER_NAME = z
  .string()
  .trim()
  .regex(/^[A-Za-z0-9._-]{1,64}$/, "1 to 64 letters, digits, '.', '_' or '-'.");

export const EMAIL = z.string().trim().pipe(z.email("Enter an e-mail address."));

/** The local part of an address in the user-name alphabet: "Ada.L+x@example.com" → "Ada.L-x". */
export function userNameFor(email: string): string {
  const local = email.trim().split("@")[0] ?? "";
  return local
    .replace(/[^A-Za-z0-9._-]/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, 64);
}
