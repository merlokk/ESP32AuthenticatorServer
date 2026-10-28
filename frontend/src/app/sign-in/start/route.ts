import { redirect } from "next/navigation";
import { AUTH_BASE_PATH, signIn } from "@/auth";
import { isAuthorizeUrl, safeReturnUrl } from "@/lib/return-url";
import { readSession, renewedSessionCookies, renewSession } from "@/lib/session";

// GET /sign-in/start?returnUrl=<path>: starts the authorization-code flow and sends the browser to
// /connect/authorize. While the authorization server's cookie lives (8 h) it comes straight back
// signed in; without it the server sends it to /sign-in?returnUrl=/connect/authorize..., the form.
// A session that renews needs no flow at all.
//
// A GET is safe here: the flow can only end in the session of whoever's browser holds the
// authorization server's cookie, never in someone else's.
export async function GET(request: Request): Promise<Response> {
  const target = safeReturnUrl(new URL(request.url).searchParams.get("returnUrl"));

  // Back into a flow the backend interrupted.
  if (isAuthorizeUrl(target)) redirect(target);

  // Signed in already: renew rather than trust the session. A page comes here after a 401, and a
  // session whose token the backend refuses (it restarted) would otherwise send it straight back.
  const session = await readSession(request);
  const renewed = session ? await renewSession(session) : undefined;
  if (renewed) {
    const headers = new Headers({ location: target, "cache-control": "no-store" });
    for (const cookie of await renewedSessionCookies(request, renewed)) headers.append("set-cookie", cookie);
    return new Response(null, { status: 303, headers });
  }

  const url = (await signIn("authserver", { redirectTo: target, redirect: false })) as string;

  // Auth.js answers its own error page, rather than throwing, when discovery fails (backend down).
  if (new URL(url, "http://origin.invalid").pathname.startsWith(`${AUTH_BASE_PATH}/error`)) {
    redirect(`/sign-in?unavailable=1&returnUrl=${encodeURIComponent(target)}`);
  }
  redirect(url);
}
