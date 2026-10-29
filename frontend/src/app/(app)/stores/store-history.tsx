"use client";

import { errorMessage } from "@/api/errors";
import type { StoreRow } from "@/api/generated/model";
import { useGetStoreVersions } from "@/api/generated/sync/sync";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { deviceLabel, formatBytes, formatModified } from "@/lib/stores";

const dateTime = new Intl.DateTimeFormat("en", { dateStyle: "medium", timeStyle: "short" });

/**
 * The copies the server keeps, newest first. A key id unlike the newest copy's means the PIN or the
 * seed phrase changed on one of the devices between them.
 */
export function StoreHistory({
  store,
  names,
}: {
  store: StoreRow;
  names: ReadonlyMap<string, string>;
}) {
  const { data, error, isError, isPending } = useGetStoreVersions(store.id, {
    query: { refetchInterval: 30_000 },
  });

  if (isPending) return <Skeleton className="h-24 w-full" />;
  if (isError) {
    return (
      <p role="alert" className="text-sm text-destructive">
        {errorMessage(error, "The history could not be loaded.")}
      </p>
    );
  }

  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead className="text-right">Version</TableHead>
          <TableHead>Modified (device clock)</TableHead>
          <TableHead>Written by</TableHead>
          <TableHead>Key id</TableHead>
          <TableHead className="text-right">Size</TableHead>
          <TableHead>Uploaded</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {data.versions.map((version) => (
          <TableRow key={version.version}>
            <TableCell className="text-right tabular-nums">{version.version}</TableCell>
            <TableCell>{formatModified(version.modified)}</TableCell>
            <TableCell className="font-mono text-xs">
              {deviceLabel(version.uploadedByDeviceId, names)}
            </TableCell>
            <TableCell>
              <span className="font-mono text-xs">{version.keyId}</span>
              {version.keyId !== store.keyId && (
                <Badge
                  className="ml-2 bg-amber-100 text-amber-900 dark:bg-amber-500/20 dark:text-amber-300"
                  title="The PIN or the seed phrase changed"
                >
                  other key
                </Badge>
              )}
            </TableCell>
            <TableCell className="text-right tabular-nums">{formatBytes(version.length)}</TableCell>
            <TableCell>{dateTime.format(new Date(version.uploadedAt))}</TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}
