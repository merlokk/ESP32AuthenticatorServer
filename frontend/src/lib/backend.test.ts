import { describe, expect, it } from "vitest";
import { downstreamResponseHeaders, upstreamRequestHeaders } from "./backend";

const publicUrl = new URL("https://auth.example.com/api/devices?page=2");

describe("upstreamRequestHeaders", () => {
  const incoming = new Headers({
    accept: "application/json",
    authorization: "Bearer forged-by-the-browser",
    cookie: "authjs.session-token=secret",
    connection: "keep-alive",
    host: "auth.example.com",
  });

  it("attaches the session's token and drops the browser's", () => {
    const headers = upstreamRequestHeaders(incoming, publicUrl, {
      accessToken: "from-session",
      forwardCookies: false,
    });
    expect(headers.get("authorization")).toBe("Bearer from-session");
  });

  it("never forwards a browser Authorization header", () => {
    const headers = upstreamRequestHeaders(incoming, publicUrl, { forwardCookies: false });
    expect(headers.has("authorization")).toBe(false);
  });

  it("keeps cookies out of /api/** and in for OIDC navigation", () => {
    expect(
      upstreamRequestHeaders(incoming, publicUrl, { forwardCookies: false }).has("cookie"),
    ).toBe(false);
    expect(
      upstreamRequestHeaders(incoming, publicUrl, { forwardCookies: true }).get("cookie"),
    ).toBe("authjs.session-token=secret");
  });

  it("drops hop-by-hop headers and states the public origin", () => {
    const headers = upstreamRequestHeaders(incoming, publicUrl, { forwardCookies: false });
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
