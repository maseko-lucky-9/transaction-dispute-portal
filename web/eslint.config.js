import js from "@eslint/js";
import globals from "globals";
import reactHooks from "eslint-plugin-react-hooks";
import reactRefresh from "eslint-plugin-react-refresh";
import tseslint from "typescript-eslint";
import prettier from "eslint-config-prettier/flat";
import { defineConfig, globalIgnores } from "eslint/config";

export default defineConfig([
  globalIgnores(["dist", "coverage", "src/api/schema.d.ts"]),
  {
    files: ["**/*.{ts,tsx}"],
    extends: [
      js.configs.recommended,
      tseslint.configs.recommendedTypeChecked,
      reactHooks.configs.flat.recommended,
      reactRefresh.configs.vite,
    ],
    languageOptions: {
      globals: globals.browser,
      parserOptions: {
        projectService: true,
        tsconfigRootDir: import.meta.dirname,
      },
    },
    rules: {
      "@typescript-eslint/consistent-type-imports": "error",
      // zod must be configured for the CSP before any schema exists: see src/lib/zod.ts.
      "no-restricted-imports": [
        "error",
        {
          paths: [
            {
              name: "zod",
              message:
                'Import { z } from "@/lib/zod": it applies the CSP-safe zod configuration first.',
            },
          ],
        },
      ],
    },
  },
  {
    // The two files that ARE the zod entry point, and the test that pins the configuration.
    files: ["src/lib/zod.ts", "src/lib/zod-csp.ts", "src/lib/zod-csp.test.ts"],
    rules: { "no-restricted-imports": "off" },
  },
  {
    // Node-side files: build config and the post-build guard.
    files: ["vite.config.ts", "scripts/**/*.mjs"],
    languageOptions: { globals: globals.node },
  },
  {
    // shadcn primitives export their cva variants next to the component by design.
    files: ["src/components/ui/**/*.tsx"],
    rules: { "react-refresh/only-export-components": "off" },
  },
  {
    files: ["**/*.js", "**/*.mjs"],
    extends: [tseslint.configs.disableTypeChecked],
  },
  prettier,
]);
