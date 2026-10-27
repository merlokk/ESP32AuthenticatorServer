import { defineConfig } from "orval";

// The backend's OpenAPI document → TanStack Query hooks and zod schemas (architecture.md §5, §9).
// Generated code is never edited; hand-written code next to it imports from it.
//
// Runs once the backend serves the document (FastEndpoints.Swagger, wired with the first module):
//   npm run api:generate
const input = `${process.env.BACKEND_URL ?? "http://localhost:5080"}/swagger/v1/swagger.json`;

export default defineConfig({
  api: {
    input,
    output: {
      target: "src/api/generated/endpoints.ts",
      schemas: "src/api/generated/model",
      client: "react-query",
      mode: "tags-split",
      // No base URL: every call is a relative path through the one mutator.
      override: {
        mutator: { path: "src/api/fetch.ts", name: "customFetch" },
        // The mutator answers the body and throws ApiError otherwise: no {data, status} wrapper.
        fetch: { includeHttpResponseReturnType: false },
      },
    },
  },
  zod: {
    input,
    output: {
      target: "src/api/generated/zod.ts",
      client: "zod",
      mode: "tags-split",
      // tags-split writes <tag>/<tag>.ts for both outputs: without this the schemas overwrite the hooks.
      fileExtension: ".zod.ts",
    },
  },
});
