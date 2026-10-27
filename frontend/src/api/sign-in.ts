import { ApiError, customFetch } from "./fetch";

// The web sign-in calls (architecture.md §4.7), by hand until the Orval client is generated. They
// go through the BFF's cookie-forwarding route (src/app/api/identity/sign-in): the credential is
// the authorization server's Set-Cookie, not the body.

function post<T>(path: string, body?: unknown): Promise<T> {
  return customFetch<T>(path, {
    method: "POST",
    headers: body === undefined ? undefined : { "content-type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
}

export function signInWithTotp(request: { email: string; code: string }) {
  return post<{ signedIn: boolean }>("/api/identity/sign-in/totp", request);
}

export function beginPasskeySignIn() {
  return post<{ requestOptions: string }>("/api/identity/sign-in/passkey/options");
}

export function completePasskeySignIn(request: { credentialJson: string }) {
  return post<{ signedIn: boolean }>("/api/identity/sign-in/passkey", request);
}

/**
 * Refused or unreachable. Every refusal is the same 401 (`sign-in.refused`): the body is not read.
 * The BFF answers 502 when the backend is down, and fetch throws on a network failure.
 */
export function signInFailure(error: unknown): "refused" | "unreachable" {
  return error instanceof ApiError && error.status < 500 ? "refused" : "unreachable";
}
