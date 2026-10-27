"use client";

import { useTransition } from "react";
import { LogIn, LogOut } from "lucide-react";
import { Button } from "@/components/ui/button";
import { continueSignIn, signOutEverywhere } from "./sign-in/actions";

// Each action answers with the next URL on /connect/**: a full navigation, not the router.

/** Starts the code flow; the authorization server shows the sign-in page only without its cookie. */
export function SignInButton() {
  const [pending, start] = useTransition();
  return (
    <Button
      disabled={pending}
      onClick={() => start(async () => window.location.assign(await continueSignIn("/")))}
    >
      <LogIn aria-hidden />
      Sign in
    </Button>
  );
}

export function SignOutButton() {
  const [pending, start] = useTransition();
  return (
    <Button
      variant="outline"
      disabled={pending}
      onClick={() => start(async () => window.location.assign(await signOutEverywhere()))}
    >
      <LogOut aria-hidden />
      Sign out
    </Button>
  );
}
