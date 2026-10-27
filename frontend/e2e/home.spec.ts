import { expect, test } from "@playwright/test";

test("home page renders and reports the backend", async ({ page }) => {
  await page.goto("/");
  await expect(page.getByRole("heading", { name: "ESP32 Authenticator" })).toBeVisible();
  // The answer, not the streaming placeholder "Backend: …".
  await expect(page.getByText(/^Backend: [^…]/)).toBeVisible();
});

test("health answers without the backend", async ({ request }) => {
  const response = await request.get("/health");
  expect(response.status()).toBe(200);
  expect(await response.text()).toBe("Healthy");
});
