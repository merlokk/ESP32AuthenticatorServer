"use client";

import { useMemo, useState, type ReactNode } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { ChevronDown, ChevronRight, Database, Trash2 } from "lucide-react";
import { errorMessage } from "@/api/errors";
import { useGetMyDevices } from "@/api/generated/identity/identity";
import type { StoreRow } from "@/api/generated/model";
import { deleteStore, getGetStoresQueryKey, useGetStores } from "@/api/generated/sync/sync";
import { Button } from "@/components/ui/button";
import { Card, CardAction, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import {
  cipherLabel,
  deviceLabel,
  deviceName,
  formatBytes,
  formatModified,
  storeKindLabel,
} from "@/lib/stores";
import { ConfirmDialog } from "../admin/users/confirm-dialog";
import { StoreHistory } from "./store-history";

const dateTime = new Intl.DateTimeFormat("en", { dateStyle: "medium", timeStyle: "short" });
const count = new Intl.NumberFormat("en");

export function StoresList() {
  // Polled: a device may sync while the page is open.
  const stores = useGetStores({ query: { refetchInterval: 30_000 } });
  const devices = useGetMyDevices();
  const names = useMemo(
    () => new Map((devices.data?.devices ?? []).map((device) => [device.id, deviceName(device)])),
    [devices.data],
  );

  if (stores.isPending) return <Skeleton className="h-48 w-full" />;
  if (stores.isError) {
    return (
      <p role="alert" className="text-sm text-destructive">
        {errorMessage(stores.error, "The stores could not be loaded.")}
      </p>
    );
  }
  if (stores.data.stores.length === 0) {
    return (
      <Card>
        <CardContent className="flex items-center gap-3 text-sm text-muted-foreground">
          <Database className="size-5" aria-hidden />
          No store synced yet: a device uploads its stores on its next sync.
        </CardContent>
      </Card>
    );
  }

  return (
    <div className="flex flex-col gap-6">
      {stores.data.stores.map((store) => (
        <StoreCard key={store.id} store={store} names={names} />
      ))}
    </div>
  );
}

function StoreCard({ store, names }: { store: StoreRow; names: ReadonlyMap<string, string> }) {
  const [open, setOpen] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const queryClient = useQueryClient();

  return (
    <Card>
      <CardHeader>
        <CardTitle>{storeKindLabel(store.kind)}</CardTitle>
        <CardAction>
          <Button
            variant="ghost"
            size="icon-sm"
            aria-label={`Delete store ${storeKindLabel(store.kind)}`}
            onClick={() => setDeleting(true)}
          >
            <Trash2 aria-hidden />
          </Button>
        </CardAction>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        <dl className="grid gap-x-8 gap-y-3 text-sm sm:grid-cols-2">
          <Field label="Version">
            <span className="tabular-nums">{store.version}</span>
          </Field>
          <Field label="Modified (device clock)">{formatModified(store.modified)}</Field>
          <Field label="Written by">
            <span className="font-mono text-xs">
              {deviceLabel(store.uploadedByDeviceId, names)}
            </span>
          </Field>
          <Field label="Uploaded">{dateTime.format(new Date(store.uploadedAt))}</Field>
          <Field label="Encryption">{cipherLabel(store.cipher)}</Field>
          <Field label="Size">{formatBytes(store.length)}</Field>
          <Field label="PBKDF2 iterations">
            <span className="tabular-nums">{count.format(store.kdfIterations)}</span>
          </Field>
          <Field label="Key id">
            <span className="font-mono text-xs">{store.keyId}</span>
          </Field>
          <Field label="Store id">
            <span className="font-mono text-xs">{store.storeId}</span>
          </Field>
          <Field label="Seed id">
            <span className="font-mono text-xs">{store.seedId}</span>
          </Field>
        </dl>
        <div>
          <Button
            variant="ghost"
            size="sm"
            className="-ml-2.5"
            aria-expanded={open}
            aria-controls={`history-${store.id}`}
            onClick={() => setOpen((value) => !value)}
          >
            {open ? <ChevronDown aria-hidden /> : <ChevronRight aria-hidden />}
            History ({store.versions} {store.versions === 1 ? "version" : "versions"} kept)
          </Button>
          {open && (
            <div id={`history-${store.id}`} className="mt-2">
              <StoreHistory store={store} names={names} />
            </div>
          )}
        </div>
      </CardContent>
      {deleting && (
        <ConfirmDialog
          title={`Delete store ${storeKindLabel(store.kind)}?`}
          description={
            <>
              The server drops all {store.versions} {store.versions === 1 ? "copy" : "copies"} of it
              (store id <span className="font-mono">{store.storeId}</span>). Your devices keep
              theirs: one that still holds the store uploads it again on its next sync.
            </>
          }
          confirm="Delete"
          destructive
          onConfirm={async () => {
            await deleteStore(store.id);
            await queryClient.invalidateQueries({ queryKey: getGetStoresQueryKey() });
          }}
          onClose={() => setDeleting(false)}
        />
      )}
    </Card>
  );
}

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex flex-col gap-0.5">
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd>{children}</dd>
    </div>
  );
}
