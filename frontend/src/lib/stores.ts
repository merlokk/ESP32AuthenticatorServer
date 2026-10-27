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
  return bytes < 1024 ? `${bytes} bytes` : `${(bytes / 1024).toFixed(1)} KB`;
}

/** A device id as the person knows the device: its serial, or a removed one. */
export function deviceLabel(id: string, serials: ReadonlyMap<string, string>): string {
  return serials.get(id) ?? `a removed device (${id.slice(0, 8)})`;
}
