import { describe, expect, it } from "vitest";
import { USER_NAME, userNameFor } from "./user-name";

describe("userNameFor", () => {
  it.each([
    ["ada@example.com", "ada"],
    ["Ada.Lovelace@example.com", "Ada.Lovelace"],
    ["ada+tag@example.com", "ada-tag"],
    ["+ada+@example.com", "ada"],
    ["", ""],
  ])("%s → %s", (email, name) => {
    expect(userNameFor(email)).toBe(name);
  });

  it("gives a name the backend accepts", () => {
    expect(USER_NAME.safeParse(userNameFor("grace_hopper@navy.mil")).success).toBe(true);
  });
});
