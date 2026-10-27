import { expect, test } from "@playwright/test";

test("sign-in page offers e-mail and device code", async ({ page }) => {
  await page.goto("/sign-in?returnUrl=%2Fconnect%2Fauthorize%3Fclient_id%3Dauthserver-web");
  await expect(page.getByRole("heading", { name: "Sign in" })).toBeVisible();
  await expect(page.getByLabel("E-mail")).toBeVisible();
  await expect(page.getByLabel("Code from your device")).toBeVisible();
  // Chromium has the WebAuthn JSON API.
  await expect(page.getByRole("button", { name: "Sign in with a passkey" })).toBeVisible();
});

test("an invalid code is caught before anything is sent", async ({ page }) => {
  await page.goto("/sign-in");
  await page.getByLabel("E-mail").fill("ada@example.com");
  await page.getByLabel("Code from your device").fill("12");
  await page.getByRole("button", { name: "Sign in", exact: true }).click();
  await expect(page.getByText("Enter the 6-digit code your device shows.")).toBeVisible();
});

test("home page offers to sign in", async ({ page }) => {
  await page.goto("/");
  await expect(page.getByRole("button", { name: "Sign in" })).toBeVisible();
});
