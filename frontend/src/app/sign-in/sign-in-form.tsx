"use client";

import { useState, useSyncExternalStore } from "react";
import { useForm } from "react-hook-form";
import { KeyRound } from "lucide-react";
import { z } from "zod";
import { failureKind } from "@/api/errors";
import {
  beginPasskeySignIn,
  completePasskeySignIn,
  signInWithTotp,
} from "@/api/generated/identity/identity";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { zodResolver } from "@/lib/zod-resolver";
import { getAssertion, passkeysSupported } from "@/lib/webauthn";
import { continueSignIn } from "./actions";

// One message for every refusal (the backend sends one answer for every cause), and a different
// one when the backend could not be reached: that is not a wrong code.
export const REFUSED = "That did not work. Check the address and the code.";
export const UNREACHABLE = "The server could not be reached. Try again in a moment.";

const schema = z.object({
  email: z.string().trim().pipe(z.email("Enter your e-mail address.")),
  code: z
    .string()
    .trim()
    .regex(/^\d{6}$/, "Enter the 6-digit code your device shows."),
});

type Status =
  | { kind: "idle" }
  | { kind: "busy"; method: "passkey" | "totp" }
  | { kind: "failed"; message: string };

export function SignInForm({ returnUrl }: { returnUrl: string }) {
  const [status, setStatus] = useState<Status>({ kind: "idle" });
  // Known only in the browser; the server renders no passkey button rather than guessing.
  const passkeys = useSyncExternalStore(subscribeToNothing, passkeysSupported, () => false);
  const form = useForm({
    resolver: zodResolver(schema),
    defaultValues: { email: "", code: "" },
  });
  const busy = status.kind === "busy";

  function fail(error: unknown) {
    setStatus({
      kind: "failed",
      message: failureKind(error) === "refused" ? REFUSED : UNREACHABLE,
    });
  }

  // The authorization server's cookie is set: on to the code flow, by a full navigation.
  async function proceed() {
    try {
      window.location.assign(await continueSignIn(returnUrl));
    } catch (error) {
      fail(error);
    }
  }

  async function signInWithPasskey() {
    setStatus({ kind: "busy", method: "passkey" });
    let requestOptions: string;
    try {
      ({ requestOptions } = await beginPasskeySignIn());
    } catch (error) {
      fail(error);
      return;
    }

    const assertion = await getAssertion(requestOptions);
    if (assertion.kind === "abandoned") {
      setStatus({ kind: "idle" });
      return;
    }
    if (assertion.kind === "failed") {
      setStatus({ kind: "failed", message: REFUSED });
      return;
    }

    try {
      await completePasskeySignIn({ credentialJson: assertion.json });
    } catch (error) {
      fail(error);
      return;
    }
    await proceed();
  }

  const submit = form.handleSubmit(async (values) => {
    setStatus({ kind: "busy", method: "totp" });
    try {
      await signInWithTotp(values);
    } catch (error) {
      fail(error);
      form.resetField("code");
      return;
    }
    await proceed();
  });

  const { errors } = form.formState;

  return (
    <div className="flex flex-col gap-6">
      {status.kind === "failed" && (
        <p role="alert" className="rounded-lg bg-destructive/10 px-3 py-2 text-sm text-destructive">
          {status.message}
        </p>
      )}

      {passkeys && (
        <>
          <Button type="button" size="lg" disabled={busy} onClick={signInWithPasskey}>
            <KeyRound aria-hidden />
            {status.kind === "busy" && status.method === "passkey"
              ? "Waiting for your passkey…"
              : "Sign in with a passkey"}
          </Button>
          <div className="flex items-center gap-3 text-xs text-muted-foreground" aria-hidden>
            <span className="h-px flex-1 bg-border" />
            or
            <span className="h-px flex-1 bg-border" />
          </div>
        </>
      )}

      <form onSubmit={submit} noValidate className="flex flex-col gap-4">
        <div className="flex flex-col gap-2">
          <Label htmlFor="email">E-mail</Label>
          <Input
            id="email"
            type="email"
            autoComplete="username"
            spellCheck={false}
            aria-invalid={errors.email ? true : undefined}
            aria-describedby={errors.email ? "email-error" : undefined}
            {...form.register("email")}
          />
          {errors.email && (
            <p id="email-error" className="text-sm text-destructive">
              {errors.email.message}
            </p>
          )}
        </div>
        <div className="flex flex-col gap-2">
          <Label htmlFor="code">Code from your device</Label>
          <Input
            id="code"
            inputMode="numeric"
            autoComplete="one-time-code"
            maxLength={6}
            aria-invalid={errors.code ? true : undefined}
            aria-describedby={errors.code ? "code-error" : undefined}
            {...form.register("code")}
          />
          {errors.code && (
            <p id="code-error" className="text-sm text-destructive">
              {errors.code.message}
            </p>
          )}
        </div>
        <Button type="submit" size="lg" variant={passkeys ? "outline" : "default"} disabled={busy}>
          {status.kind === "busy" && status.method === "totp" ? "Signing in…" : "Sign in"}
        </Button>
      </form>
    </div>
  );
}

// Whether the browser does WebAuthn never changes while the page lives.
function subscribeToNothing(): () => void {
  return () => {};
}
