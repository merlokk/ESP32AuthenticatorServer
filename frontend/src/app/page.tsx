import { Suspense } from "react";
import { connection } from "next/server";
import { ShieldCheck } from "lucide-react";
import { backendUrl } from "@/lib/backend";

export default function Home() {
  return (
    <main className="mx-auto flex w-full max-w-2xl flex-1 flex-col gap-6 px-6 py-16">
      <div className="flex items-center gap-3">
        <ShieldCheck className="size-8" aria-hidden />
        <h1 className="font-heading text-2xl font-semibold">ESP32 Authenticator</h1>
      </div>
      <p className="text-muted-foreground">
        Encrypted sync for your authenticator devices. The server never sees your PIN, your seed
        phrase or your passwords.
      </p>
      <Suspense fallback={<p className="text-sm text-muted-foreground">Backend: …</p>}>
        <BackendVersion />
      </Suspense>
    </main>
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
