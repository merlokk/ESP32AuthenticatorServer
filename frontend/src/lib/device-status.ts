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
