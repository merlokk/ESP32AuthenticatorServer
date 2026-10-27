"use client";

import { useSyncExternalStore, type ReactNode } from "react";

/**
 * The fallback on the server and while hydrating, the children after. For the parts of the app
 * that read the query cache inside a streamed <Suspense>: by the time such a boundary hydrates, a
 * component outside it (the sidebar) may have filled the cache, and rendering that data would not
 * match the server's skeleton.
 */
export function ClientOnly({ children, fallback }: { children: ReactNode; fallback: ReactNode }) {
  const hydrated = useSyncExternalStore(
    subscribeToNothing,
    () => true,
    () => false,
  );
  return hydrated ? children : fallback;
}

function subscribeToNothing(): () => void {
  return () => {};
}
