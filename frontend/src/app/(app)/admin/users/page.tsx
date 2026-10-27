import type { Metadata } from "next";
import { AdminOnly } from "../admin-only";
import { UsersCard } from "./users-card";

// Behind the session (the layout's <Suspense>): nothing here prerenders, so instant-navigation
// validation has no segment to check.
export const instant = false;

export const metadata: Metadata = {
  title: "Users · ESP32 Authenticator",
};

export default function UsersPage() {
  return (
    <>
      <h1 className="font-heading text-2xl font-semibold">Users</h1>
      <AdminOnly>
        <UsersCard />
      </AdminOnly>
    </>
  );
}
