"use client";

import { useEffect } from "react";
import Link from "next/link";
import { usePathname, useSearchParams } from "next/navigation";

// To /sign-in with the page asked for. Here rather than in the layout: a layout does not know the
// path. A full navigation, as /sign-in starts the code flow through /connect/**.
export function SignInRedirect() {
  const pathname = usePathname();
  const query = useSearchParams().toString();
  const href = `/sign-in?returnUrl=${encodeURIComponent(query ? `${pathname}?${query}` : pathname)}`;

  useEffect(() => {
    window.location.replace(href);
  }, [href]);

  return (
    <p role="status" className="text-sm text-muted-foreground">
      Taking you to sign in… <Link href={href}>Sign in</Link>
    </p>
  );
}
