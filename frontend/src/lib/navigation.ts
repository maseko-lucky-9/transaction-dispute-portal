/**
 * Top-level navigations. Sign-in and logout MUST be real browser navigations (the BFF answers
 * with redirects to Keycloak), never fetches. They live behind one function so tests can
 * observe them without jsdom's unimplemented `location.assign`.
 */
export function navigate(url: string): void {
  window.location.assign(url);
}

/** The page the user is on, as a same-site path the BFF can send them back to after login. */
export function currentReturnUrl(): string {
  const { pathname, search } = window.location;
  // "//host" is protocol-relative: never offer it as a return target.
  return pathname.startsWith("//") ? "/" : `${pathname}${search}`;
}

export function loginUrl(returnUrl: string): string {
  return `/bff/login?returnUrl=${encodeURIComponent(returnUrl)}`;
}

/** True for same-origin URLs only (relative paths or absolute URLs on this origin). */
export function isSameOriginUrl(value: string): boolean {
  if (value.startsWith("//") || value.startsWith("/\\")) return false;
  try {
    return new URL(value, window.location.origin).origin === window.location.origin;
  } catch {
    return false;
  }
}
