import { LogIn } from "lucide-react";

import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { currentReturnUrl, loginUrl, navigate } from "@/lib/navigation";

/**
 * Shown when /bff/user answers 401. Signing in is a top-level navigation to the BFF, which
 * redirects on to Keycloak. It is never a fetch: a fetch cannot follow a cross-origin
 * redirect to a login page, and the SPA must never handle credentials.
 */
export function SignIn() {
  return (
    <Card className="mx-auto mt-12 max-w-md">
      <CardHeader>
        <CardTitle as="h1" className="text-xl">
          Sign in
        </CardTitle>
        <CardDescription>Sign in to view your transactions and manage disputes.</CardDescription>
      </CardHeader>
      <CardContent>
        <Button onClick={() => navigate(loginUrl(currentReturnUrl()))}>
          <LogIn aria-hidden="true" />
          Sign in
        </Button>
      </CardContent>
    </Card>
  );
}
