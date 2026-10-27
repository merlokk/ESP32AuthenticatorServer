import { describe, expect, it } from "vitest";
import { statusSummary } from "./device-status";

describe("statusSummary", () => {
  it("reads the documented fields", () => {
    expect(statusSummary({ firmware: "0.5.0", wifi: true, ble: false, battery: 87.4 })).toBe(
      "Wi-Fi on · BLE off · battery 87%",
    );
  });

  it("skips what is missing or of another type", () => {
    expect(statusSummary({ wifi: "yes", battery: 50 })).toBe("battery 50%");
    expect(statusSummary({ firmware: "0.5.0" })).toBe("—");
  });

  it("says nothing before a first report", () => {
    expect(statusSummary(null)).toBe("—");
    expect(statusSummary(undefined)).toBe("—");
  });
});
