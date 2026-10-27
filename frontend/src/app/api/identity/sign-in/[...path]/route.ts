import { forward } from "@/lib/backend";

// /api/identity/sign-in/** (TOTP, passkey options and assertion): the backend's answer is a
// Set-Cookie for its own authorization server, and the passkey ceremony keeps its state in a cookie
// between the two calls. So, unlike the rest of /api/**, cookies cross in both directions here. No
// token is attached: nobody is signed in yet.
async function handler(request: Request): Promise<Response> {
  return forward(request, { forwardCookies: true, redirect: "manual" });
}

export { handler as POST };
