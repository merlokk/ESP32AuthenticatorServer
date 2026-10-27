"use client";

import { MutationCache, QueryCache, QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useState, type ReactNode } from "react";
import { ApiError } from "@/api/fetch";
import { TooltipProvider } from "@/components/ui/tooltip";

// A 401 means the session's access token is gone (expired: there is no refresh yet). Signing in
// again is silent while the authorization server's cookie lives, so go round once more.
function signInAgainOn401(error: unknown) {
  if (error instanceof ApiError && error.status === 401) {
    const here = `${window.location.pathname}${window.location.search}`;
    // A full load, not the router: it drops every cached query of the old session.
    const signIn = new URL("/sign-in", window.location.origin);
    signIn.searchParams.set("returnUrl", here);
    window.location.assign(signIn);
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
