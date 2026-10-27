import { describe, expect, it } from "vitest";
import { formatMinutes, windowState } from "./registration-window";

const now = new Date("2026-10-27T12:00:00Z");

describe("windowState", () => {
  it("is closed without a date or once it has passed", () => {
    expect(windowState(null, now)).toEqual({ open: false });
    expect(windowState(undefined, now)).toEqual({ open: false });
    expect(windowState("2026-10-27T12:00:00Z", now)).toEqual({ open: false });
    expect(windowState("2026-10-27T11:00:00Z", now)).toEqual({ open: false });
  });

  it("is open until the date, minutes rounded up", () => {
    expect(windowState("2026-10-27T12:59:30Z", now)).toEqual({
      open: true,
      until: new Date("2026-10-27T12:59:30Z"),
      minutesLeft: 60,
    });
    expect(windowState("2026-10-27T12:00:01Z", now)).toMatchObject({ open: true, minutesLeft: 1 });
  });
});

describe("formatMinutes", () => {
  it.each([
    [1, "1 min"],
    [45, "45 min"],
    [60, "1 h"],
    [65, "1 h 5 min"],
    [1440, "24 h"],
  ])("%i → %s", (minutes, text) => {
    expect(formatMinutes(minutes)).toBe(text);
  });
});
