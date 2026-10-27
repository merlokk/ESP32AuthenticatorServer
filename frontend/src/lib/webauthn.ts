// The browser's own WebAuthn JSON API (architecture.md §9: no package). The server's options and
// the credential cross as JSON strings, unaltered.

export type CeremonyOutcome =
  | { kind: "credential"; json: string }
  /** Dismissed or timed out: not a failure to report. */
  | { kind: "abandoned" }
  | { kind: "failed" };

export function passkeysSupported(): boolean {
  return (
    typeof window !== "undefined" &&
    typeof window.PublicKeyCredential !== "undefined" &&
    typeof window.PublicKeyCredential.parseRequestOptionsFromJSON === "function" &&
    typeof window.PublicKeyCredential.parseCreationOptionsFromJSON === "function"
  );
}

/** navigator.credentials.get() with the server's request options. */
export async function getAssertion(requestOptionsJson: string): Promise<CeremonyOutcome> {
  if (!passkeysSupported()) return { kind: "failed" };

  let options: PublicKeyCredentialRequestOptions;
  try {
    options = PublicKeyCredential.parseRequestOptionsFromJSON(
      JSON.parse(requestOptionsJson) as PublicKeyCredentialRequestOptionsJSON,
    );
  } catch {
    return { kind: "failed" };
  }

  return ceremony(() => navigator.credentials.get({ publicKey: options }));
}

/** navigator.credentials.create() with the server's creation options. */
export async function createCredential(creationOptionsJson: string): Promise<CeremonyOutcome> {
  if (!passkeysSupported()) return { kind: "failed" };

  let options: PublicKeyCredentialCreationOptions;
  try {
    options = PublicKeyCredential.parseCreationOptionsFromJSON(
      JSON.parse(creationOptionsJson) as PublicKeyCredentialCreationOptionsJSON,
    );
  } catch {
    return { kind: "failed" };
  }

  return ceremony(() => navigator.credentials.create({ publicKey: options }));
}

async function ceremony(run: () => Promise<Credential | null>): Promise<CeremonyOutcome> {
  try {
    const credential = (await run()) as PublicKeyCredential | null;
    if (!credential) return { kind: "abandoned" };
    return { kind: "credential", json: JSON.stringify(credential.toJSON()) };
  } catch (error) {
    // Dismissed, timed out, or (create) an authenticator that already holds one for this account.
    if (error instanceof DOMException && error.name === "NotAllowedError") {
      return { kind: "abandoned" };
    }
    return { kind: "failed" };
  }
}
