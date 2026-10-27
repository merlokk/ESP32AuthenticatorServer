import { execSync } from "node:child_process";
import type { NextConfig } from "next";

// The product version is git's, as for the backend (architecture.md §4.5): VERSION and COMMIT from
// the Docker build (CI passes them), else git itself, else nothing. Fixed at build time.
function git(command: string): string | undefined {
  try {
    return execSync(`git ${command}`, { stdio: ["ignore", "pipe", "ignore"] }).toString().trim();
  } catch {
    return undefined;
  }
}

const version = process.env.VERSION || git("describe --tags --match=* --long") || "0.0.0";
const commit = process.env.COMMIT || git("rev-parse HEAD") || "";

const nextConfig: NextConfig = {
  env: { APP_VERSION: version, APP_COMMIT: commit },
  // The Docker image runs .next/standalone/server.js: no node_modules in the image.
  output: "standalone",
  cacheComponents: true,
  partialPrefetching: true,
  // The BFF's /api/** catch-all forwards the backend's paths as they are; a trailing-slash
  // redirect would change them.
  skipTrailingSlashRedirect: true,
  turbopack: {
    rules: {
      "*.css": {
        loaders: ["@tailwindcss/turbopack"],
        as: "*.css",
      },
    },
  },
};

export default nextConfig;
