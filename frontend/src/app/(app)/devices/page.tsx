import type { Metadata } from "next";
import { DevicesTable } from "./devices-table";
import { RegistrationCard } from "./registration-card";

export const metadata: Metadata = {
  title: "Devices · ESP32 Authenticator",
};

export default function DevicesPage() {
  return (
    <>
      <h1 className="font-heading text-2xl font-semibold">Devices</h1>
      <div className="flex max-w-4xl flex-col gap-6">
        <RegistrationCard />
        <DevicesTable />
      </div>
    </>
  );
}
