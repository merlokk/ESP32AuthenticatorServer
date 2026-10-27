"use client";

import { useGetVersion } from "@/api/generated/host/host";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { describeVersion, frontendVersion } from "@/lib/version";

// Both halves run from the same commit after a rollout; a difference means one has not rolled.
export function VersionsCard() {
  const backend = useGetVersion();

  return (
    <Card>
      <CardHeader>
        <CardTitle>Versions</CardTitle>
        <CardDescription>
          git describe of each part: the last release tag, commits since, and the commit.
        </CardDescription>
      </CardHeader>
      <CardContent>
        <dl className="grid grid-cols-[auto_1fr] gap-x-6 gap-y-2 text-sm">
          <dt className="text-muted-foreground">Frontend</dt>
          <dd className="font-mono">
            {describeVersion(frontendVersion.version, frontendVersion.commit)}
          </dd>
          <dt className="text-muted-foreground">Backend</dt>
          <dd className="font-mono">
            {backend.isPending ? (
              <Skeleton className="h-5 w-48" />
            ) : backend.isError ? (
              <span className="text-destructive">unavailable</span>
            ) : (
              describeVersion(backend.data.version, backend.data.commit)
            )}
          </dd>
        </dl>
      </CardContent>
    </Card>
  );
}
