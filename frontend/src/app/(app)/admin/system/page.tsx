import type { Metadata } from "next";
import { AdminOnly } from "../admin-only";
import { VersionsCard } from "./versions-card";

export const metadata: Metadata = {
  title: "System · ESP32 Authenticator",
};

// The installation itself. Versions today; its configuration comes here later.
export default function SystemPage() {
  return (
    <>
      <h1 className="font-heading text-2xl font-semibold">System</h1>
      <AdminOnly>
        <div className="flex max-w-3xl flex-col gap-6">
          <VersionsCard />
        </div>
      </AdminOnly>
    </>
  );
}
