"use client";

import { errorMessage } from "@/api/errors";
import { useGetUserDevices } from "@/api/generated/identity/identity";
import type { UserRow } from "@/api/generated/model";
import { Skeleton } from "@/components/ui/skeleton";
import { statusSummary } from "@/lib/device-status";

const dateTimeFormat = new Intl.DateTimeFormat("en", { dateStyle: "medium", timeStyle: "short" });

/** One user's devices, loaded when their row opens. Never a key or a TOTP secret: the API has none. */
export function UserDevices({ user }: { user: UserRow }) {
  // Polled like the user list: a device may register while the row is open.
  const { data, error, isError, isPending } = useGetUserDevices(user.id, {
    query: { refetchInterval: 30_000 },
  });

  if (isPending) return <Skeleton className="h-12 w-full" />;
  if (isError) {
    return (
      <p role="alert" className="text-sm text-destructive">
        {errorMessage(error, "The devices could not be loaded.")}
      </p>
    );
  }
  if (data.devices.length === 0) {
    return <p className="text-sm text-muted-foreground">No devices.</p>;
  }

  return (
    <table className="w-full text-sm" aria-label={`Devices of ${user.userName}`}>
      <thead className="text-left text-xs text-muted-foreground">
        <tr>
          <th className="py-1 pr-4 font-normal">Serial</th>
          <th className="py-1 pr-4 font-normal">MAC</th>
          <th className="py-1 pr-4 font-normal">Registered</th>
          <th className="py-1 pr-4 font-normal">Last seen</th>
          <th className="py-1 pr-4 font-normal">Firmware</th>
          <th className="py-1 font-normal">Status</th>
        </tr>
      </thead>
      <tbody>
        {data.devices.map((device) => (
          <tr key={device.id}>
            <td className="py-1 pr-4 font-mono text-xs">{device.serial}</td>
            <td className="py-1 pr-4 font-mono text-xs">{device.mac}</td>
            <td className="py-1 pr-4">{dateTimeFormat.format(new Date(device.registeredAt))}</td>
            <td className="py-1">
              {device.lastSeenAt ? dateTimeFormat.format(new Date(device.lastSeenAt)) : "—"}
            </td>
            <td className="py-1 pr-4 font-mono text-xs">{device.firmware ?? "—"}</td>
            <td className="py-1 text-xs">{statusSummary(device.status)}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
