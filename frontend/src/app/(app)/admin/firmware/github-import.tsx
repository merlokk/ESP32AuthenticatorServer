"use client";

import { useState } from "react";
import { Download } from "lucide-react";
import { errorMessage, problemCode } from "@/api/errors";
import { importRelease, useGetGitHubReleases } from "@/api/generated/firmware/firmware";
import type { ImportReleaseResponse } from "@/api/generated/model";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Skeleton } from "@/components/ui/skeleton";
import { formatBytes } from "@/lib/stores";

const dateFormat = new Intl.DateTimeFormat("en", { dateStyle: "medium" });

// 502 from the backend is GitHub's silence, not the backend's: errorMessage would say "unreachable".
function importFailure(error: unknown, fallback: string) {
  return problemCode(error) === "firmware.github-unavailable"
    ? "GitHub did not answer as expected. Try again later."
    : errorMessage(error, fallback);
}

/** The repository's releases and their .bin files; one is fetched by the server for the board. */
export function GitHubImportDialog({
  repository,
  board,
  onImported,
  onClose,
}: {
  repository: string;
  board: { id: string; name: string };
  onImported: (release: ImportReleaseResponse) => Promise<unknown>;
  onClose: () => void;
}) {
  const { data, error, isError, isPending } = useGetGitHubReleases({
    query: { staleTime: 60_000 },
  });
  const [busy, setBusy] = useState<number>();
  const [failure, setFailure] = useState<string>();

  async function run(assetId: number) {
    setBusy(assetId);
    setFailure(undefined);
    try {
      await onImported(await importRelease({ board: board.id, assetId }));
      onClose();
    } catch (failed) {
      setFailure(importFailure(failed, "The image was not imported."));
    } finally {
      setBusy(undefined);
    }
  }

  const releases = data?.releases.filter((release) => release.assets.length > 0) ?? [];

  return (
    <Dialog open onOpenChange={(open) => !open && busy === undefined && onClose()}>
      <DialogContent className="sm:max-w-xl">
        <DialogHeader>
          <DialogTitle>Import from GitHub</DialogTitle>
          <DialogDescription>
            Release images of{" "}
            <a
              href={`https://github.com/${repository}/releases`}
              target="_blank"
              rel="noopener noreferrer"
              className="font-mono"
            >
              {repository}
            </a>{" "}
            for {board.name}. The
            server fetches the file and checks it as an upload; it stays inactive.
          </DialogDescription>
        </DialogHeader>
        {isPending ? (
          <Skeleton className="h-32 w-full" />
        ) : isError ? (
          <p role="alert" className="text-sm text-destructive">
            {importFailure(error, "The GitHub releases could not be loaded.")}
          </p>
        ) : releases.length === 0 ? (
          <p className="text-sm text-muted-foreground">No release has a .bin file.</p>
        ) : (
          <ul className="grid max-h-96 gap-3 overflow-y-auto">
            {releases.map((release) => (
              <li key={release.tag} className="grid gap-1.5 rounded-md border p-3">
                <div className="flex flex-wrap items-center gap-2">
                  <span className="font-mono font-medium">{release.name}</span>
                  {release.prerelease && <Badge variant="secondary">Pre-release</Badge>}
                  {release.publishedAt && (
                    <span className="text-sm text-muted-foreground">
                      {dateFormat.format(new Date(release.publishedAt))}
                    </span>
                  )}
                </div>
                {release.assets.map((asset) => (
                  <div key={asset.id} className="flex items-center justify-between gap-3">
                    <span className="min-w-0 truncate font-mono text-sm">
                      {asset.name}{" "}
                      <span className="text-muted-foreground">({formatBytes(asset.size)})</span>
                    </span>
                    <Button
                      size="sm"
                      variant="outline"
                      disabled={busy !== undefined}
                      onClick={() => run(asset.id)}
                    >
                      <Download aria-hidden />
                      {busy === asset.id ? "Importing…" : "Import"}
                    </Button>
                  </div>
                ))}
              </li>
            ))}
          </ul>
        )}
        {failure && (
          <p role="alert" className="text-sm text-destructive">
            {failure}
          </p>
        )}
      </DialogContent>
    </Dialog>
  );
}
