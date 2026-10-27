"use client";

import { useState, useSyncExternalStore } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { Plus } from "lucide-react";
import { errorMessage } from "@/api/errors";
import {
  closeMyRegistrationWindow,
  getGetProfileQueryKey,
  openMyRegistrationWindow,
} from "@/api/generated/identity/identity";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { formatMinutes, formatUntil } from "@/lib/registration-window";
import { useRegistrationWindow } from "./registration-window";

export function RegistrationCard() {
  const queryClient = useQueryClient();
  const { profile, state } = useRegistrationWindow();
  const host = useSyncExternalStore(
    subscribeToNothing,
    () => window.location.host,
    () => "",
  );
  const [busy, setBusy] = useState(false);
  const [failure, setFailure] = useState<string>();

  async function change(action: () => Promise<unknown>, fallback: string) {
    setBusy(true);
    setFailure(undefined);
    try {
      await action();
      await queryClient.invalidateQueries({ queryKey: getGetProfileQueryKey() });
    } catch (error) {
      setFailure(errorMessage(error, fallback));
    } finally {
      setBusy(false);
    }
  }

  const open = () => change(openMyRegistrationWindow, "Registration could not be opened.");
  const close = () => change(closeMyRegistrationWindow, "Registration could not be closed.");

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          Add a device
          {state.open && <Badge>Open</Badge>}
        </CardTitle>
        <CardDescription>
          A device registers to your account only while registration is open. The first device that
          registers closes it.
        </CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        {!profile.data ? (
          <Skeleton className="h-20 w-full" />
        ) : state.open ? (
          <>
            <p className="text-sm" role="status">
              Open until <span className="font-medium">{formatUntil(state.until, new Date())}</span>{" "}
              ({formatMinutes(state.minutesLeft)} left). Now, on the device, register with:
            </p>
            <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 rounded-lg border px-4 py-3 text-sm">
              <dt className="text-muted-foreground">Server</dt>
              <dd className="font-mono">{host}</dd>
              <dt className="text-muted-foreground">E-mail</dt>
              <dd className="font-mono">{profile.data.email}</dd>
            </dl>
            <div className="flex flex-wrap gap-2">
              <Button variant="outline" disabled={busy} onClick={open}>
                Extend
              </Button>
              <Button variant="ghost" disabled={busy} onClick={close}>
                Close
              </Button>
            </div>
          </>
        ) : (
          <div>
            <Button disabled={busy} onClick={open}>
              <Plus aria-hidden />
              Allow a new device
            </Button>
          </div>
        )}
        {failure && (
          <p role="alert" className="text-sm text-destructive">
            {failure}
          </p>
        )}
      </CardContent>
    </Card>
  );
}

// The page's host never changes while it lives.
function subscribeToNothing(): () => void {
  return () => {};
}
