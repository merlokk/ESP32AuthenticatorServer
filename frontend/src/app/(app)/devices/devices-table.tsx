"use client";

import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { RefreshCw, Trash2 } from "lucide-react";
import { errorMessage } from "@/api/errors";
import {
  getGetMyDevicesQueryKey,
  removeMyDevice,
  useGetMyDevices,
} from "@/api/generated/identity/identity";
import type { MyDevice } from "@/api/generated/model";
import { Button } from "@/components/ui/button";
import { Card, CardAction, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { statusSummary } from "@/lib/device-status";
import { Skeleton } from "@/components/ui/skeleton";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { ConfirmDialog } from "../admin/users/confirm-dialog";

const dateTimeFormat = new Intl.DateTimeFormat("en", { dateStyle: "medium", timeStyle: "short" });

export function DevicesTable() {
  // Fresh on every visit and polled: a device's status report or a registration changes the rows.
  const { data, error, isError, isFetching, isPending, refetch } = useGetMyDevices({
    query: { staleTime: 0, refetchInterval: 30_000 },
  });
  const queryClient = useQueryClient();
  const [removing, setRemoving] = useState<MyDevice>();

  return (
    <Card>
      <CardHeader>
        <CardTitle>Your devices</CardTitle>
        <CardAction>
          <Button variant="outline" size="sm" disabled={isFetching} onClick={() => void refetch()}>
            <RefreshCw className={isFetching ? "animate-spin" : undefined} aria-hidden />
            Refresh
          </Button>
        </CardAction>
      </CardHeader>
      <CardContent>
        {isPending ? (
          <Skeleton className="h-24 w-full" />
        ) : isError ? (
          <p role="alert" className="text-sm text-destructive">
            {errorMessage(error, "The devices could not be loaded.")}
          </p>
        ) : data.devices.length === 0 ? (
          <p className="text-sm text-muted-foreground">No devices yet.</p>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Serial</TableHead>
                <TableHead>MAC</TableHead>
                <TableHead>Registered</TableHead>
                <TableHead>Last seen</TableHead>
                <TableHead>Firmware</TableHead>
                <TableHead>Status</TableHead>
                <TableHead className="text-right">Actions</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {data.devices.map((device) => (
                <TableRow key={device.id}>
                  <TableCell className="font-mono text-xs">{device.serial}</TableCell>
                  <TableCell className="font-mono text-xs">{device.mac}</TableCell>
                  <TableCell>{dateTimeFormat.format(new Date(device.registeredAt))}</TableCell>
                  <TableCell>
                    {device.lastSeenAt ? dateTimeFormat.format(new Date(device.lastSeenAt)) : "—"}
                  </TableCell>
                  <TableCell className="font-mono text-xs">{device.firmware ?? "—"}</TableCell>
                  <TableCell className="text-xs">{statusSummary(device.status)}</TableCell>
                  <TableCell className="text-right">
                    <Button
                      variant="ghost"
                      size="icon-sm"
                      aria-label={`Remove ${device.serial}`}
                      className="text-destructive hover:text-destructive"
                      onClick={() => setRemoving(device)}
                    >
                      <Trash2 aria-hidden />
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </CardContent>
      {removing && (
        <ConfirmDialog
          title={`Remove device ${removing.serial}?`}
          description={
            <>
              It signs in and syncs no more; its stores stay on the server. To use it again,
              register it through a registration window.
              {data?.devices.length === 1 &&
                " It is your last device: only a passkey signs you in then."}
            </>
          }
          confirm="Remove"
          destructive
          onConfirm={async () => {
            await removeMyDevice(removing.id);
            await queryClient.invalidateQueries({ queryKey: getGetMyDevicesQueryKey() });
          }}
          onClose={() => setRemoving(undefined)}
        />
      )}
    </Card>
  );
}
