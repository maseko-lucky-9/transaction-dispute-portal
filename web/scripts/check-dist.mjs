// Post-build guard for the CSP (script-src 'self', default-src 'none').
// Fails the build if dist/index.html would be blocked or reach outside the origin.
import { readFileSync, readdirSync } from "node:fs";
import { join } from "node:path";

const dist = new URL("../dist/", import.meta.url).pathname;
const html = readFileSync(join(dist, "index.html"), "utf8");
const problems = [];

for (const match of html.matchAll(/<script\b([^>]*)>([\s\S]*?)<\/script>/gi)) {
  const [, attrs, body] = match;
  if (!/\bsrc\s*=/.test(attrs)) problems.push("inline <script> (blocked by script-src 'self')");
  else if (body.trim()) problems.push("<script src> with an inline body");
}
if (/\son[a-z]+\s*=/i.test(html)) problems.push("inline event handler attribute");
if (/\bjavascript:/i.test(html)) problems.push("javascript: URL");
for (const match of html.matchAll(/\b(?:src|href)\s*=\s*"([^"]+)"/gi)) {
  if (/^(?:https?:)?\/\//i.test(match[1])) problems.push(`external resource: ${match[1]}`);
}

// Hashed bundles must exist under /assets (nginx caches that path as immutable).
const assets = readdirSync(join(dist, "assets"));
if (!assets.some((file) => file.endsWith(".js"))) problems.push("no JS bundle in dist/assets");
if (!assets.some((file) => file.endsWith(".css"))) problems.push("no CSS bundle in dist/assets");

if (problems.length > 0) {
  console.error(`dist/index.html violates the CSP contract:\n - ${problems.join("\n - ")}`);
  process.exit(1);
}
console.log(`dist OK: no inline scripts, no external URLs (${assets.length} files in /assets)`);
