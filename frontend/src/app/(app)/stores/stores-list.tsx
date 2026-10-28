"use client";

import { useMemo, useState, type ReactNode } from "react";
import { ChevronDown, ChevronRight, Database } from "lucide-react";
import { errorMessage } from "@/api/errors";
import { useGetMyDevices } from "@/api/generated/identity/identity";
import type { StoreRow } from "@/api/generated/model";
import { useGetStores } from "@/api/generated/sync/sync";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import {
  cipherLabel,
  deviceLabel,
  formatBytes,
  formatModified,
  storeKindLabel,
} from "@/lib/stores";
import { StoreHistory } from "./store-history";

const dateTime = new Intl.DateTimeFormat("en", { dateStyle: "medium", timeStyle: "short" });
const count = new Intl.NumberFormat("en");

export function StoresList() {
  // Polled: a device may sync while the page is open.
  const stores = useGetStores({ query: { refetchInterval: 30_000 } });
  const devices = useGetMyDevices();
  const serials = useMemo(
    () => new Map((devices.data?.devices ?? []).map((device) => [device.id, device.serial])),
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
        <StoreCard key={store.id} store={store} serials={serials} />
      ))}
    </div>
  );
}

function StoreCard({ store, serials }: { store: StoreRow; serials: ReadonlyMap<string, string> }) {
  const [open, setOpen] = useState(false);

  return (
    <Card>
      <CardHeader>
        <CardTitle>{storeKindLabel(store.kind)}</CardTitle>
        <CardDescription className="font-mono text-xs">
          store {store.storeId} · seed {store.seedId}
        </CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        <dl className="grid gap-x-8 gap-y-3 text-sm sm:grid-cols-2">
          <Field label="Version">
            <span className="tabular-nums">{store.version}</span>
          </Field>
          <Field label="Modified (device clock)">{formatModified(store.modified)}</Field>
          <Field label="Written by">
            <span className="font-mono text-xs">
              {deviceLabel(store.uploadedByDeviceId, serials)}
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
              <StoreHistory store={store} serials={serials} />
            </div>
          )}
        </div>
      </CardContent>
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
