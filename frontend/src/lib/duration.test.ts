import { describe, expect, it } from "vitest";
import { formatDuration } from "./duration";

describe("formatDuration", () => {
  it.each([
    [2, "2 s"],
    [600, "10 min"],
    [3_600, "1 h"],
    [5_400, "1 h 30 min"],
    [28_800, "8 h"],
    [86_400, "1 day"],
    [172_800, "2 days"],
    [90_000, "1 day 1 h"],
  ])("%i s → %s", (seconds, text) => {
    expect(formatDuration(seconds)).toBe(text);
  });
});
