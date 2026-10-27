import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { REFUSED, SignInForm, UNREACHABLE } from "./sign-in-form";

const fetchMock = vi.fn<typeof fetch>();
const assign = vi.fn();

beforeEach(() => {
  vi.stubGlobal("fetch", fetchMock);
  vi.stubGlobal("location", { ...window.location, assign });
});

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  vi.resetAllMocks();
});

function problem(status: number) {
  return new Response(JSON.stringify({ type: "urn:authserver:problem:sign-in.refused", status }), {
    status,
    headers: { "content-type": "application/problem+json" },
  });
}

function fill(email: string, code: string) {
  fireEvent.input(screen.getByLabelText("E-mail"), { target: { value: email } });
  fireEvent.input(screen.getByLabelText("Code from your device"), { target: { value: code } });
  fireEvent.click(screen.getByRole("button", { name: "Sign in" }));
}

describe("SignInForm", () => {
  it("posts the e-mail and code, then follows the flow", async () => {
    fetchMock.mockResolvedValue(Response.json({ signedIn: true }));
    render(<SignInForm returnUrl="/connect/authorize?client_id=a" />);

    fill(" ada@example.com ", "123456");

    await waitFor(() => expect(assign).toHaveBeenCalledWith("/connect/authorize?client_id=a"));
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe("/api/identity/sign-in/totp");
    expect(JSON.parse(init?.body as string)).toEqual({ email: "ada@example.com", code: "123456" });
  });

  it("checks the fields before sending anything", async () => {
    render(<SignInForm returnUrl="/" />);

    fill("not-an-address", "12");

    expect(await screen.findByText("Enter your e-mail address.")).toBeTruthy();
    expect(screen.getByText("Enter the 6-digit code your device shows.")).toBeTruthy();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("shows one refusal and stays on the page", async () => {
    fetchMock.mockResolvedValue(problem(401));
    render(<SignInForm returnUrl="/" />);

    fill("ada@example.com", "123456");

    expect((await screen.findByRole("alert")).textContent).toBe(REFUSED);
    expect(assign).not.toHaveBeenCalled();
  });

  it("tells an unreachable backend apart from a refusal", async () => {
    fetchMock.mockResolvedValue(problem(502));
    render(<SignInForm returnUrl="/" />);

    fill("ada@example.com", "123456");

    expect((await screen.findByRole("alert")).textContent).toBe(UNREACHABLE);
  });
});
