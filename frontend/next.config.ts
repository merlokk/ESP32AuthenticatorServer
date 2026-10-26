import type { NextConfig } from "next";

const nextConfig: NextConfig = {
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
