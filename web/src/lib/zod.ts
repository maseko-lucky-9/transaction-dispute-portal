// The only way application code gets zod. Importing zod-csp here, rather than once in main.tsx,
// is what makes the order safe: the bundler splits code into chunks, and a schema built at module
// level in a shared chunk can run BEFORE the entry's own code. A module that imports `z` from here
// can never see zod without its CSP configuration (ESLint forbids importing "zod" directly).
import "./zod-csp";

export * as z from "zod";
