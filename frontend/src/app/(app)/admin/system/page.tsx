import type { Metadata } from "next";
import { AdminOnly } from "../admin-only";
import { ConfigurationCard } from "./configuration-card";
import { VersionsCard } from "./versions-card";

// Behind the session (the layout's <Suspense>): nothing here prerenders, so instant-navigation
// validation has no segment to check.
export const instant = false;

export const metadata: Metadata = {
  title: "System · ESP32 Authenticator",
};

// The installation itself: versions and the configuration it runs with.
export default function SystemPage() {
  return (
    <>
      <h1 className="font-heading text-2xl font-semibold">System</h1>
      <AdminOnly>
        <div className="flex max-w-3xl flex-col gap-6">
          <VersionsCard />
          <ConfigurationCard />
        </div>
      </AdminOnly>
    </>
  );
}
