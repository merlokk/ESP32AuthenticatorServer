import { describe, expect, it } from "vitest";
import { downstreamResponseHeaders, publicUrl, upstreamRequestHeaders } from "./backend";

describe("publicUrl", () => {
  // What the standalone server hands a route handler: its own bind address.
  const bound = "http://0.0.0.0:3000/api/devices?page=2";

  it("takes the browser's Host, not the bind address", () => {
    const url = publicUrl(new Request(bound, { headers: { host: "localhost:3100" } }));
    expect(url.href).toBe("http://localhost:3100/api/devices?page=2");
  });

  it("prefers the ingress's X-Forwarded-Host and -Proto", () => {
    const url = publicUrl(
      new Request(bound, {
        headers: {
          host: "frontend:3000",
          "x-forwarded-host": "auth.example.com, proxy.internal",
          "x-forwarded-proto": "https",
        },
      }),
    );
    expect(url.href).toBe("https://auth.example.com/api/devices?page=2");
  });

  it("ignores a scheme that is not http or https", () => {
    const url = publicUrl(
      new Request(bound, { headers: { host: "localhost:3100", "x-forwarded-proto": "ftp" } }),
    );
    expect(url.protocol).toBe("http:");
  });
});

const pageUrl = new URL("https://auth.example.com/api/devices?page=2");

describe("upstreamRequestHeaders", () => {
  const incoming = new Headers({
    accept: "application/json",
    authorization: "Bearer forged-by-the-browser",
    cookie: "authjs.session-token=secret",
    connection: "keep-alive",
    host: "auth.example.com",
  });

  it("attaches the session's token and drops the browser's", () => {
    const headers = upstreamRequestHeaders(incoming, pageUrl, {
      accessToken: "from-session",
      forwardCookies: false,
    });
    expect(headers.get("authorization")).toBe("Bearer from-session");
  });

  it("never forwards a browser Authorization header", () => {
    const headers = upstreamRequestHeaders(incoming, pageUrl, { forwardCookies: false });
    expect(headers.has("authorization")).toBe(false);
  });

  it("passes a device's own token through on the device API, and nothing of a session", () => {
    const headers = upstreamRequestHeaders(incoming, pageUrl, {
      forwardCookies: false,
      forwardAuthorization: true,
    });
    expect(headers.get("authorization")).toBe("Bearer forged-by-the-browser");
    expect(headers.has("cookie")).toBe(false);
  });

  it("keeps cookies out of /api/** and in for OIDC navigation", () => {
    expect(upstreamRequestHeaders(incoming, pageUrl, { forwardCookies: false }).has("cookie")).toBe(
      false,
    );
    expect(upstreamRequestHeaders(incoming, pageUrl, { forwardCookies: true }).get("cookie")).toBe(
      "authjs.session-token=secret",
    );
  });

  it("drops hop-by-hop headers and states the public origin", () => {
    const headers = upstreamRequestHeaders(incoming, pageUrl, { forwardCookies: false });
    expect(headers.has("connection")).toBe(false);
    expect(headers.has("host")).toBe(false);
    expect(headers.get("accept")).toBe("application/json");
    expect(headers.get("x-forwarded-host")).toBe("auth.example.com");
    expect(headers.get("x-forwarded-proto")).toBe("https");
  });
});

describe("downstreamResponseHeaders", () => {
  const upstream = new Headers([
    ["content-type", "application/json"],
    ["transfer-encoding", "chunked"],
    ["content-encoding", "gzip"],
    ["set-cookie", "a=1; HttpOnly"],
    ["set-cookie", "b=2; HttpOnly"],
  ]);

  it("passes Set-Cookie only for OIDC navigation, every one of them", () => {
    expect(downstreamResponseHeaders(upstream, { forwardCookies: false }).has("set-cookie")).toBe(
      false,
    );
    expect(downstreamResponseHeaders(upstream, { forwardCookies: true }).getSetCookie()).toEqual([
      "a=1; HttpOnly",
      "b=2; HttpOnly",
    ]);
  });

  it("drops hop-by-hop and encoding headers (fetch has already decoded the body)", () => {
    const headers = downstreamResponseHeaders(upstream, { forwardCookies: false });
    expect(headers.has("transfer-encoding")).toBe(false);
    expect(headers.has("content-encoding")).toBe(false);
    expect(headers.get("content-type")).toBe("application/json");
  });
});
