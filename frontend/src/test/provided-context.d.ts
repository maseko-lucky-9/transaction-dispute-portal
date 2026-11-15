import "vitest";

declare module "vitest" {
  interface ProvidedContext {
    /** Contents of docs/openapi.json, or null when the repo's docs/ is not next to frontend/. */
    openApiSpec: string | null;
    /** Contents of src/index.css (the theme tokens), for the contrast test. */
    indexCss: string;
  }
}
