import "server-only";

/** The backend's origin as the BFF reaches it. Server-side only. */
export function backendUrl(): string {
  const url = process.env.BACKEND_URL;
  if (!url) {
    throw new Error("BACKEND_URL is not set");
  }
  return url.replace(/\/+$/, "");
}

/**
 * The URL the browser asked for. Not `request.url`: the standalone server builds that from its own
 * HOSTNAME:PORT (http://0.0.0.0:3000 in the image). Behind ingress X-Forwarded-Host/Proto carry the
 * public origin (ingress overwrites them); without one, the browser's Host header does.
 */
export function publicUrl(request: Request): URL {
  const url = new URL(request.url);
  const first = (value: string | null) => value?.split(",")[0]?.trim() || undefined;
  const host = first(request.headers.get("x-forwarded-host")) ?? request.headers.get("host");
  const proto = first(request.headers.get("x-forwarded-proto"));
  const scheme = proto === "http" || proto === "https" ? proto : url.protocol.replace(":", "");
  // Built anew rather than by setting url.host: a host without a port keeps the old port there.
  return new URL(`${url.pathname}${url.search}`, `${scheme}://${host ?? url.host}`);
}

// Hop-by-hop headers (RFC 9110 §7.6.1) and those fetch sets itself: never forwarded.
const HOP_BY_HOP = new Set([
  "connection",
  "keep-alive",
  "proxy-authenticate",
  "proxy-authorization",
  "te",
  "trailer",
  "transfer-encoding",
  "upgrade",
  "host",
  "content-length",
]);

export type ForwardOptions = {
  /** Bearer token attached upstream; the browser never sees it. */
  accessToken?: string;
  /** Cookies cross in both directions (OIDC navigation); /api/** keeps them out. */
  forwardCookies: boolean;
  /**
   * The caller's own Authorization passes through: the device API, where a device presents the
   * token it got with its key. Never with `accessToken`: a session's token is not attached there.
   */
  forwardAuthorization?: boolean;
  /** Redirects are returned to the browser, not followed (OIDC navigation). */
  redirect: "manual" | "follow";
};

/** Request headers sent upstream: no hop-by-hop, no browser credentials unless asked. */
export function upstreamRequestHeaders(
  incoming: Headers,
  publicUrl: URL,
  options: Pick<ForwardOptions, "accessToken" | "forwardCookies" | "forwardAuthorization">,
): Headers {
  const headers = new Headers();
  incoming.forEach((value, name) => {
    const key = name.toLowerCase();
    if (HOP_BY_HOP.has(key)) return;
    if (key === "authorization" && !options.forwardAuthorization) return;
    if (key === "cookie" && !options.forwardCookies) return;
    headers.set(name, value);
  });
  if (options.accessToken) {
    headers.set("authorization", `Bearer ${options.accessToken}`);
  }
  // The backend builds public URLs (OIDC discovery, redirects) from these.
  headers.set("x-forwarded-host", publicUrl.host);
  headers.set("x-forwarded-proto", publicUrl.protocol.replace(":", ""));
  return headers;
}

/** Response headers returned to the browser: no hop-by-hop, Set-Cookie only if asked. */
export function downstreamResponseHeaders(
  upstream: Headers,
  options: Pick<ForwardOptions, "forwardCookies">,
): Headers {
  const headers = new Headers();
  upstream.forEach((value, name) => {
    const key = name.toLowerCase();
    if (HOP_BY_HOP.has(key) || key === "content-encoding" || key === "set-cookie") return;
    headers.set(name, value);
  });
  if (options.forwardCookies) {
    for (const cookie of upstream.getSetCookie()) {
      headers.append("set-cookie", cookie);
    }
  }
  return headers;
}

/**
 * Forwards a request to the same path on the backend: path, query, method and body untouched.
 * The BFF renames nothing and decides nothing (architecture.md §9).
 */
export async function forward(request: Request, options: ForwardOptions): Promise<Response> {
  const url = publicUrl(request);
  const target = `${backendUrl()}${url.pathname}${url.search}`;
  const hasBody = request.method !== "GET" && request.method !== "HEAD";

  // A Blob, not the request's stream: on a 401 (a missing or expired token) Node's fetch re-reads
  // the body (fetch spec, 401 handling) and fails on a stream or a transferred buffer, so the caller
  // saw a 502 instead of the 401. Bodies are bounded: JSON, a store file (Sync:MaxFileBytes), a
  // firmware image at most (Firmware:MaxImageBytes, ~8 MB).
  const body = hasBody ? await request.blob() : undefined;

  let upstream: Response;
  try {
    upstream = await fetch(target, {
      method: request.method,
      headers: upstreamRequestHeaders(request.headers, url, options),
      body: body && body.size > 0 ? body : undefined,
      redirect: options.redirect,
      cache: "no-store",
    });
  } catch {
    return Response.json(
      {
        type: "about:blank",
        title: "Backend unavailable",
        status: 502,
      },
      { status: 502, headers: { "content-type": "application/problem+json" } },
    );
  }

  return new Response(upstream.body, {
    status: upstream.status,
    statusText: upstream.statusText,
    headers: downstreamResponseHeaders(upstream.headers, options),
  });
}
