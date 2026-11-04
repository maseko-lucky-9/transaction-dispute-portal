/**
 * Hand-written API types for the walking skeleton.
 *
 * `npm run gen:types` writes the generated OpenAPI types to `schema.d.ts` once the backend
 * publishes `docs/openapi.json`. Until then the BFF contract lives here.
 */

/** `GET /bff/user` -> 200. A 401 means "not signed in" and has no body contract. */
export interface User {
  /** Display name from the ID token. */
  name: string;
  /** Realm roles, e.g. "customer" or "dispute-agent". Display only: the API enforces them. */
  roles: string[];
  /**
   * Same-origin URL for RP-initiated logout (`/bff/logout?sid=...`). Navigate to it with a
   * top-level navigation; never fetch it.
   */
  logoutUrl: string;
}
