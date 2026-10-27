import { getAccessToken } from "@/auth";
import { forward } from "@/lib/backend";

// /api/identity/me/passkeys/** (list, options, register, rename, remove): the token attached as on
// the rest of /api/**, and cookies crossing both ways, because the registration ceremony keeps its
// state in the backend's cookie between the options and the credential.
async function handler(request: Request): Promise<Response> {
  return forward(request, {
    accessToken: await getAccessToken(request),
    forwardCookies: true,
    redirect: "follow",
  });
}

export { handler as GET, handler as POST, handler as PUT, handler as DELETE };
