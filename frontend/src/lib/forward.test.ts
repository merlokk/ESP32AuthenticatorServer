// @vitest-environment node
import { createServer, type Server } from "node:http";
import type { AddressInfo } from "node:net";
import { afterAll, beforeAll, describe, expect, it } from "vitest";
import { forward } from "./backend";

// A backend that refuses before reading the body, as ASP.NET Core does for a missing token.
let server: Server;

beforeAll(async () => {
  server = createServer((request, response) => {
    if (request.url === "/echo") {
      const chunks: Buffer[] = [];
      request.on("data", (chunk: Buffer) => chunks.push(chunk));
      request.on("end", () => response.writeHead(200).end(Buffer.concat(chunks)));
      return;
    }
    response.writeHead(401, { "www-authenticate": "Bearer" }).end();
  });
  await new Promise<void>((resolve) => server.listen(0, "127.0.0.1", resolve));
  process.env.BACKEND_URL = `http://127.0.0.1:${(server.address() as AddressInfo).port}`;
});

afterAll(() => new Promise<void>((resolve) => server.close(() => resolve())));

const post = (path: string, body: BodyInit) =>
  new Request(`http://localhost:3000${path}`, {
    method: "POST",
    body,
    headers: { host: "localhost:3000", "content-type": "application/octet-stream" },
  });

describe("forward", () => {
  it("returns the backend's 401 to a request with a body, not a 502", async () => {
    const response = await forward(post("/api/refused", "{}"), {
      forwardCookies: false,
      redirect: "follow",
    });
    expect(response.status).toBe(401);
  });

  it("passes the body through untouched", async () => {
    const bytes = new Uint8Array([0, 1, 2, 250, 251, 252]);
    const response = await forward(post("/echo", bytes), {
      forwardCookies: false,
      redirect: "follow",
    });
    expect(response.status).toBe(200);
    expect(new Uint8Array(await response.arrayBuffer())).toEqual(bytes);
  });
});
