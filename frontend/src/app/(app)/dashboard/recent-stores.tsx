"use client";

import { useMemo } from "react";
import Link from "next/link";
import { Database } from "lucide-react";
import { errorMessage } from "@/api/errors";
import { useGetMyDevices } from "@/api/generated/identity/identity";
import { useGetStores } from "@/api/generated/sync/sync";
import { Card, CardContent, CardDescription, CardHeader } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { deviceLabel, deviceName, storeKindLabel } from "@/lib/stores";

const dateTime = new Intl.DateTimeFormat("en", { dateStyle: "medium", timeStyle: "short" });

// The user's stores, the most recently updated first: the newest version of each and when it
// reached the server (the server's clock, not the device's).
export function RecentStores() {
  // Polled like the Stores page: a device may sync while the page is open.
  const stores = useGetStores({ query: { refetchInterval: 30_000 } });
  const devices = useGetMyDevices();
  const names = useMemo(
    () => new Map((devices.data?.devices ?? []).map((device) => [device.id, deviceName(device)])),
    [devices.data],
  );
  const rows = useMemo(
    () => [...(stores.data?.stores ?? [])].sort((a, b) => b.uploadedAt.localeCompare(a.uploadedAt)),
    [stores.data],
  );

  return (
    <Card className="sm:col-span-2">
      <CardHeader>
        <CardDescription className="flex items-center gap-2">
          <Database className="size-4" aria-hidden />
          Stores
        </CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-3">
        {stores.isPending ? (
          <Skeleton className="h-24 w-full" />
        ) : stores.isError ? (
          <p role="alert" className="text-sm text-destructive">
            {errorMessage(stores.error, "The stores could not be loaded.")}
          </p>
        ) : rows.length === 0 ? (
          <p className="text-sm text-muted-foreground">
            No store synced yet: a device uploads its stores on its next sync.
          </p>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Store</TableHead>
                <TableHead>Seed id</TableHead>
                <TableHead className="text-right">Version</TableHead>
                <TableHead>Updated</TableHead>
                <TableHead>Written by</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {rows.map((store) => (
                <TableRow key={store.id}>
                  <TableCell>{storeKindLabel(store.kind)}</TableCell>
                  <TableCell className="font-mono text-xs" title={store.seedId}>
                    {store.seedId.slice(0, 8)}
                  </TableCell>
                  <TableCell className="text-right tabular-nums">{store.version}</TableCell>
                  <TableCell>{dateTime.format(new Date(store.uploadedAt))}</TableCell>
                  <TableCell className="font-mono text-xs">
                    {deviceLabel(store.uploadedByDeviceId, names)}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
        <Link href="/stores" className="text-sm underline underline-offset-4">
          View stores and their history
        </Link>
      </CardContent>
    </Card>
  );
}
