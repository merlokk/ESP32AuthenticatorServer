import type { Metadata } from "next";
import { DeviceCount } from "./device-count";
import { RecentStores } from "./recent-stores";

// Behind the session (the layout's <Suspense>): nothing here prerenders, so instant-navigation
// validation has no segment to check.
export const instant = false;

export const metadata: Metadata = {
  title: "Dashboard · ESP32 Authenticator",
};

export default function DashboardPage() {
  return (
    <>
      <h1 className="font-heading text-2xl font-semibold">Dashboard</h1>
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        <DeviceCount />
        <RecentStores />
      </div>
    </>
  );
}
