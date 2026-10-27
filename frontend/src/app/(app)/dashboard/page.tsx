import type { Metadata } from "next";
import { DeviceCount } from "./device-count";

export const metadata: Metadata = {
  title: "Dashboard · ESP32 Authenticator",
};

export default function DashboardPage() {
  return (
    <>
      <h1 className="font-heading text-2xl font-semibold">Dashboard</h1>
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        <DeviceCount />
      </div>
    </>
  );
}
