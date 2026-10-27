"use client";

import { useTransition } from "react";
import { LogIn } from "lucide-react";
import { Button } from "@/components/ui/button";
import { DEFAULT_RETURN_URL } from "@/lib/return-url";
import { continueSignIn } from "./sign-in/actions";

/**
 * Starts the code flow; the authorization server shows the sign-in page only without its cookie.
 * The action answers with a URL on /connect/**: a full navigation, not the router.
 */
export function SignInButton() {
  const [pending, start] = useTransition();
  return (
    <Button
      disabled={pending}
      onClick={() =>
        start(async () => window.location.assign(await continueSignIn(DEFAULT_RETURN_URL)))
      }
    >
      <LogIn aria-hidden />
      Sign in
    </Button>
  );
}
