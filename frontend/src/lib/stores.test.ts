import { describe, expect, it } from "vitest";
import {
  cipherLabel,
  deviceLabel,
  deviceName,
  formatBytes,
  formatModified,
  storeKindLabel,
} from "./stores";

describe("stores", () => {
  it("names the kinds and ciphers", () => {
    expect(storeKindLabel(1)).toBe("Passkeys (FIDO2)");
    expect(storeKindLabel(2)).toBe("Passwords and TOTP");
    expect(cipherLabel(1)).toBe("AES-256-GCM");
    expect(cipherLabel(0)).toBe("None (plain)");
  });

  it("says an unknown time rather than 1970", () => {
    expect(formatModified(0)).toBe("unknown (no clock)");
    expect(formatModified(1_790_000_000)).toMatch(/2026/);
  });

  it("formats sizes", () => {
    expect(formatBytes(512)).toBe("512 bytes");
    expect(formatBytes(130 * 1024)).toBe("130.0 KB");
    expect(formatBytes(1536 * 1024)).toBe("1.5 MB");
  });

  it("names a device by its name, else its serial", () => {
    expect(deviceName({ name: "Kitchen X4", serial: "e81a7bc9" })).toBe("Kitchen X4");
    expect(deviceName({ name: null, serial: "e81a7bc9" })).toBe("e81a7bc9");
  });

  it("labels a device id by its name, or says it is gone", () => {
    const names = new Map([["01a1-device", "Kitchen X4"]]);
    expect(deviceLabel("01a1-device", names)).toBe("Kitchen X4");
    expect(deviceLabel("0123456789abcdef", names)).toBe("a removed device (01234567)");
  });
});
