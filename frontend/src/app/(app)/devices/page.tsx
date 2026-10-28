import type { Metadata } from "next";
import { DevicesTable } from "./devices-table";
import { RegistrationCard } from "./registration-card";

// Behind the session (the layout's <Suspense>): nothing here prerenders, so instant-navigation
// validation has no segment to check.
export const instant = false;

export const metadata: Metadata = {
  title: "Devices · ESP32 Authenticator",
};

export default function DevicesPage() {
  return (
    <>
      <h1 className="font-heading text-2xl font-semibold">Devices</h1>
      <div className="flex flex-col gap-6">
        <RegistrationCard />
        <DevicesTable />
      </div>
    </>
  );
}
