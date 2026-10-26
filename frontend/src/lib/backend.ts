import "server-only";

/** The backend's origin as the BFF reaches it. Server-side only. */
export function backendUrl(): string {
  const url = process.env.BACKEND_URL;
  if (!url) {
    throw new Error("BACKEND_URL is not set");
  }
  return url.replace(/\/+$/, "");
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
  /** Redirects are returned to the browser, not followed (OIDC navigation). */
  redirect: "manual" | "follow";
};

/** Request headers sent upstream: no hop-by-hop, no browser credentials unless asked. */
export function upstreamRequestHeaders(
  incoming: Headers,
  publicUrl: URL,
  options: Pick<ForwardOptions, "accessToken" | "forwardCookies">,
): Headers {
  const headers = new Headers();
  incoming.forEach((value, name) => {
    const key = name.toLowerCase();
    if (HOP_BY_HOP.has(key) || key === "authorization") return;
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
  const publicUrl = new URL(request.url);
  const target = `${backendUrl()}${publicUrl.pathname}${publicUrl.search}`;
  const hasBody = request.method !== "GET" && request.method !== "HEAD";

  let upstream: Response;
  try {
    upstream = await fetch(target, {
      method: request.method,
      headers: upstreamRequestHeaders(request.headers, publicUrl, options),
      body: hasBody ? request.body : undefined,
      // Streams the body rather than buffering it (required by fetch for a stream body).
      ...(hasBody ? { duplex: "half" } : {}),
      redirect: options.redirect,
      cache: "no-store",
    } as RequestInit);
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
