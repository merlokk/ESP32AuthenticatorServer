import { describe, expect, it } from "vitest";
import { deviceStores, statusSummary, syncState } from "./device-status";

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

describe("deviceStores", () => {
  it("reads the store versions and the seed id", () => {
    expect(
      deviceStores({ passwordsVersion: 57, passkeysVersion: 339, seedId: "4c8be86e" }),
    ).toEqual({
      seedId: "4c8be86e",
      passwords: 57,
      passkeys: 339,
    });
  });

  it("leaves out what is missing or malformed", () => {
    expect(
      deviceStores({ passwordsVersion: -1, passkeysVersion: "3", seedId: "4C8BE86E" }),
    ).toEqual({});
    expect(deviceStores(null)).toEqual({});
  });
});

describe("syncState", () => {
  it("compares the device's counter with the server's", () => {
    expect(syncState(5, 5)).toBe("synced");
    expect(syncState(4, 5)).toBe("behind");
    expect(syncState(6, 5)).toBe("ahead");
    expect(syncState(1, undefined)).toBe("not-on-server");
  });
});
