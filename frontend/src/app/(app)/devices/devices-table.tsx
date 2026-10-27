"use client";

import { errorMessage } from "@/api/errors";
import { useGetMyDevices } from "@/api/generated/identity/identity";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
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
  const { data, error, isError, isPending } = useGetMyDevices();

  return (
    <Card>
      <CardHeader>
        <CardTitle>Your devices</CardTitle>
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
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </CardContent>
    </Card>
  );
}
