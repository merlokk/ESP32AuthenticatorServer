"use client";

import { RefreshCw } from "lucide-react";
import { errorMessage } from "@/api/errors";
import { useGetMyDevices } from "@/api/generated/identity/identity";
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

const dateTimeFormat = new Intl.DateTimeFormat("en", { dateStyle: "medium", timeStyle: "short" });

export function DevicesTable() {
  // Fresh on every visit and polled: a device's status report or a registration changes the rows.
  const { data, error, isError, isFetching, isPending, refetch } = useGetMyDevices({
    query: { staleTime: 0, refetchInterval: 30_000 },
  });

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
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </CardContent>
    </Card>
  );
}
