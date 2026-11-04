import "vitest";

declare module "vitest" {
  interface ProvidedContext {
    /** Contents of docs/openapi.json, or null when the repo's docs/ is not next to web/. */
    openApiSpec: string | null;
  }
}
