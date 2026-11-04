import { existsSync, readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";
import { defineConfig } from "vitest/config";

// Where the dev server proxies the BFF/API paths. Override when something else
// already holds 8080 (DISPUTE_API_URL=http://localhost:8081 npm run dev).
const apiTarget = process.env["DISPUTE_API_URL"] ?? "http://localhost:8080";

// The committed OpenAPI snapshot, handed to the contract test as plain data (see
// src/api/contract.test.ts). Read here, in Node, so the browser code and the dev server never
// touch files outside web/. Null in a web-only checkout, which skips that test.
const openApiPath = fileURLToPath(new URL("../docs/openapi.json", import.meta.url));
const openApiSpec = existsSync(openApiPath) ? readFileSync(openApiPath, "utf8") : null;

// Paths owned by the backend. In production nginx routes these; in dev Vite does.
const backendPaths = ["/api", "/bff", "/signin-oidc", "/signout-callback-oidc"];

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: { "@": fileURLToPath(new URL("./src", import.meta.url)) },
  },
  server: {
    port: 5173,
    strictPort: true,
    // changeOrigin stays false on purpose: the backend builds OIDC redirect URIs and
    // checks the Host header, so it must see the host the browser actually used.
    proxy: Object.fromEntries(backendPaths.map((path) => [path, { target: apiTarget }])),
  },
  build: {
    // CSP is `default-src 'none'` with font-src 'self': never inline assets as data: URIs.
    assetsInlineLimit: 0,
    sourcemap: false,
  },
  test: {
    environment: "jsdom",
    setupFiles: ["./src/test/setup.ts"],
    include: ["src/**/*.test.{ts,tsx}"],
    clearMocks: true,
    provide: { openApiSpec },
    unstubGlobals: true,
    coverage: {
      provider: "v8",
      reporter: ["text", "lcov"],
      include: ["src/**/*.{ts,tsx}"],
      exclude: [
        "src/**/*.test.{ts,tsx}",
        "src/**/*.d.ts",
        "src/test/**",
        "src/main.tsx", // bootstrap only: createRoot on the real DOM
        "src/components/ui/**", // shadcn primitives, generated style
      ],
      thresholds: { lines: 80, functions: 80, branches: 80, statements: 80 },
    },
  },
});
