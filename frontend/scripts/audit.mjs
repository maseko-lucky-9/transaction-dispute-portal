// npm audit gate for CI: fails on any high or critical advisory, except the ones allowlisted below.
// Each exception names why it is safe and a date after which it fails again, so it gets looked at.
// Usage: npm audit --json | node scripts/audit.mjs   (fails closed on anything it cannot read)
import { readFileSync } from "node:fs";

const ALLOW = {
  "GHSA-fc5q-39xc-vjpf": {
    until: "2026-12-31",
    why: "picomatch <=4.0.7 ReDoS with no patched release; reached only through vite at build time, on glob patterns this repo controls, and not in the nginx image",
  },
};

const report = JSON.parse(readFileSync(0, "utf8"));
if (!report.vulnerabilities) {
  console.error("audit: no vulnerabilities section in the npm audit output", report.error ?? "");
  process.exit(1);
}

const today = new Date().toISOString().slice(0, 10);
const blocking = new Set();
const allowed = new Set();
for (const vuln of Object.values(report.vulnerabilities)) {
  for (const via of vuln.via) {
    // String entries point at another package's advisory; the advisory itself is the object.
    if (typeof via !== "object" || !["high", "critical"].includes(via.severity)) continue;
    const id = via.url?.split("/").pop() ?? via.title;
    const exception = ALLOW[id];
    (exception && today <= exception.until ? allowed : blocking).add(`${id} ${via.severity} ${via.name}: ${via.title}`);
  }
}

for (const line of allowed) console.log(`allowlisted: ${line}`);
for (const line of blocking) console.error(`blocking: ${line}`);
if (blocking.size > 0) process.exit(1);
console.log(`audit: no high or critical advisories outside the allowlist (${allowed.size} allowlisted)`);
