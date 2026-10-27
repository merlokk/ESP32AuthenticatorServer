import { forward } from "@/lib/backend";

// /api/device/** (device-protocol.md): ping, me, status, stores. A device presents the token it got
// with its own key (private_key_jwt), so its Authorization passes through as is; no session token
// is attached and no cookie crosses. The backend's device policy is the check (ping is anonymous).
async function handler(request: Request): Promise<Response> {
  return forward(request, {
    forwardCookies: false,
    forwardAuthorization: true,
    redirect: "manual",
  });
}

export { handler as GET, handler as POST };
