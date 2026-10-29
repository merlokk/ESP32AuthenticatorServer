// What the store header's fields mean to a person (firmware store_header.h, architecture.md §1).

/** 1 fido.bin, 2 vault.bin. */
export function storeKindLabel(kind: number): string {
  switch (kind) {
    case 1:
      return "Passkeys (FIDO2)";
    case 2:
      return "Passwords and TOTP";
    default:
      return `Store kind ${kind}`;
  }
}

export function cipherLabel(cipher: number): string {
  return cipher === 1 ? "AES-256-GCM" : cipher === 0 ? "None (plain)" : `Cipher ${cipher}`;
}

const dateTime = new Intl.DateTimeFormat("en", { dateStyle: "medium", timeStyle: "short" });

/** Unix seconds by the device's clock; 0 means the device had no believable time. */
export function formatModified(seconds: number): string {
  return seconds === 0 ? "unknown (no clock)" : dateTime.format(new Date(seconds * 1000));
}

/** "1,234 bytes", "12.3 KB". */
export function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} bytes`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

/** A device as the person knows it: the name they gave it, or its serial. */
export function deviceName(device: { name?: string | null; serial: string }): string {
  return device.name ?? device.serial;
}

/** A device id as the person knows the device: its name (or serial), or a removed one. */
export function deviceLabel(id: string, names: ReadonlyMap<string, string>): string {
  return names.get(id) ?? `a removed device (${id.slice(0, 8)})`;
}
