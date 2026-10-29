"use client";

import { useForm } from "react-hook-form";
import { z } from "zod";
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
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { zodResolver } from "@/lib/zod-resolver";

// The backend's rule (AccountFields.DeviceNameOf): empty clears the name.
const schema = z.object({
  name: z
    .string()
    .trim()
    .max(64, "At most 64 characters.")
    .regex(/^[^\p{Cc}]*$/u, "No control characters."),
});

/** A device's name, for its owner or an admin: `save` sends it. */
export function RenameDeviceDialog({
  device,
  save,
  onClose,
}: {
  device: { serial: string; name?: string | null };
  save: (name: string) => Promise<unknown>;
  onClose: () => void;
}) {
  const form = useForm({
    resolver: zodResolver(schema),
    defaultValues: { name: device.name ?? "" },
  });
  const { errors, isDirty, isSubmitting } = form.formState;

  const submit = form.handleSubmit(async ({ name }) => {
    try {
      await save(name);
    } catch (error) {
      form.setError("name", { message: errorMessage(error, "The name was not saved.") });
      return;
    }
    onClose();
  });

  return (
    <Dialog open onOpenChange={(open) => !open && !isSubmitting && onClose()}>
      <DialogContent>
        <form onSubmit={submit} noValidate className="flex flex-col gap-4">
          <DialogHeader>
            <DialogTitle>Name device</DialogTitle>
            <DialogDescription>
              Serial <span className="font-mono">{device.serial}</span>. Leave it empty to show the
              serial instead.
            </DialogDescription>
          </DialogHeader>
          <div className="flex flex-col gap-2">
            <Label htmlFor="device-name">Name</Label>
            <Input
              id="device-name"
              autoComplete="off"
              autoFocus
              placeholder="Kitchen X4"
              aria-invalid={errors.name ? true : undefined}
              aria-describedby={errors.name ? "device-name-error" : undefined}
              {...form.register("name")}
            />
            {errors.name && (
              <p id="device-name-error" role="alert" className="text-sm text-destructive">
                {errors.name.message}
              </p>
            )}
          </div>
          <DialogFooter>
            <Button type="button" variant="outline" disabled={isSubmitting} onClick={onClose}>
              Cancel
            </Button>
            <Button type="submit" disabled={!isDirty || isSubmitting}>
              Save
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
