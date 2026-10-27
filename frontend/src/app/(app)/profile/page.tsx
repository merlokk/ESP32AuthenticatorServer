import type { Metadata } from "next";
import { AccountCard } from "./account-card";
import { PasskeysCard } from "./passkeys-card";

// Behind the session (the layout's <Suspense>): nothing here prerenders, so instant-navigation
// validation has no segment to check.
export const instant = false;

export const metadata: Metadata = {
  title: "Profile · ESP32 Authenticator",
};

export default function ProfilePage() {
  return (
    <>
      <h1 className="font-heading text-2xl font-semibold">Profile</h1>
      <div className="flex max-w-3xl flex-col gap-6">
        <AccountCard />
        <PasskeysCard />
      </div>
    </>
  );
}
