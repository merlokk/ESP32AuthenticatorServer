"use client";

import { useState, type ReactNode } from "react";
import { errorMessage } from "@/api/errors";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";

/** A yes/no for an action that cannot be taken back; the refusal shows in the dialog. */
export function ConfirmDialog({
  title,
  description,
  confirm,
  destructive = false,
  onConfirm,
  onClose,
}: {
  title: string;
  description: ReactNode;
  confirm: string;
  destructive?: boolean;
  onConfirm: () => Promise<unknown>;
  onClose: () => void;
}) {
  const [busy, setBusy] = useState(false);
  const [failure, setFailure] = useState<string>();

  async function run() {
    setBusy(true);
    setFailure(undefined);
    try {
      await onConfirm();
      onClose();
    } catch (error) {
      setFailure(errorMessage(error, "That did not work."));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Dialog open onOpenChange={(open) => !open && !busy && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          <DialogDescription>{description}</DialogDescription>
        </DialogHeader>
        {failure && (
          <p role="alert" className="text-sm text-destructive">
            {failure}
          </p>
        )}
        <DialogFooter>
          <Button variant="outline" disabled={busy} onClick={onClose}>
            Cancel
          </Button>
          <Button variant={destructive ? "destructive" : "default"} disabled={busy} onClick={run}>
            {confirm}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
