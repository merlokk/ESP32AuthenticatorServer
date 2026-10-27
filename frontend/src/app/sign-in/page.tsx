import type { Metadata } from "next";
import { Suspense } from "react";
import { redirect } from "next/navigation";
import { connection } from "next/server";
import { auth } from "@/auth";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { isAuthorizeUrl, safeReturnUrl } from "@/lib/return-url";
import { SignInForm } from "./sign-in-form";

export const metadata: Metadata = {
  title: "Sign in · ESP32 Authenticator",
};

// The backend's /connect/authorize sends a browser without its cookie here, with
// ?returnUrl=/connect/authorize?...; a link of this app may send one with a path of its own.
export default function SignInPage({ searchParams }: PageProps<"/sign-in">) {
  return (
    <main className="flex flex-1 items-center justify-center px-4 py-16">
      <Card className="w-full max-w-sm">
        <CardHeader>
          <CardTitle>
            <h1 className="font-heading text-xl">Sign in</h1>
          </CardTitle>
          <CardDescription>
            With a passkey, or with your e-mail and the code one of your devices shows.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <Suspense>
            <SignIn searchParams={searchParams} />
          </Suspense>
        </CardContent>
      </Card>
    </main>
  );
}

async function SignIn({ searchParams }: Pick<PageProps<"/sign-in">, "searchParams">) {
  const { returnUrl } = await searchParams;
  const target = safeReturnUrl(typeof returnUrl === "string" ? returnUrl : undefined);

  // Signed in here already: nothing to do, unless the authorization server asked (its own cookie
  // is gone, which this app's session cannot tell).
  // Auth.js draws random bytes (jose) before it reads the cookie: request time, not prerender.
  await connection();
  const session = await auth();
  if (session && !session.error && !isAuthorizeUrl(target)) {
    redirect(target);
  }

  return <SignInForm returnUrl={target} />;
}
