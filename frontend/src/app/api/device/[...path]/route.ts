import { forward } from "@/lib/backend";

// /api/sync/device/** (heads, upload, download): the firmware's sync API. A device presents the
// token it got with its own key (private_key_jwt), so its Authorization passes through as is; no
// session token is attached and no cookie crosses. The backend's device policy is the check.
async function handler(request: Request): Promise<Response> {
  return forward(request, {
    forwardCookies: false,
    forwardAuthorization: true,
    redirect: "manual",
  });
}

export { handler as GET, handler as POST };
