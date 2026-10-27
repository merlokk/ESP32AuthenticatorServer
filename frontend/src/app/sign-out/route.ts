import { signOut } from "@/auth";
import { publicUrl } from "@/lib/backend";

// POST /sign-out (a form): ends both sessions, Auth.js's here and the authorization server's at its
// end-session endpoint; without the second, the next sign-in would go through without a prompt.
// POST only, so a prefetch or a link scanner cannot sign anyone out; a cross-site form is refused.
export async function POST(request: Request): Promise<Response> {
  if (request.headers.get("origin") !== publicUrl(request).origin) {
    return new Response("Cross-site sign-out refused.", { status: 403 });
  }

  await signOut({ redirect: false });

  // The issuer is this app's public origin (.env.example); "/" is the registered logout URI.
  const home = new URL("/", process.env.AUTH_AUTHSERVER_ISSUER);
  return new Response(null, {
    status: 303,
    headers: {
      location: `/connect/endsession?post_logout_redirect_uri=${encodeURIComponent(home.href)}`,
      "cache-control": "no-store",
    },
  });
}
