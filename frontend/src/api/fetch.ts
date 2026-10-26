// The one fetch every generated call goes through (orval.config.ts: mutator). Paths are relative,
// so a call lands on this app's origin, and the BFF's /api/** catch-all forwards it with the token.

/** RFC 9457 Problem Details: `type` is public API, `title` and `detail` are text. */
export type ProblemDetails = {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  [extension: string]: unknown;
};

export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly problem: ProblemDetails | undefined,
  ) {
    super(problem?.title ?? `HTTP ${status}`);
    this.name = "ApiError";
  }
}

export async function customFetch<T>(url: string, init?: RequestInit): Promise<T> {
  const response = await fetch(url, { ...init, credentials: "same-origin" });

  if (!response.ok) {
    const isProblem = response.headers.get("content-type")?.includes("json") ?? false;
    const problem = isProblem ? ((await response.json()) as ProblemDetails) : undefined;
    throw new ApiError(response.status, problem);
  }

  if (response.status === 204) {
    return undefined as T;
  }
  return (await response.json()) as T;
}
