import { redirect } from "next/navigation";
import { AUTH_BASE_PATH, auth, signIn } from "@/auth";
import { isAuthorizeUrl, safeReturnUrl } from "@/lib/return-url";

// GET /sign-in/start?returnUrl=<path>: starts the authorization-code flow and sends the browser to
// /connect/authorize. While the authorization server's cookie lives (8 h) it comes straight back
// signed in; without it the server sends it to /sign-in?returnUrl=/connect/authorize..., the form.
//
// A GET is safe here: the flow can only end in the session of whoever's browser holds the
// authorization server's cookie, never in someone else's.
export async function GET(request: Request): Promise<Response> {
  const target = safeReturnUrl(new URL(request.url).searchParams.get("returnUrl"));

  // Back into a flow the backend interrupted, or nothing to do.
  if (isAuthorizeUrl(target)) redirect(target);
  const session = await auth();
  if (session && !session.error) redirect(target);

  const url = (await signIn("authserver", { redirectTo: target, redirect: false })) as string;

  // Auth.js answers its own error page, rather than throwing, when discovery fails (backend down).
  if (new URL(url, "http://origin.invalid").pathname.startsWith(`${AUTH_BASE_PATH}/error`)) {
    redirect(`/sign-in?unavailable=1&returnUrl=${encodeURIComponent(target)}`);
  }
  redirect(url);
}
