// @vitest-environment node
import { createServer, type Server } from "node:http";
import type { AddressInfo } from "node:net";
import { encode, getToken, type JWT } from "next-auth/jwt";
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { forwardWithSession, renewedSessionCookies } from "./session";

// A backend with OpenIddict's token endpoint (refresh grant) and one API call that takes the
// tokens it knows: "fresh" (issued before the call) and "renewed-<n>"; "rotated" is a token signed
// by a key the backend no longer has (it restarted), refused although not expired.
const SECRET = "a-test-secret-of-enough-length-for-auth-js";
const COOKIE = "authjs.session-token";
let server: Server;
let renewals: URLSearchParams[] = [];

beforeAll(async () => {
  server = createServer((request, response) => {
    const chunks: Buffer[] = [];
    request.on("data", (chunk: Buffer) => chunks.push(chunk));
    request.on("end", () => {
      const body = Buffer.concat(chunks);
      if (request.url === "/connect/token") {
        const form = new URLSearchParams(body.toString());
        renewals.push(form);
        if (form.get("grant_type") === "refresh_token" && form.get("refresh_token") === "good") {
          response.writeHead(200, { "content-type": "application/json" }).end(
            JSON.stringify({
              access_token: `renewed-${renewals.length}`,
              token_type: "Bearer",
              expires_in: 600,
            }),
          );
        } else {
          response.writeHead(400, { "content-type": "application/json" }).end('{"error":"invalid_grant"}');
        }
        return;
      }
      const token = request.headers.authorization?.replace(/^Bearer /, "");
      if (token === "fresh" || token?.startsWith("renewed-")) {
        response.writeHead(200, { "x-token": token }).end(body);
        return;
      }
      response.writeHead(401, { "www-authenticate": "Bearer" }).end();
    });
  });
  await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
  process.env.BACKEND_URL = `http://127.0.0.1:${(server.address() as AddressInfo).port}`;
  process.env.AUTH_SECRET = SECRET;
  process.env.AUTH_AUTHSERVER_ID = "authserver-web";
});

afterAll(() => new Promise<void>((resolve) => server.close(() => resolve())));

// The renewal log lines (console.info and console.warn), parsed.
let logged: { level: string; line: Record<string, unknown> }[] = [];

beforeEach(() => {
  renewals = [];
  logged = [];
  for (const level of ["info", "warn"] as const) {
    vi.spyOn(console, level).mockImplementation((line: string) => {
      logged.push({ level, line: JSON.parse(line) as Record<string, unknown> });
    });
  }
});

afterEach(() => {
  vi.restoreAllMocks();
});

const now = () => Math.floor(Date.now() / 1000);

async function request(session: JWT | null, init: { method?: string; body?: string; cookie?: string } = {}) {
  const cookie = init.cookie ?? (session ? `${COOKIE}=${await encode({ token: session, secret: SECRET, salt: COOKIE })}` : "");
  return new Request("http://localhost:3000/api/identity/me", {
    method: init.method ?? "GET",
    body: init.body,
    headers: { host: "localhost:3000", ...(cookie ? { cookie } : {}) },
  });
}

/** The session the browser holds after the response: its cookies applied over the request's. */
async function sessionAfter(sent: Request, response: Response): Promise<JWT | null> {
  const jar = new Map(
    (sent.headers.get("cookie") ?? "")
      .split("; ")
      .filter(Boolean)
      .map((pair) => [pair.slice(0, pair.indexOf("=")), pair.slice(pair.indexOf("=") + 1)] as const),
  );
  for (const cookie of response.headers.getSetCookie()) {
    const [pair, ...attributes] = cookie.split("; ");
    const name = pair.slice(0, pair.indexOf("="));
    if (attributes.some((attribute) => attribute.toLowerCase() === "max-age=0")) jar.delete(name);
    else jar.set(name, pair.slice(pair.indexOf("=") + 1));
  }
  const header = [...jar].map(([name, value]) => `${name}=${value}`).join("; ");
  return getToken({
    req: new Request("http://localhost:3000/", { headers: { cookie: header } }),
    secret: SECRET,
    secureCookie: false,
  });
}

const options = { forwardCookies: false, redirect: "follow" } as const;

describe("forwardWithSession", () => {
  it("attaches a fresh access token as it is, renewing nothing", async () => {
    const sent = await request({ accessToken: "fresh", refreshToken: "good", expiresAt: now() + 300 });
    const response = await forwardWithSession(sent, options);

    expect(response.status).toBe(200);
    expect(response.headers.get("x-token")).toBe("fresh");
    expect(response.headers.getSetCookie()).toEqual([]);
    expect(renewals).toHaveLength(0);
    expect(logged).toEqual([]);
  });

  it("renews an expired access token first and keeps the new one in the session cookie", async () => {
    const sent = await request({ accessToken: "stale", refreshToken: "good", expiresAt: now() - 1, idToken: "id" });
    const response = await forwardWithSession(sent, options);

    expect(response.status).toBe(200);
    expect(response.headers.get("x-token")).toBe("renewed-1");
    expect(renewals[0].get("client_id")).toBe("authserver-web");
    const session = await sessionAfter(sent, response);
    expect(session?.accessToken).toBe("renewed-1");
    expect(session?.refreshToken).toBe("good");
    expect(session?.idToken).toBe("id");
    expect(session?.expiresAt).toBeGreaterThan(now() + 500);
    expect(logged).toEqual([
      { level: "info", line: expect.objectContaining({ event: "bff.session-renewal", reason: "expired", status: 200 }) },
    ]);
    expect(JSON.stringify(logged)).not.toMatch(/renewed-1|good|stale/);
  });

  it("renews and retries once, body and all, when the backend refuses a token not yet expired", async () => {
    // The backend restarted: its signing key is per process, the old token's signature is unknown.
    const sent = await request(
      { accessToken: "rotated", refreshToken: "good", expiresAt: now() + 300 },
      { method: "POST", body: '{"userName":"ada"}' },
    );
    const response = await forwardWithSession(sent, options);

    expect(response.status).toBe(200);
    expect(response.headers.get("x-token")).toBe("renewed-1");
    expect(await response.text()).toBe('{"userName":"ada"}');
    expect((await sessionAfter(sent, response))?.accessToken).toBe("renewed-1");
    expect(logged[0].line).toMatchObject({ reason: "refused", method: "POST", status: 200 });
  });

  it("passes the 401 on when the refresh token is refused too", async () => {
    const sent = await request({ accessToken: "rotated", refreshToken: "revoked", expiresAt: now() + 300 });
    const response = await forwardWithSession(sent, options);

    expect(response.status).toBe(401);
    expect(response.headers.getSetCookie()).toEqual([]);
    expect(renewals).toHaveLength(1);
    expect(logged).toEqual([{ level: "warn", line: expect.objectContaining({ outcome: "refused", tokenStatus: 400 }) }]);
  });

  it("forwards without a token when there is no session", async () => {
    const response = await forwardWithSession(await request(null), options);

    expect(response.status).toBe(401);
    expect(renewals).toHaveLength(0);
  });
});

describe("renewedSessionCookies", () => {
  it("chunks a large session as Auth.js does and drops the chunks once it fits again", async () => {
    const large = { accessToken: "x".repeat(4000), refreshToken: "good", expiresAt: now() + 600 };
    const chunked = await renewedSessionCookies(await request(null), large);
    expect(chunked.map((cookie) => cookie.split("=")[0])).toEqual([`${COOKIE}.0`, `${COOKIE}.1`]);
    const holding = new Request("http://localhost:3000/", {
      headers: { cookie: chunked.map((cookie) => cookie.split("; ")[0]).join("; ") },
    });
    expect((await getToken({ req: holding, secret: SECRET, secureCookie: false }))?.accessToken).toBe(large.accessToken);

    const small = await renewedSessionCookies(holding, { ...large, accessToken: "small" });
    const response = new Response(null);
    small.forEach((cookie) => response.headers.append("set-cookie", cookie));
    expect((await sessionAfter(holding, response))?.accessToken).toBe("small");
  });

  it("uses the __Secure- name over https", async () => {
    const secure = new Request("https://auth.example.com/api/x", { headers: { host: "auth.example.com" } });
    const [cookie] = await renewedSessionCookies(secure, { accessToken: "a", expiresAt: now() + 600 });
    expect(cookie).toMatch(/^__Secure-authjs\.session-token=.*; Secure/);
  });
});
