"use client";

import { useForm } from "react-hook-form";
import { z } from "zod";
import { errorMessage } from "@/api/errors";
import { updateUser } from "@/api/generated/identity/identity";
import type { UserRow } from "@/api/generated/model";
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
import { USER_NAME } from "./user-name";

const schema = z.object({ userName: USER_NAME });

export function EditNameDialog({
  user,
  onDone,
  onClose,
}: {
  user: UserRow;
  onDone: () => Promise<unknown>;
  onClose: () => void;
}) {
  const form = useForm({
    resolver: zodResolver(schema),
    defaultValues: { userName: user.userName },
  });
  const { errors, isDirty, isSubmitting } = form.formState;

  // The admin's update takes the e-mail too: it is sent unchanged.
  const submit = form.handleSubmit(async ({ userName }) => {
    try {
      await updateUser(user.id, { userName, email: user.email });
    } catch (error) {
      form.setError("userName", { message: errorMessage(error, "The name was not saved.") });
      return;
    }
    await onDone();
    onClose();
  });

  return (
    <Dialog open onOpenChange={(open) => !open && !isSubmitting && onClose()}>
      <DialogContent>
        <form onSubmit={submit} noValidate className="flex flex-col gap-4">
          <DialogHeader>
            <DialogTitle>Edit user name</DialogTitle>
            <DialogDescription>
              {user.email} signs in with this address; it stays.
            </DialogDescription>
          </DialogHeader>
          <div className="flex flex-col gap-2">
            <Label htmlFor="edit-userName">User name</Label>
            <Input
              id="edit-userName"
              autoComplete="off"
              spellCheck={false}
              autoFocus
              aria-invalid={errors.userName ? true : undefined}
              aria-describedby={errors.userName ? "edit-userName-error" : undefined}
              {...form.register("userName")}
            />
            {errors.userName && (
              <p id="edit-userName-error" role="alert" className="text-sm text-destructive">
                {errors.userName.message}
              </p>
            )}
          </div>
          <DialogFooter>
            <Button type="button" variant="outline" disabled={isSubmitting} onClick={onClose}>
              Cancel
            </Button>
            <Button type="submit" disabled={!isDirty || isSubmitting}>
              {isSubmitting ? "Saving…" : "Save"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
