import { Suspense } from "react";
import { connection } from "next/server";
import { ShieldCheck } from "lucide-react";
import { redirect } from "next/navigation";
import { auth } from "@/auth";
import { backendUrl } from "@/lib/backend";
import { DEFAULT_RETURN_URL } from "@/lib/return-url";
import { SignInButton } from "./sign-in-button";

const FIRMWARE_URL = "https://github.com/merlokk/ESP32Authenticator";
const DEVICE_URL = "https://www.xteink.com/products/xteink-x4-pro-pocket-ereader";
const LINK = "font-medium text-foreground underline underline-offset-4 hover:text-primary";

export default function Home() {
  return (
    <main className="mx-auto flex w-full max-w-2xl flex-1 flex-col gap-6 px-6 py-16">
      <div className="flex items-center gap-3">
        <ShieldCheck className="size-8" aria-hidden />
        <h1 className="font-heading text-2xl font-semibold">ESP32 Authenticator</h1>
      </div>
      <p className="text-muted-foreground">
        <a href={FIRMWARE_URL} className={LINK}>
          ESP32 Authenticator
        </a>{" "}
        is third-party firmware that turns the{" "}
        <a href={DEVICE_URL} className={LINK}>
          Xteink X4 Pro
        </a>{" "}
        pocket e-reader into a hardware authenticator: TOTP codes, passwords and FIDO2 passkeys.
      </p>
      <p className="text-muted-foreground">
        This server keeps your secrets in sync between your devices. It stores them encrypted and
        never sees your PIN, your seed phrase or your passwords.
      </p>
      <p className="text-muted-foreground">
        To sign in here, install the firmware on your device and ask the administrator of this
        server for an invitation.
      </p>
      <Suspense>
        <Account />
      </Suspense>
      <Suspense fallback={<p className="text-sm text-muted-foreground">Backend: …</p>}>
        <BackendVersion />
      </Suspense>
    </main>
  );
}

// Signed in: straight to the app. Otherwise the way in.
async function Account() {
  // Auth.js draws random bytes (jose) before it reads the cookie: request time, not prerender.
  await connection();
  const session = await auth();
  if (session && !session.error) {
    redirect(DEFAULT_RETURN_URL);
  }
  return (
    <div>
      <SignInButton />
    </div>
  );
}

// Which backend build answers, read server-side at request time.
async function BackendVersion() {
  await connection();
  let text: string;
  try {
    const response = await fetch(`${backendUrl()}/api/version`, { cache: "no-store" });
    const body = (await response.json()) as { version: string; commit: string | null };
    text = `${body.version}${body.commit ? ` (${body.commit.slice(0, 7)})` : ""}`;
  } catch {
    text = "unavailable";
  }
  return <p className="text-sm text-muted-foreground">Backend: {text}</p>;
}
