import type { StoreRow } from "@/api/generated/model";
import { deviceStores, syncState, type SyncState } from "@/lib/device-status";

const WARN = "text-amber-700 dark:text-amber-400";

const STATE: Record<SyncState, { label: string; title: string; className: string }> = {
  synced: {
    label: "synced",
    title: "The server holds this version",
    className: "text-muted-foreground",
  },
  behind: {
    label: "behind",
    title: "The server holds a newer version: the device gets it on its next sync",
    className: WARN,
  },
  ahead: {
    label: "ahead",
    title: "The device holds a version the server has not got: it uploads it on its next sync",
    className: WARN,
  },
  "not-on-server": {
    label: "not on server",
    title: "The server holds no copy of this store under the device's seed id",
    className: WARN,
  },
};

/**
 * The store versions and seed id from a device's last status report. With `stores` (the owner's,
 * GET /api/sync/stores) each version is set against the server's newest of the same store.
 */
export function DeviceStores({ status, stores }: { status: unknown; stores?: StoreRow[] }) {
  const held = deviceStores(status);
  const rows = [
    { label: "Passwords", kind: 2, version: held.passwords },
    { label: "Passkeys", kind: 1, version: held.passkeys },
  ].flatMap((row) => (row.version === undefined ? [] : [{ ...row, version: row.version }]));

  if (rows.length === 0 && !held.seedId) return <span>—</span>;

  const server = (kind: number) => {
    const versions = (stores ?? [])
      .filter((store) => store.kind === kind && store.seedId === held.seedId)
      .map((store) => store.version);
    return versions.length > 0 ? Math.max(...versions) : undefined;
  };

  return (
    <div className="flex flex-col gap-0.5 whitespace-nowrap">
      {rows.map((row) => {
        const state =
          stores && held.seedId ? STATE[syncState(row.version, server(row.kind))] : undefined;
        return (
          <span key={row.kind}>
            {row.label} <span className="tabular-nums">v{row.version}</span>
            {state && (
              <span className={`ml-1.5 ${state.className}`} title={state.title}>
                {state.label}
              </span>
            )}
          </span>
        );
      })}
      {held.seedId && (
        <span className="text-muted-foreground">
          seed <span className="font-mono">{held.seedId}</span>
        </span>
      )}
    </div>
  );
}
