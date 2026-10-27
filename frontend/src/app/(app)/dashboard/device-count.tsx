"use client";

import { Cpu } from "lucide-react";
import { errorMessage } from "@/api/errors";
import { useGetMyDevices } from "@/api/generated/identity/identity";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";

export function DeviceCount() {
  const { data, error, isError, isPending } = useGetMyDevices();

  return (
    <Card>
      <CardHeader>
        <CardDescription className="flex items-center gap-2">
          <Cpu className="size-4" aria-hidden />
          Registered devices
        </CardDescription>
        <CardTitle className="text-4xl tabular-nums" aria-live="polite">
          {isPending ? <Skeleton className="h-10 w-12" /> : (data?.devices.length ?? "—")}
        </CardTitle>
      </CardHeader>
      {isError && (
        <CardContent>
          <p role="alert" className="text-sm text-destructive">
            {errorMessage(error, "The devices could not be loaded.")}
          </p>
        </CardContent>
      )}
    </Card>
  );
}
