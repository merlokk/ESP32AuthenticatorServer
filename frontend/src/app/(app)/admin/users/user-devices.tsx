"use client";

import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Trash2 } from "lucide-react";
import { errorMessage } from "@/api/errors";
import {
  getGetUserDevicesQueryKey,
  getGetUsersQueryKey,
  removeUserDevice,
  useGetUserDevices,
} from "@/api/generated/identity/identity";
import type { UserDevice, UserRow } from "@/api/generated/model";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { statusSummary } from "@/lib/device-status";
import { ConfirmDialog } from "./confirm-dialog";

const dateTimeFormat = new Intl.DateTimeFormat("en", { dateStyle: "medium", timeStyle: "short" });

/** One user's devices, loaded when their row opens. Never a key or a TOTP secret: the API has none. */
export function UserDevices({ user }: { user: UserRow }) {
  // Polled like the user list: a device may register while the row is open.
  const { data, error, isError, isPending } = useGetUserDevices(user.id, {
    query: { refetchInterval: 30_000 },
  });
  const queryClient = useQueryClient();
  const [removing, setRemoving] = useState<UserDevice>();

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
    <>
      <table className="w-full text-sm" aria-label={`Devices of ${user.userName}`}>
        <thead className="text-left text-xs text-muted-foreground">
          <tr>
            <th className="py-1 pr-4 font-normal">Serial</th>
            <th className="py-1 pr-4 font-normal">MAC</th>
            <th className="py-1 pr-4 font-normal">Registered</th>
            <th className="py-1 pr-4 font-normal">Last seen</th>
            <th className="py-1 pr-4 font-normal">Firmware</th>
            <th className="py-1 pr-4 font-normal">Status</th>
            <th className="py-1 font-normal">
              <span className="sr-only">Actions</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {data.devices.map((device) => (
            <tr key={device.id}>
              <td className="py-1 pr-4 font-mono text-xs">{device.serial}</td>
              <td className="py-1 pr-4 font-mono text-xs">{device.mac}</td>
              <td className="py-1 pr-4">{dateTimeFormat.format(new Date(device.registeredAt))}</td>
              <td className="py-1 pr-4">
                {device.lastSeenAt ? dateTimeFormat.format(new Date(device.lastSeenAt)) : "—"}
              </td>
              <td className="py-1 pr-4 font-mono text-xs">{device.firmware ?? "—"}</td>
              <td className="py-1 pr-4 text-xs">{statusSummary(device.status)}</td>
              <td className="py-1 text-right">
                <Button
                  variant="ghost"
                  size="icon-sm"
                  aria-label={`Remove ${device.serial}`}
                  className="text-destructive hover:text-destructive"
                  onClick={() => setRemoving(device)}
                >
                  <Trash2 aria-hidden />
                </Button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      {removing && (
        <ConfirmDialog
          title={`Remove device ${removing.serial}?`}
          description={`It no longer signs in or syncs for ${user.userName}; the account and its stores stay. It may register again through a registration window.`}
          confirm="Remove"
          destructive
          onConfirm={async () => {
            await removeUserDevice(user.id, removing.id);
            await Promise.all([
              queryClient.invalidateQueries({ queryKey: getGetUserDevicesQueryKey(user.id) }),
              queryClient.invalidateQueries({ queryKey: getGetUsersQueryKey() }),
            ]);
          }}
          onClose={() => setRemoving(undefined)}
        />
      )}
    </>
  );
}
