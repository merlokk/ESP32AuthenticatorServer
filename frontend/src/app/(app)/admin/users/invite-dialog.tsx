"use client";

import { useState } from "react";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { errorMessage, problemCode } from "@/api/errors";
import { inviteUser } from "@/api/generated/identity/identity";
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
import { EMAIL, USER_NAME, userNameFor } from "./user-name";

const schema = z.object({ email: EMAIL, userName: USER_NAME });

const timeFormat = new Intl.DateTimeFormat("en", { dateStyle: "medium", timeStyle: "short" });

/**
 * An invitation is an account with no device and an open registration window (24 h): the person
 * registers their device with this address before it closes.
 */
export function InviteDialog({
  onDone,
  onClose,
}: {
  onDone: () => Promise<unknown>;
  onClose: () => void;
}) {
  const [invited, setInvited] = useState<{ email: string; until: string }>();
  // The user name follows the address until the admin types one of their own.
  const [nameTouched, setNameTouched] = useState(false);
  const form = useForm({
    resolver: zodResolver(schema),
    defaultValues: { email: "", userName: "" },
  });
  const { errors, isSubmitting } = form.formState;
  const email = form.register("email", {
    onChange: (event: React.ChangeEvent<HTMLInputElement>) => {
      if (!nameTouched) form.setValue("userName", userNameFor(event.target.value));
    },
  });

  const submit = form.handleSubmit(async (values) => {
    try {
      const result = await inviteUser({ ...values, role: "user" });
      setInvited({ email: values.email, until: result.registrationOpenUntil });
      await onDone();
    } catch (error) {
      const code = problemCode(error);
      if (code === "user.email-taken") {
        form.setError("email", { message: "A user with this e-mail already exists." });
      } else if (code === "user.name-taken") {
        form.setError("userName", { message: "That user name is taken: choose another." });
      } else {
        form.setError("root", { message: errorMessage(error, "The invitation was not sent.") });
      }
    }
  });

  return (
    <Dialog open onOpenChange={(open) => !open && !isSubmitting && onClose()}>
      <DialogContent>
        {invited ? (
          <>
            <DialogHeader>
              <DialogTitle>Invited</DialogTitle>
              <DialogDescription>
                {invited.email} can register a device until{" "}
                {timeFormat.format(new Date(invited.until))}: on the device, this server and this
                e-mail. Then they sign in with the device&apos;s code.
              </DialogDescription>
            </DialogHeader>
            <DialogFooter>
              <Button onClick={onClose}>Done</Button>
            </DialogFooter>
          </>
        ) : (
          <form onSubmit={submit} noValidate className="flex flex-col gap-4">
            <DialogHeader>
              <DialogTitle>Invite a user</DialogTitle>
              <DialogDescription>
                They get 24 hours to register a device with this e-mail.
              </DialogDescription>
            </DialogHeader>
            <div className="flex flex-col gap-2">
              <Label htmlFor="invite-email">E-mail</Label>
              <Input
                id="invite-email"
                type="email"
                autoComplete="off"
                spellCheck={false}
                autoFocus
                aria-invalid={errors.email ? true : undefined}
                aria-describedby={errors.email ? "invite-email-error" : undefined}
                {...email}
              />
              {errors.email && (
                <p id="invite-email-error" role="alert" className="text-sm text-destructive">
                  {errors.email.message}
                </p>
              )}
            </div>
            <div className="flex flex-col gap-2">
              <Label htmlFor="invite-userName">User name</Label>
              <Input
                id="invite-userName"
                autoComplete="off"
                spellCheck={false}
                aria-invalid={errors.userName ? true : undefined}
                aria-describedby={errors.userName ? "invite-userName-error" : undefined}
                {...form.register("userName", { onChange: () => setNameTouched(true) })}
              />
              {errors.userName && (
                <p id="invite-userName-error" role="alert" className="text-sm text-destructive">
                  {errors.userName.message}
                </p>
              )}
            </div>
            {errors.root && (
              <p role="alert" className="text-sm text-destructive">
                {errors.root.message}
              </p>
            )}
            <DialogFooter>
              <Button type="button" variant="outline" disabled={isSubmitting} onClick={onClose}>
                Cancel
              </Button>
              <Button type="submit" disabled={isSubmitting}>
                {isSubmitting ? "Inviting…" : "Invite"}
              </Button>
            </DialogFooter>
          </form>
        )}
      </DialogContent>
    </Dialog>
  );
}
