"use client";

import { useState, useSyncExternalStore } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { KeyRound, Pencil, Plus, Trash2 } from "lucide-react";
import { errorMessage } from "@/api/errors";
import {
  beginPasskeyRegistration,
  completePasskeyRegistration,
  getGetPasskeysQueryKey,
  removePasskey,
  renamePasskey,
  useGetPasskeys,
} from "@/api/generated/identity/identity";
import type { Passkey } from "@/api/generated/model";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { createCredential, passkeysSupported } from "@/lib/webauthn";

// The backend's limits (AccountFields): at most 10 passkeys, names of 1 to 64 characters.
const MAXIMUM_PASSKEYS = 10;
const MAXIMUM_NAME_LENGTH = 64;

// English like the rest of the UI (html lang="en"), not the browser's locale.
const dateFormat = new Intl.DateTimeFormat("en", { dateStyle: "medium" });

export function PasskeysCard() {
  const { data, error, isError, isPending } = useGetPasskeys();
  const passkeys = data?.passkeys ?? [];

  return (
    <Card>
      <CardHeader>
        <CardTitle>Passkeys</CardTitle>
        <CardDescription>
          Sign in with your fingerprint, face or a security key instead of a device code.
        </CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-6">
        {isPending ? (
          <Skeleton className="h-16 w-full" />
        ) : isError ? (
          <p role="alert" className="text-sm text-destructive">
            {errorMessage(error, "The passkeys could not be loaded.")}
          </p>
        ) : passkeys.length === 0 ? (
          <p className="text-sm text-muted-foreground">No passkeys yet.</p>
        ) : (
          <ul className="flex flex-col divide-y rounded-lg border">
            {passkeys.map((passkey) => (
              <PasskeyRow key={passkey.id} passkey={passkey} />
            ))}
          </ul>
        )}
        {!isPending && !isError && <AddPasskey count={passkeys.length} />}
      </CardContent>
    </Card>
  );
}

function useRefreshPasskeys() {
  const queryClient = useQueryClient();
  return () => queryClient.invalidateQueries({ queryKey: getGetPasskeysQueryKey() });
}

type RowMode = "view" | "rename" | "remove";

function PasskeyRow({ passkey }: { passkey: Passkey }) {
  const refresh = useRefreshPasskeys();
  const [mode, setMode] = useState<RowMode>("view");
  const [name, setName] = useState(passkey.name);
  const [busy, setBusy] = useState(false);
  const [failure, setFailure] = useState<string>();

  async function run(action: () => Promise<unknown>, fallback: string) {
    setBusy(true);
    setFailure(undefined);
    try {
      await action();
      await refresh();
      setMode("view");
    } catch (error) {
      setFailure(errorMessage(error, fallback));
    } finally {
      setBusy(false);
    }
  }

  const label = passkey.name || "Unnamed passkey";

  return (
    <li className="flex flex-col gap-2 px-4 py-3">
      {mode === "rename" ? (
        <form
          className="flex flex-wrap items-center gap-2"
          onSubmit={(event) => {
            event.preventDefault();
            void run(
              () => renamePasskey(passkey.id, { name: name.trim() }),
              "The passkey was not renamed.",
            );
          }}
        >
          <Label htmlFor={`name-${passkey.id}`} className="sr-only">
            Name of {label}
          </Label>
          <Input
            id={`name-${passkey.id}`}
            className="max-w-64 flex-1"
            value={name}
            maxLength={MAXIMUM_NAME_LENGTH}
            autoFocus
            onChange={(event) => setName(event.target.value)}
          />
          <Button type="submit" size="sm" disabled={busy || name.trim().length === 0}>
            Save
          </Button>
          <Button
            type="button"
            size="sm"
            variant="ghost"
            onClick={() => {
              setName(passkey.name);
              setFailure(undefined);
              setMode("view");
            }}
          >
            Cancel
          </Button>
        </form>
      ) : (
        <div className="flex flex-wrap items-center gap-3">
          <KeyRound className="size-4 text-muted-foreground" aria-hidden />
          <div className="min-w-0 flex-1">
            <p className="truncate font-medium">{label}</p>
            <p className="text-xs text-muted-foreground">
              Added {dateFormat.format(new Date(passkey.addedAt))} · Last used{" "}
              {passkey.lastUsedAt ? dateFormat.format(new Date(passkey.lastUsedAt)) : "never"}
            </p>
          </div>
          {mode === "remove" ? (
            <div className="flex items-center gap-2">
              <span className="text-sm">Remove it?</span>
              <Button
                size="sm"
                variant="destructive"
                disabled={busy}
                onClick={() =>
                  void run(() => removePasskey(passkey.id), "The passkey was not removed.")
                }
              >
                Remove
              </Button>
              <Button size="sm" variant="ghost" onClick={() => setMode("view")}>
                Keep
              </Button>
            </div>
          ) : (
            <div className="flex items-center gap-1">
              <Button
                size="icon-sm"
                variant="ghost"
                aria-label={`Rename ${label}`}
                onClick={() => setMode("rename")}
              >
                <Pencil aria-hidden />
              </Button>
              <Button
                size="icon-sm"
                variant="ghost"
                aria-label={`Remove ${label}`}
                onClick={() => setMode("remove")}
              >
                <Trash2 aria-hidden />
              </Button>
            </div>
          )}
        </div>
      )}
      {failure && (
        <p role="alert" className="text-sm text-destructive">
          {failure}
        </p>
      )}
    </li>
  );
}

function AddPasskey({ count }: { count: number }) {
  const refresh = useRefreshPasskeys();
  // Known only in the browser; the server renders no button rather than guessing.
  const supported = useSyncExternalStore(subscribeToNothing, passkeysSupported, () => false);
  const [name, setName] = useState("");
  const [busy, setBusy] = useState(false);
  const [failure, setFailure] = useState<string>();

  if (count >= MAXIMUM_PASSKEYS) {
    return (
      <p className="text-sm text-muted-foreground">
        An account holds at most {MAXIMUM_PASSKEYS} passkeys: remove one to add another.
      </p>
    );
  }
  if (!supported) {
    return <p className="text-sm text-muted-foreground">This browser cannot create passkeys.</p>;
  }

  async function add() {
    const trimmed = name.trim();
    setBusy(true);
    setFailure(undefined);
    try {
      const { creationOptions } = await beginPasskeyRegistration({ name: trimmed });
      const credential = await createCredential(creationOptions);
      if (credential.kind === "abandoned") return;
      if (credential.kind === "failed") {
        setFailure("The browser could not create the passkey.");
        return;
      }
      await completePasskeyRegistration({ credentialJson: credential.json, name: trimmed });
      setName("");
      await refresh();
    } catch (error) {
      setFailure(errorMessage(error, "The passkey was not added."));
    } finally {
      setBusy(false);
    }
  }

  return (
    <form
      className="flex flex-col gap-2"
      onSubmit={(event) => {
        event.preventDefault();
        void add();
      }}
    >
      <Label htmlFor="passkey-name">New passkey</Label>
      <div className="flex flex-wrap gap-2">
        <Input
          id="passkey-name"
          className="max-w-64 flex-1"
          placeholder="e.g. Laptop, YubiKey"
          value={name}
          maxLength={MAXIMUM_NAME_LENGTH}
          onChange={(event) => setName(event.target.value)}
        />
        <Button type="submit" disabled={busy || name.trim().length === 0}>
          <Plus aria-hidden />
          {busy ? "Waiting for your passkey…" : "Add a passkey"}
        </Button>
      </div>
      {failure && (
        <p role="alert" className="text-sm text-destructive">
          {failure}
        </p>
      )}
    </form>
  );
}

// Whether the browser does WebAuthn never changes while the page lives.
function subscribeToNothing(): () => void {
  return () => {};
}
