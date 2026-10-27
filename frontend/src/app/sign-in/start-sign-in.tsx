"use client";

import { useEffect } from "react";
import { Button } from "@/components/ui/button";
import { signInStartUrl } from "@/lib/return-url";
import { UNREACHABLE } from "./sign-in-form";

/**
 * /sign-in reached without the authorization server asking: on to /sign-in/start, which begins the
 * code flow. With `unavailable` the flow could not start (backend down): say so, offer a retry.
 */
export function StartSignIn({
  returnUrl,
  unavailable,
}: {
  returnUrl: string;
  unavailable: boolean;
}) {
  const href = signInStartUrl(returnUrl);

  useEffect(() => {
    if (!unavailable) window.location.replace(href);
  }, [href, unavailable]);

  if (!unavailable) {
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
      <Button variant="outline" asChild>
        <a href={href}>Try again</a>
      </Button>
    </div>
  );
}
