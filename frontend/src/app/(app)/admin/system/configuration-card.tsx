"use client";

import type { ReactNode } from "react";
import { errorMessage } from "@/api/errors";
import { useGetConfiguration } from "@/api/generated/identity/identity";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { formatDuration } from "@/lib/duration";

// What the server runs with, read-only: it comes from appsettings and the environment, so each row
// names its key (an environment variable is the key with __ for :).
export function ConfigurationCard() {
  const { data, error, isError, isPending } = useGetConfiguration();

  return (
    <Card>
      <CardHeader>
        <CardTitle>Configuration</CardTitle>
        <CardDescription>
          Read-only. Change a value in the server&apos;s environment (the key with __ for :) and
          roll it out.
        </CardDescription>
      </CardHeader>
      <CardContent>
        {isPending ? (
          <Skeleton className="h-48 w-full" />
        ) : isError ? (
          <p role="alert" className="text-sm text-destructive">
            {errorMessage(error, "The configuration could not be loaded.")}
          </p>
        ) : (
          <dl className="flex flex-col divide-y text-sm">
            <Row label="Public origin" settingKey="Identity:PublicOrigin">
              <span className="font-mono">{data.publicOrigin || "—"}</span>
            </Row>
            <Row label="OIDC client" settingKey="Identity:ClientId">
              <span className="font-mono">{data.clientId}</span>
            </Row>
            <Row label="Access token lifetime" settingKey="Identity:AccessTokenLifetime">
              {formatDuration(data.accessTokenLifetimeSeconds)}
            </Row>
            <Row label="Refresh token lifetime" settingKey="Identity:RefreshTokenLifetime">
              {formatDuration(data.refreshTokenLifetimeSeconds)}
            </Row>
            <Row label="Invitation window" settingKey="Identity:InviteWindow">
              {formatDuration(data.inviteWindowSeconds)}
            </Row>
            <Row label="New device window" settingKey="Identity:DeviceWindow">
              {formatDuration(data.deviceWindowSeconds)}
            </Row>
            <Row label="Latest firmware (offered to devices)" settingKey="Identity:LatestFirmware">
              <span className="font-mono">{data.latestFirmware || "— (none offered)"}</span>
            </Row>
            <Row label="TOTP encryption key" settingKey="Identity:TotpEncryptionKey">
              {data.totpEncryptionKeySet ? (
                <Badge variant="secondary">set</Badge>
              ) : (
                <Badge variant="destructive">not set</Badge>
              )}
            </Row>
          </dl>
        )}
      </CardContent>
    </Card>
  );
}

function Row({
  label,
  settingKey,
  children,
}: {
  label: string;
  settingKey: string;
  children: ReactNode;
}) {
  return (
    <div className="flex flex-wrap items-baseline justify-between gap-x-6 gap-y-1 py-2.5">
      <dt>
        <span>{label}</span>
        <span className="block font-mono text-xs text-muted-foreground">{settingKey}</span>
      </dt>
      <dd className="text-right">{children}</dd>
    </div>
  );
}
