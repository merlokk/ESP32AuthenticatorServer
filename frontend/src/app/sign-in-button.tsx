import { LogIn } from "lucide-react";
import { Button } from "@/components/ui/button";
import { DEFAULT_RETURN_URL, signInStartUrl } from "@/lib/return-url";

/**
 * Starts the code flow; the authorization server shows the sign-in page only without its cookie.
 * A plain link to a route: it works without JavaScript and survives a deployment.
 */
export function SignInButton() {
  return (
    <Button asChild>
      <a href={signInStartUrl(DEFAULT_RETURN_URL)}>
        <LogIn aria-hidden />
        Sign in
      </a>
    </Button>
  );
}
