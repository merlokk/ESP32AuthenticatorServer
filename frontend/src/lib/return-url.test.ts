import { describe, expect, it } from "vitest";
import { DEFAULT_RETURN_URL, isAuthorizeUrl, safeReturnUrl } from "./return-url";

describe("safeReturnUrl", () => {
  it("keeps a path on this origin with its query", () => {
    expect(safeReturnUrl("/connect/authorize?client_id=a&state=b")).toBe(
      "/connect/authorize?client_id=a&state=b",
    );
    expect(safeReturnUrl("/devices")).toBe("/devices");
  });

  it.each([
    undefined,
    null,
    "",
    "devices",
    "https://evil.example/",
    "//evil.example/",
    "/\\evil.example/",
    "javascript:alert(1)",
  ])("falls back to the default for %s", (value) => {
    expect(safeReturnUrl(value)).toBe(DEFAULT_RETURN_URL);
  });

  it("drops a fragment", () => {
    expect(safeReturnUrl("/devices#top")).toBe("/devices");
  });
});

describe("isAuthorizeUrl", () => {
  it("recognises the authorization endpoint", () => {
    expect(isAuthorizeUrl("/connect/authorize?client_id=a")).toBe(true);
    expect(isAuthorizeUrl("/connect/authorize")).toBe(true);
  });

  it("refuses anything else", () => {
    expect(isAuthorizeUrl("/connect/authorizex")).toBe(false);
    expect(isAuthorizeUrl("/")).toBe(false);
  });
});
