"use client";

import { useRef, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { CircleCheck, CircleOff, Download, Trash2, Upload } from "lucide-react";
import { errorMessage } from "@/api/errors";
import { customFetch } from "@/api/fetch";
import {
  activateRelease,
  deactivateRelease,
  deleteRelease,
  getGetReleasesQueryKey,
  useGetReleases,
} from "@/api/generated/firmware/firmware";
import type { ReleaseRow, UploadReleaseResponse } from "@/api/generated/model";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import { formatBytes } from "@/lib/stores";
import { ConfirmDialog } from "../users/confirm-dialog";
import { GitHubImportDialog } from "./github-import";

const dateTimeFormat = new Intl.DateTimeFormat("en", { dateStyle: "medium", timeStyle: "short" });

type Open =
  | { kind: "activate"; release: ReleaseRow; current?: ReleaseRow }
  | { kind: "deactivate"; release: ReleaseRow }
  | { kind: "delete"; release: ReleaseRow };

// The generated uploadRelease has no board: the query is read by the endpoint, not declared.
function upload(board: string, file: File) {
  return customFetch<UploadReleaseResponse>(
    `/api/firmware/releases?board=${encodeURIComponent(board)}`,
    { method: "POST", body: file, headers: { "content-type": "application/octet-stream" } },
  );
}

export function FirmwareCard() {
  const queryClient = useQueryClient();
  const { data, error, isError, isPending } = useGetReleases();
  const [open, setOpen] = useState<Open>();
  const refresh = () => queryClient.invalidateQueries({ queryKey: getGetReleasesQueryKey() });
  const close = () => setOpen(undefined);

  if (isPending) return <Skeleton className="h-60 w-full" />;
  if (isError) {
    return (
      <p role="alert" className="text-sm text-destructive">
        {errorMessage(error, "The releases could not be loaded.")}
      </p>
    );
  }

  return (
    <>
      <UploadCard boards={data.boards} gitHubRepository={data.gitHubRepository} onDone={refresh} />
      {data.boards.map((board) => {
        const releases = data.releases.filter((release) => release.board === board.id);
        const active = releases.find((release) => release.active);
        return (
          <Card key={board.id}>
            <CardHeader>
              <CardTitle>
                {board.name}{" "}
                <span className="font-mono font-normal text-muted-foreground">({board.id})</span>
              </CardTitle>
              <CardDescription>
                {active
                  ? `Devices on another version are offered ${active.version}.`
                  : "No active release: no device is offered an update."}
              </CardDescription>
            </CardHeader>
            <CardContent>
              {releases.length === 0 ? (
                <p className="text-sm text-muted-foreground">Nothing uploaded for this board.</p>
              ) : (
                <Table className="min-w-[52rem]">
                  <TableHeader>
                    <TableRow>
                      <TableHead>Version</TableHead>
                      <TableHead>Built</TableHead>
                      <TableHead className="text-right">Size</TableHead>
                      <TableHead>SHA-256</TableHead>
                      <TableHead>Uploaded</TableHead>
                      <TableHead>Status</TableHead>
                      <TableHead className="text-right">Actions</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {releases.map((release) => (
                      <TableRow key={release.id}>
                        <TableCell className="font-mono">{release.version}</TableCell>
                        <TableCell>
                          {release.built}
                          <span className="block text-xs text-muted-foreground">{release.idf}</span>
                        </TableCell>
                        <TableCell className="text-right tabular-nums">
                          {formatBytes(release.size)}
                        </TableCell>
                        <TableCell>
                          <Tooltip>
                            <TooltipTrigger asChild>
                              <span tabIndex={0} className="font-mono text-xs">
                                {release.sha256.slice(0, 12)}…
                              </span>
                            </TooltipTrigger>
                            <TooltipContent className="font-mono">{release.sha256}</TooltipContent>
                          </Tooltip>
                        </TableCell>
                        <TableCell>{dateTimeFormat.format(new Date(release.uploadedAt))}</TableCell>
                        <TableCell>
                          {release.active ? (
                            <Badge>Active</Badge>
                          ) : (
                            <span className="text-muted-foreground">Inactive</span>
                          )}
                        </TableCell>
                        <TableCell>
                          <div className="flex justify-end gap-1">
                            {release.active ? (
                              <Button
                                variant="ghost"
                                size="sm"
                                onClick={() => setOpen({ kind: "deactivate", release })}
                              >
                                <CircleOff aria-hidden />
                                Deactivate
                              </Button>
                            ) : (
                              <Button
                                variant="ghost"
                                size="sm"
                                onClick={() =>
                                  setOpen({ kind: "activate", release, current: active })
                                }
                              >
                                <CircleCheck aria-hidden />
                                Activate
                              </Button>
                            )}
                            <Button
                              variant="ghost"
                              size="icon-sm"
                              aria-label={`Delete ${release.version}`}
                              className="text-destructive hover:text-destructive"
                              onClick={() => setOpen({ kind: "delete", release })}
                            >
                              <Trash2 aria-hidden />
                            </Button>
                          </div>
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              )}
            </CardContent>
          </Card>
        );
      })}

      {open?.kind === "activate" && (
        <ConfirmDialog
          title={`Activate ${open.release.version}?`}
          description={
            open.current
              ? `It replaces ${open.current.version}: every ${open.release.board} device on another version is offered ${open.release.version} at its next status report.`
              : `Every ${open.release.board} device on another version is offered it at its next status report.`
          }
          confirm="Activate"
          onConfirm={async () => {
            await activateRelease(open.release.id);
            await refresh();
          }}
          onClose={close}
        />
      )}
      {open?.kind === "deactivate" && (
        <ConfirmDialog
          title={`Deactivate ${open.release.version}?`}
          description={`No ${open.release.board} device is offered an update until another release is activated.`}
          confirm="Deactivate"
          onConfirm={async () => {
            await deactivateRelease(open.release.id);
            await refresh();
          }}
          onClose={close}
        />
      )}
      {open?.kind === "delete" && (
        <ConfirmDialog
          title={`Delete ${open.release.version}?`}
          description={
            open.release.active
              ? "It is active: its board then offers no update. A device downloading it gets an error and asks again."
              : "The image is removed from the server. This cannot be undone."
          }
          confirm="Delete"
          destructive
          onConfirm={async () => {
            await deleteRelease(open.release.id);
            await refresh();
          }}
          onClose={close}
        />
      )}
    </>
  );
}

function UploadCard({
  boards,
  gitHubRepository,
  onDone,
}: {
  boards: { id: string; name: string }[];
  gitHubRepository?: string | null;
  onDone: () => Promise<unknown>;
}) {
  const [board, setBoard] = useState(boards[0]?.id ?? "");
  const [file, setFile] = useState<File>();
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<{ ok: boolean; text: string }>();
  const [importing, setImporting] = useState(false);
  const input = useRef<HTMLInputElement>(null);

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    if (!file) return;
    setBusy(true);
    setResult(undefined);
    try {
      const release = await upload(board, file);
      setResult({ ok: true, text: `Uploaded ${release.version} for ${release.board}, inactive.` });
      setFile(undefined);
      if (input.current) input.current.value = "";
      await onDone();
    } catch (failure) {
      setResult({ ok: false, text: errorMessage(failure, "The image was not uploaded.") });
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>Upload a release</CardTitle>
        <CardDescription>
          The app image as idf.py builds it (build/esp32-authenticator.bin). Its version is read
          from the image; it stays inactive until activated.
        </CardDescription>
      </CardHeader>
      <CardContent>
        <form onSubmit={submit} className="flex flex-wrap items-end gap-3">
          <div className="grid gap-1.5">
            <Label htmlFor="firmware-board">Board</Label>
            <select
              id="firmware-board"
              value={board}
              onChange={(event) => setBoard(event.target.value)}
              className="h-9 rounded-md border border-input bg-transparent px-3 text-sm shadow-xs"
            >
              {boards.map((option) => (
                <option key={option.id} value={option.id}>
                  {option.name}
                </option>
              ))}
            </select>
          </div>
          <div className="grid min-w-0 gap-1.5">
            <Label htmlFor="firmware-image">Image</Label>
            <Input
              id="firmware-image"
              ref={input}
              type="file"
              accept=".bin,application/octet-stream"
              onChange={(event) => setFile(event.target.files?.[0])}
            />
          </div>
          <Button type="submit" disabled={!file || busy}>
            <Upload aria-hidden />
            {busy ? "Uploading…" : "Upload"}
          </Button>
        </form>
        {gitHubRepository && (
          <div className="mt-3 flex flex-wrap items-center gap-3">
            <Button
              variant="outline"
              disabled={busy}
              onClick={() => {
                setResult(undefined);
                setImporting(true);
              }}
            >
              <Download aria-hidden />
              Import from GitHub
            </Button>
            <span className="text-sm text-muted-foreground">
              A release image of <span className="font-mono">{gitHubRepository}</span> for the board
              above.
            </span>
          </div>
        )}
        {importing && gitHubRepository && (
          <GitHubImportDialog
            repository={gitHubRepository}
            board={boards.find((option) => option.id === board) ?? { id: board, name: board }}
            onImported={async (release) => {
              setResult({
                ok: true,
                text: `Imported ${release.version} for ${release.board}, inactive.`,
              });
              await onDone();
            }}
            onClose={() => setImporting(false)}
          />
        )}
        {result && (
          <p
            role={result.ok ? "status" : "alert"}
            className={`mt-3 text-sm ${result.ok ? "text-muted-foreground" : "text-destructive"}`}
          >
            {result.text}
          </p>
        )}
      </CardContent>
    </Card>
  );
}
