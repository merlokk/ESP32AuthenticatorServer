import { describe, expect, it } from "vitest";
import { z } from "zod";
import { zodResolver } from "./zod-resolver";

const schema = z.object({
  name: z.string().min(1, "Required"),
  address: z.object({ city: z.string().min(2, "Too short") }),
  pin: z.string().transform((value) => value.trim()),
});

const resolve = (values: unknown) =>
  zodResolver(schema)(values as z.input<typeof schema>, undefined, {
    fields: {},
    shouldUseNativeValidation: false,
  });

describe("zodResolver", () => {
  it("returns the schema's output on success", async () => {
    const result = await resolve({ name: "a", address: { city: "Kyiv" }, pin: " 123456 " });
    expect(result.errors).toEqual({});
    expect(result.values).toEqual({ name: "a", address: { city: "Kyiv" }, pin: "123456" });
  });

  it("puts errors at their nested path, one message per field", async () => {
    const result = await resolve({ name: "", address: { city: "K" }, pin: "1" });
    expect(result.values).toEqual({});
    expect(result.errors).toMatchObject({
      name: { message: "Required" },
      address: { city: { message: "Too short" } },
    });
  });

  it("puts a whole-object refinement on root", async () => {
    const confirm = z
      .object({ a: z.string(), b: z.string() })
      .refine((v) => v.a === v.b, "Do not match");
    const result = await zodResolver(confirm)({ a: "1", b: "2" }, undefined, {
      fields: {},
      shouldUseNativeValidation: false,
    });
    expect(result.errors).toMatchObject({ root: { message: "Do not match" } });
  });
});
