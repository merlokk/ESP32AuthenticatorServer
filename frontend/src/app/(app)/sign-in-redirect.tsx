"use client";

import { useEffect } from "react";
import { usePathname, useSearchParams } from "next/navigation";
import { signInStartUrl } from "@/lib/return-url";

// To sign in, with the page asked for. Here rather than in the layout: a layout does not know the
// path. A full navigation: /sign-in/start is a route that goes on to /connect/**.
export function SignInRedirect() {
  const pathname = usePathname();
  const query = useSearchParams().toString();
  const href = signInStartUrl(query ? `${pathname}?${query}` : pathname);

  useEffect(() => {
    window.location.replace(href);
  }, [href]);

  return (
    <p role="status" className="text-sm text-muted-foreground">
      Taking you to sign in… <a href={href}>Sign in</a>
    </p>
  );
}
