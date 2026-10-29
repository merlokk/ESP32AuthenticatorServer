"use client";

import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Trash2 } from "lucide-react";
import { errorMessage } from "@/api/errors";
import type { StoreRow, VersionRow } from "@/api/generated/model";
import {
  deleteStoreVersion,
  getGetStoresQueryKey,
  getGetStoreVersionsQueryKey,
  useGetStoreVersions,
} from "@/api/generated/sync/sync";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
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
import { ConfirmDialog } from "../admin/users/confirm-dialog";

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
  const queryClient = useQueryClient();
  const [deleting, setDeleting] = useState<VersionRow>();

  if (isPending) return <Skeleton className="h-24 w-full" />;
  if (isError) {
    return (
      <p role="alert" className="text-sm text-destructive">
        {errorMessage(error, "The history could not be loaded.")}
      </p>
    );
  }

  const newest = data.versions[0]?.version;
  const last = data.versions.length === 1;

  return (
    <>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead className="text-right">Version</TableHead>
            <TableHead>Modified (device clock)</TableHead>
            <TableHead>Written by</TableHead>
            <TableHead>Key id</TableHead>
            <TableHead className="text-right">Size</TableHead>
            <TableHead>Uploaded</TableHead>
            <TableHead>
              <span className="sr-only">Actions</span>
            </TableHead>
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
              <TableCell className="text-right tabular-nums">
                {formatBytes(version.length)}
              </TableCell>
              <TableCell>{dateTime.format(new Date(version.uploadedAt))}</TableCell>
              <TableCell className="text-right">
                <Button
                  variant="ghost"
                  size="icon-sm"
                  aria-label={`Delete version ${version.version}`}
                  onClick={() => setDeleting(version)}
                >
                  <Trash2 aria-hidden />
                </Button>
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
      {deleting && (
        <ConfirmDialog
          title={`Delete version ${deleting.version}?`}
          description={
            last
              ? "It is the only copy the server keeps: the store goes with it. A device that still holds the store uploads it again on its next sync."
              : deleting.version === newest
                ? "It is the newest copy: devices get the one below it. A device that holds this version or a later one uploads it again on its next sync."
                : "The server drops this copy; the newest stays as it is."
          }
          confirm="Delete"
          destructive
          onConfirm={async () => {
            await deleteStoreVersion(store.id, String(deleting.version));
            await Promise.all([
              queryClient.invalidateQueries({ queryKey: getGetStoreVersionsQueryKey(store.id) }),
              queryClient.invalidateQueries({ queryKey: getGetStoresQueryKey() }),
            ]);
          }}
          onClose={() => setDeleting(undefined)}
        />
      )}
    </>
  );
}
