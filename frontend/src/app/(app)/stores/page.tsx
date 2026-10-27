import type { Metadata } from "next";
import { StoresList } from "./stores-list";

// Behind the session (the layout's <Suspense>): nothing here prerenders, so instant-navigation
// validation has no segment to check.
export const instant = false;

export const metadata: Metadata = {
  title: "Stores · ESP32 Authenticator",
};

export default function StoresPage() {
  return (
    <>
      <h1 className="font-heading text-2xl font-semibold">Stores</h1>
      <p className="max-w-3xl text-sm text-muted-foreground">
        The encrypted stores your devices sync through this server. It holds them as they are and
        cannot open them; only the header below is readable. Devices share a store only under the
        same seed phrase (seed id).
      </p>
      <StoresList />
    </>
  );
}
