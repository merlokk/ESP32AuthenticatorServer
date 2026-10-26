import { connection } from "next/server";

// The frontend's own probe: the Node process answers. It does not ask the backend, so a backend
// outage never restarts this pod.
export async function GET(): Promise<Response> {
  await connection();
  return new Response("Healthy", { headers: { "content-type": "text/plain" } });
}
