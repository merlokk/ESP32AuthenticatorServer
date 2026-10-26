import { defineConfig, devices } from "@playwright/test";

// End-to-end against the production build. The backend is not required: the home page reports
// "Backend: unavailable" without one.
export default defineConfig({
  testDir: "e2e",
  fullyParallel: true,
  use: {
    baseURL: "http://localhost:3000",
    trace: "on-first-retry",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
  webServer: {
    command: "npm run start",
    url: "http://localhost:3000/health",
    reuseExistingServer: true,
    env: { BACKEND_URL: process.env.BACKEND_URL ?? "http://localhost:5080" },
  },
});
