import type { Metadata } from "next";
import { AdminOnly } from "../admin-only";
import { FirmwareCard } from "./firmware-card";

// Behind the session (the layout's <Suspense>): nothing here prerenders, so instant-navigation
// validation has no segment to check.
export const instant = false;

export const metadata: Metadata = {
  title: "Firmware · ESP32 Authenticator",
};

export default function FirmwarePage() {
  return (
    <>
      <h1 className="font-heading text-2xl font-semibold">Firmware</h1>
      <AdminOnly>
        <FirmwareCard />
      </AdminOnly>
    </>
  );
}
