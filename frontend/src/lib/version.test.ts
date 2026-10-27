import { describe, expect, it } from "vitest";
import { describeVersion } from "./version";

describe("describeVersion", () => {
  it("adds the short commit", () => {
    expect(describeVersion("0.5.0-7-gcb9915d", "cb9915d84d11ba36d54c1c4be687afc0eee18a67")).toBe(
      "0.5.0-7-gcb9915d (cb9915d)",
    );
  });

  it("is the version alone without a commit", () => {
    expect(describeVersion("0.0.0", null)).toBe("0.0.0");
    expect(describeVersion("0.0.0", "")).toBe("0.0.0");
  });
});
