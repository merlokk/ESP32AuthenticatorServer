"use client";

import { useEffect, useState } from "react";
import { Button } from "@/components/ui/button";
import { continueSignIn } from "./actions";
import { UNREACHABLE } from "./sign-in-form";

/**
 * Starts the code flow at once. While the authorization server's cookie lives (8 h) the browser
 * comes straight back signed in; otherwise the server sends it here again, with ?returnUrl=
 * /connect/authorize..., and the page shows the form.
 */
export function StartSignIn({ returnUrl }: { returnUrl: string }) {
  const [failed, setFailed] = useState(false);
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    let current = true;
    continueSignIn(returnUrl)
      .then((url) => current && window.location.assign(url))
      .catch(() => current && setFailed(true));
    return () => {
      current = false;
    };
  }, [returnUrl, attempt]);

  if (!failed) {
    return (
      <p role="status" className="text-sm text-muted-foreground">
        Signing you in…
      </p>
    );
  }
  return (
    <div className="flex flex-col gap-3">
      <p role="alert" className="text-sm text-destructive">
        {UNREACHABLE}
      </p>
      <Button
        variant="outline"
        onClick={() => {
          setFailed(false);
          setAttempt((value) => value + 1);
        }}
      >
        Try again
      </Button>
    </div>
  );
}
