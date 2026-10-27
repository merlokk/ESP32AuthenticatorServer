import { ApiError } from "./fetch";

/**
 * Refused or unreachable. The BFF answers 502 when the backend is down, and fetch throws on a
 * network failure: neither is something the user typed wrong.
 */
export function failureKind(error: unknown): "refused" | "unreachable" {
  return error instanceof ApiError && error.status < 500 ? "refused" : "unreachable";
}

/**
 * What to show for a failed call: the Problem Details title of a refusal (written for people), or
 * the fallback. Branching on a cause goes by `type`, never by this text.
 */
export function errorMessage(error: unknown, fallback: string): string {
  if (failureKind(error) === "unreachable") {
    return "The server could not be reached. Try again in a moment.";
  }
  return (error instanceof ApiError && error.problem?.title) || fallback;
}
