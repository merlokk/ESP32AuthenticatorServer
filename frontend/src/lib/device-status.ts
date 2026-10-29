// A device's last status report (POST /api/device/status, device-protocol.md) as one line. The
// report is the device's own JSON: any field may be missing or of another type.

export function statusSummary(status: unknown): string {
  if (typeof status !== "object" || status === null) return "—";
  const report = status as Record<string, unknown>;
  const parts: string[] = [];
  const toggle = (name: string, key: string) => {
    if (typeof report[key] === "boolean") parts.push(`${name} ${report[key] ? "on" : "off"}`);
  };
  toggle("Wi-Fi", "wifi");
  toggle("BLE", "ble");
  if (typeof report.battery === "number") parts.push(`battery ${Math.round(report.battery)}%`);
  return parts.length > 0 ? parts.join(" · ") : "—";
}

/** What the device holds (status fields of firmware 0.7.1+): its store versions and seed id. */
export type DeviceStores = { seedId?: string; passwords?: number; passkeys?: number };

const isVersion = (value: unknown): value is number =>
  typeof value === "number" && Number.isInteger(value) && value >= 0;

export function deviceStores(status: unknown): DeviceStores {
  if (typeof status !== "object" || status === null) return {};
  const report = status as Record<string, unknown>;
  const held: DeviceStores = {};
  if (typeof report.seedId === "string" && /^[0-9a-f]{8}$/.test(report.seedId)) {
    held.seedId = report.seedId;
  }
  if (isVersion(report.passwordsVersion)) held.passwords = report.passwordsVersion;
  if (isVersion(report.passkeysVersion)) held.passkeys = report.passkeysVersion;
  return held;
}

/**
 * The device's copy against the server's newest of the same store (kind and seed id): `ahead` is
 * a write the device has not uploaded yet, `behind` one it has not downloaded.
 */
export type SyncState = "synced" | "behind" | "ahead" | "not-on-server";

export function syncState(device: number, server: number | undefined): SyncState {
  if (server === undefined) return "not-on-server";
  if (device === server) return "synced";
  return device < server ? "behind" : "ahead";
}
