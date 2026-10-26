import { getAccessToken } from "@/auth";
import { forward } from "@/lib/backend";

// /api/** forwarded to the backend as data: the access token from the session attached, the
// browser's cookies kept out, path, query, body and status untouched (architecture.md §9).
async function handler(request: Request): Promise<Response> {
  return forward(request, {
    accessToken: await getAccessToken(request),
    forwardCookies: false,
    redirect: "follow",
  });
}

export { handler as GET, handler as POST, handler as PUT, handler as PATCH, handler as DELETE };
