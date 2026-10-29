import "server-only";
import { encode, getToken, type JWT } from "next-auth/jwt";
import { backendUrl, forward, publicUrl, type ForwardOptions } from "@/lib/backend";

// The BFF's half of token renewal (architecture.md §9). The session cookie holds the backend's
// access and refresh tokens; an access token that has expired, or that the backend refuses before
// then (its signing key is per process: a restart refuses every earlier token), is renewed with
// the refresh token and the cookie rewritten on the same response. Refresh tokens are not rolling,
// so concurrent renewals of one session all succeed; the last cookie written wins.

/** Auth.js's default session lifetime: the cookie and its JWE live this long. */
const SESSION_MAX_AGE = 30 * 24 * 60 * 60;

/** Renew this long before the access token runs out, so it does not expire in flight. */
const RENEW_MARGIN_SECONDS = 30;

/** Auth.js's chunk size (4096 less an empty cookie's ~160 bytes); getToken joins the chunks. */
const CHUNK_SIZE = 4096 - 160;

const nowSeconds = () => Math.floor(Date.now() / 1000);

/**
 * One JSON line on stdout per renewal (kubectl logs deploy/frontend): what was renewed and why,
 * and what came of it. Never a token: the user id (sub), the path, statuses and cookie sizes.
 */
function logRenewal(level: "info" | "warn", fields: Record<string, unknown>): void {
  console[level](JSON.stringify({ event: "bff.session-renewal", ...fields }));
}

function isSecure(request: Request): boolean {
  return publicUrl(request).protocol === "https:";
}

/** Auth.js's session cookie name: `__Secure-` over https. */
function cookieName(request: Request): string {
  return `${isSecure(request) ? "__Secure-" : ""}authjs.session-token`;
}

/** The session in the request's cookie, or null. */
export async function readSession(request: Request): Promise<JWT | null> {
  return getToken({ req: request, secret: process.env.AUTH_SECRET, secureCookie: isSecure(request) });
}

function isFresh(session: JWT): boolean {
  return Boolean(session.accessToken) && (!session.expiresAt || nowSeconds() < session.expiresAt - RENEW_MARGIN_SECONDS);
}

/**
 * The session with a new access token from the refresh token, or undefined when the backend
 * refuses it (expired, revoked at sign-out, the account blocked or deleted) or does not answer.
 */
export async function renewSession(session: JWT): Promise<JWT | undefined> {
  if (!session.refreshToken) return undefined;

  let response: Response;
  try {
    response = await fetch(`${backendUrl()}/connect/token`, {
      method: "POST",
      headers: { "content-type": "application/x-www-form-urlencoded" },
      body: new URLSearchParams({
        grant_type: "refresh_token",
        client_id: process.env.AUTH_AUTHSERVER_ID ?? "",
        refresh_token: session.refreshToken,
      }),
      cache: "no-store",
    });
  } catch {
    logRenewal("warn", { sub: session.sub, outcome: "backend-unreachable" });
    return undefined;
  }
  if (!response.ok) {
    logRenewal("warn", { sub: session.sub, outcome: "refused", tokenStatus: response.status });
    return undefined;
  }

  const answer = (await response.json()) as {
    access_token?: string;
    expires_in?: number;
    refresh_token?: string;
    id_token?: string;
  };
  if (!answer.access_token) return undefined;

  return {
    ...session,
    accessToken: answer.access_token,
    // Not rolling: the answer usually has no new refresh token, and the old one stays good.
    refreshToken: answer.refresh_token ?? session.refreshToken,
    idToken: answer.id_token ?? session.idToken,
    expiresAt: answer.expires_in ? nowSeconds() + answer.expires_in : undefined,
  };
}

/**
 * Set-Cookie values that store `session` as Auth.js would: one cookie, or `.0`, `.1` ... chunks,
 * and any cookie of the request's old session the new one does not overwrite expired.
 */
export async function renewedSessionCookies(request: Request, session: JWT): Promise<string[]> {
  const name = cookieName(request);
  const value = await encode({ token: session, secret: process.env.AUTH_SECRET ?? "", salt: name, maxAge: SESSION_MAX_AGE });
  const attributes = `Path=/; HttpOnly; SameSite=Lax${isSecure(request) ? "; Secure" : ""}`;

  const chunks =
    value.length <= CHUNK_SIZE
      ? [{ name, value }]
      : Array.from({ length: Math.ceil(value.length / CHUNK_SIZE) }, (_, index) => ({
          name: `${name}.${index}`,
          value: value.slice(index * CHUNK_SIZE, (index + 1) * CHUNK_SIZE),
        }));
  const written = new Set(chunks.map((chunk) => chunk.name));

  const stale = (request.headers.get("cookie") ?? "")
    .split(";")
    .map((pair) => pair.trim().split("=")[0])
    .filter((old) => old.startsWith(name) && !written.has(old));

  return [
    ...chunks.map((chunk) => `${chunk.name}=${chunk.value}; ${attributes}; Max-Age=${SESSION_MAX_AGE}`),
    ...stale.map((old) => `${old}=; ${attributes}; Max-Age=0`),
  ];
}

/**
 * {@link forward} with the session's access token attached: renewed first when it has expired,
 * and once more, the request sent again, when the backend refuses it with 401. A renewal rewrites
 * the session cookie on the response. A 401 that survives a renewal is the client's to handle
 * (sign in again).
 */
export async function forwardWithSession(
  request: Request,
  options: Omit<ForwardOptions, "accessToken">,
): Promise<Response> {
  const session = await readSession(request);
  // Kept for a second attempt: forward reads the body.
  const again = session?.refreshToken ? request.clone() : undefined;

  let reason: "expired" | "refused" | undefined = session && !isFresh(session) ? "expired" : undefined;
  let renewed = session && reason ? await renewSession(session) : undefined;
  const token = renewed?.accessToken ?? (session && isFresh(session) ? session.accessToken : undefined);

  let response = await forward(request, { ...options, accessToken: token });

  if (response.status === 401 && session && again && !renewed && token) {
    reason = "refused";
    renewed = await renewSession(session);
    if (renewed) {
      await response.body?.cancel();
      response = await forward(again, { ...options, accessToken: renewed.accessToken });
    }
  }

  const cookies = renewed ? await renewedSessionCookies(request, renewed) : [];
  for (const cookie of cookies) {
    response.headers.append("set-cookie", cookie);
  }

  if (reason && renewed) {
    // A 401 here: the backend refuses the token it has just issued, and the client signs in again.
    logRenewal(response.status === 401 ? "warn" : "info", {
      sub: session?.sub,
      reason,
      method: request.method,
      path: publicUrl(request).pathname,
      status: response.status,
      // A browser drops a cookie over ~4 KB; Auth.js chunks below that.
      cookies: cookies.map((cookie) => cookie.split(";")[0].length),
    });
  }
  return response;
}
