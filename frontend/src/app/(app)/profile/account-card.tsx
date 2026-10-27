"use client";

import { useState } from "react";
import { useForm } from "react-hook-form";
import { useQueryClient } from "@tanstack/react-query";
import { z } from "zod";
import { errorMessage } from "@/api/errors";
import {
  getGetProfileQueryKey,
  useGetProfile,
  useUpdateProfile,
} from "@/api/generated/identity/identity";
import type { ProfileResponse } from "@/api/generated/model";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { zodResolver } from "@/lib/zod-resolver";

// The backend's rule (AccountFields.UserNameOf), checked here first so a typo needs no round trip.
const schema = z.object({
  userName: z
    .string()
    .trim()
    .regex(/^[A-Za-z0-9._-]{1,64}$/, "1 to 64 letters, digits, '.', '_' or '-'."),
});

export function AccountCard() {
  const { data: profile } = useGetProfile();

  return (
    <Card>
      <CardHeader>
        <CardTitle>Account</CardTitle>
        <CardDescription>
          You sign in with your e-mail address; only an administrator changes it.
        </CardDescription>
      </CardHeader>
      <CardContent>
        {profile ? <UserNameForm profile={profile} /> : <Skeleton className="h-36 w-full" />}
      </CardContent>
    </Card>
  );
}

function UserNameForm({ profile }: { profile: ProfileResponse }) {
  const queryClient = useQueryClient();
  const update = useUpdateProfile();
  const [saved, setSaved] = useState(false);
  const form = useForm({
    resolver: zodResolver(schema),
    defaultValues: { userName: profile.userName },
  });
  const { errors, isDirty, isSubmitting } = form.formState;

  const submit = form.handleSubmit(async ({ userName }) => {
    setSaved(false);
    try {
      await update.mutateAsync({ data: { userName } });
    } catch (error) {
      form.setError("userName", { message: errorMessage(error, "The name was not saved.") });
      return;
    }
    form.reset({ userName });
    setSaved(true);
    await queryClient.invalidateQueries({ queryKey: getGetProfileQueryKey() });
  });

  return (
    <form onSubmit={submit} noValidate className="flex flex-col gap-4">
      <div className="flex flex-col gap-2">
        <Label htmlFor="email">E-mail</Label>
        <Input id="email" value={profile.email} readOnly disabled />
      </div>
      <div className="flex flex-col gap-2">
        <Label htmlFor="userName">User name</Label>
        <Input
          id="userName"
          autoComplete="off"
          spellCheck={false}
          aria-invalid={errors.userName ? true : undefined}
          aria-describedby={errors.userName ? "userName-error" : undefined}
          {...form.register("userName", { onChange: () => setSaved(false) })}
        />
        {errors.userName && (
          <p id="userName-error" role="alert" className="text-sm text-destructive">
            {errors.userName.message}
          </p>
        )}
      </div>
      <div className="flex items-center gap-3">
        <Button type="submit" disabled={!isDirty || isSubmitting}>
          {isSubmitting ? "Saving…" : "Save"}
        </Button>
        {saved && (
          <p role="status" className="text-sm text-muted-foreground">
            Saved.
          </p>
        )}
      </div>
    </form>
  );
}
