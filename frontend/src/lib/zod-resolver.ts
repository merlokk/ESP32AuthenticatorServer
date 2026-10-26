import type { FieldError, FieldErrors, FieldValues, Resolver } from "react-hook-form";
import type { z } from "zod";

// react-hook-form + zod without @hookform/resolvers: its optional peers (@typeschema/*, effect)
// make npm's install and `npm ci` disagree, and a zod resolver is this file.

/**
 * A react-hook-form resolver from a zod schema: the form holds the schema's input and submits its
 * output. Errors land at their path, nested ("address.city", "items.0.name").
 */
export function zodResolver<S extends z.ZodType<FieldValues, FieldValues>>(
  schema: S,
): Resolver<z.input<S>, unknown, z.output<S>> {
  return async (values) => {
    const result = await schema.safeParseAsync(values);
    if (result.success) {
      return { values: result.data, errors: {} };
    }
    return { values: {}, errors: toFieldErrors<z.input<S>>(result.error.issues) };
  };
}

export function toFieldErrors<T extends FieldValues>(issues: z.core.$ZodIssue[]): FieldErrors<T> {
  const errors: Record<string, unknown> = {};
  for (const issue of issues) {
    const path = issue.path.map(String);
    // The root of the form when the issue has no path (a refine over the whole object).
    const keys = path.length > 0 ? path : ["root"];
    let node = errors;
    for (const key of keys.slice(0, -1)) {
      node = (node[key] ??= {}) as Record<string, unknown>;
    }
    const leaf = keys[keys.length - 1];
    // The first issue at a path wins, as a form shows one message per field.
    if (!(node[leaf] && (node[leaf] as FieldError).message)) {
      node[leaf] = { type: issue.code, message: issue.message } satisfies FieldError;
    }
  }
  return errors as FieldErrors<T>;
}
