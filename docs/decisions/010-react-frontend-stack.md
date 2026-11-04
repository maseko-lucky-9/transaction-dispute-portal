# 010. Frontend stack: React

Status: accepted (2026-11-04)

## Decision
React is the most widely used frontend framework and the owner's existing stack, so it minimises risk for a
one-week build: React 19, Vite, TypeScript strict, TanStack Router and Query, Tailwind 4 with shadcn/ui,
react-hook-form with zod, Vitest, Testing Library, MSW, Playwright and axe. The SPA is static files behind nginx
and holds no tokens. TypeScript API types are generated from the code-first OpenAPI snapshot; CI fails if they
drift. Validation rules live in zod on the client and FluentValidation on the server, pinned together by a shared
`validation-cases.json`.
