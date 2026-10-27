// The browser's own WebAuthn JSON API (architecture.md §9: no package). The server's options and
// the credential cross as JSON strings, unaltered.

export type AssertionOutcome =
  | { kind: "credential"; json: string }
  /** Dismissed or timed out: not a failure to report. */
  | { kind: "abandoned" }
  | { kind: "failed" };

export function passkeysSupported(): boolean {
  return (
    typeof window !== "undefined" &&
    typeof window.PublicKeyCredential !== "undefined" &&
    typeof window.PublicKeyCredential.parseRequestOptionsFromJSON === "function"
  );
}

/** navigator.credentials.get() with the server's request options. */
export async function getAssertion(requestOptionsJson: string): Promise<AssertionOutcome> {
  if (!passkeysSupported()) return { kind: "failed" };

  let options: PublicKeyCredentialRequestOptions;
  try {
    options = PublicKeyCredential.parseRequestOptionsFromJSON(
      JSON.parse(requestOptionsJson) as PublicKeyCredentialRequestOptionsJSON,
    );
  } catch {
    return { kind: "failed" };
  }

  try {
    const credential = (await navigator.credentials.get({
      publicKey: options,
    })) as PublicKeyCredential | null;
    if (!credential) return { kind: "abandoned" };
    return { kind: "credential", json: JSON.stringify(credential.toJSON()) };
  } catch (error) {
    if (error instanceof DOMException && error.name === "NotAllowedError") {
      return { kind: "abandoned" };
    }
    return { kind: "failed" };
  }
}
