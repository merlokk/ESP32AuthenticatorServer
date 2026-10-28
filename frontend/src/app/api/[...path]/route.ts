import { forwardWithSession } from "@/lib/session";

// /api/** forwarded to the backend as data: the access token from the session attached (renewed
// when it has expired or the backend refuses it), the browser's cookies kept out, path, query,
// body and status untouched (architecture.md §9).
async function handler(request: Request): Promise<Response> {
  return forwardWithSession(request, {
    forwardCookies: false,
    redirect: "follow",
  });
}

export { handler as GET, handler as POST, handler as PUT, handler as PATCH, handler as DELETE };
