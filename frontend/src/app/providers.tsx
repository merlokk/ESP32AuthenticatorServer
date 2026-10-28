"use client";

import { MutationCache, QueryCache, QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useState, type ReactNode } from "react";
import { ApiError } from "@/api/fetch";
import { signInStartUrl } from "@/lib/return-url";
import { TooltipProvider } from "@/components/ui/tooltip";

// A 401 means the BFF could not renew the session's access token either (the refresh token expired
// or was revoked, the account blocked). Signing in again is silent while the authorization
// server's cookie lives, so go round once more.
function signInAgainOn401(error: unknown) {
  if (error instanceof ApiError && error.status === 401) {
    const here = `${window.location.pathname}${window.location.search}`;
    // A full load, not the router: it drops every cached query of the old session.
    window.location.assign(new URL(signInStartUrl(here), window.location.origin));
  }
}

export function Providers({ children }: { children: ReactNode }) {
  // One client per browser session, created once: a module-level client would be shared between
  // users on the server.
  const [queryClient] = useState(
    () =>
      new QueryClient({
        queryCache: new QueryCache({ onError: signInAgainOn401 }),
        mutationCache: new MutationCache({ onError: signInAgainOn401 }),
        defaultOptions: {
          queries: {
            staleTime: 30_000,
            retry: (failures, error) =>
              !(error instanceof ApiError && error.status < 500) && failures < 1,
          },
        },
      }),
  );

  return (
    <QueryClientProvider client={queryClient}>
      <TooltipProvider>{children}</TooltipProvider>
    </QueryClientProvider>
  );
}
