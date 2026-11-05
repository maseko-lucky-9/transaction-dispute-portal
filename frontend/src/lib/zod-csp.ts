import * as z from "zod";

// The nginx CSP has no 'unsafe-eval'. Zod 4's JIT probes `new Function("")` and swallows the CSP error, but the
// browser still reports a securitypolicyviolation. Opt out of the JIT path (the e2e suite fails on any violation).
z.config({ jitless: true });
