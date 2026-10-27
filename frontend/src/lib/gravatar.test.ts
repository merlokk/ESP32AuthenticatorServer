import { createHash } from "node:crypto";
import { describe, expect, it } from "vitest";
import { gravatarUrl, initials } from "./gravatar";

describe("gravatarUrl", () => {
  it("hashes the trimmed, lower-cased address with SHA-256", async () => {
    const hash = createHash("sha256").update("ada@example.com").digest("hex");
    expect(await gravatarUrl("  Ada@Example.com ", 64)).toBe(
      `https://www.gravatar.com/avatar/${hash}?s=64&d=identicon`,
    );
  });
});

describe("initials", () => {
  it.each([
    ["ada.lovelace", "AL"],
    ["ada", "AD"],
    ["grace_hopper", "GH"],
    ["x", "X"],
  ])("%s → %s", (name, expected) => {
    expect(initials(name)).toBe(expected);
  });
});
