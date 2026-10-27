// This build's version and commit, fixed at build time by next.config.ts (git describe).
export const frontendVersion = {
  version: process.env.APP_VERSION ?? "0.0.0",
  commit: process.env.APP_COMMIT || null,
};

/** "0.5.0-7-gcb9915d (cb9915d)" or the version alone. */
export function describeVersion(version: string, commit: string | null | undefined): string {
  return commit ? `${version} (${commit.slice(0, 7)})` : version;
}
