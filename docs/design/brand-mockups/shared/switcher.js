// PROTOTYPE switcher: cycles design directions on the same page (arrow keys too), jumps between
// pages and toggles the theme. Not part of any design; never ships with the app.
(() => {
  const DIRECTIONS = [
    ["a-crimson-pill", "A Crimson Pill"],
    ["b-gem-sidebar", "B Gem Sidebar"],
    ["c-compact-ledger", "C Compact Ledger"],
    ["d-royal-dashboard", "D Royal Dashboard"],
    ["e-soft-canvas", "E Soft Canvas"],
  ];
  const PAGES = [
    ["landing", "Landing"], ["sign-in", "Sign in (Keycloak)"], ["home", "Home"],
    ["transactions", "Transactions"], ["dispute", "Dispute"], ["agent", "Agent"],
  ];
  const THEMES = ["system", "light", "dark"];

  const store = {
    get: () => {
      try { const v = localStorage.getItem("mock-theme"); return THEMES.includes(v) ? v : "system"; }
      catch { return "system"; }
    },
    set: (v) => { try { localStorage.setItem("mock-theme", v); } catch { /* storage blocked */ } },
  };
  const applyTheme = (t) => {
    if (t === "system") delete document.documentElement.dataset.theme;
    else document.documentElement.dataset.theme = t;
  };
  applyTheme(store.get());

  const parts = location.pathname.split("/");
  const page = (parts.pop() || "landing.html").replace(".html", "");
  const dir = parts.pop();
  const di = DIRECTIONS.findIndex(([d]) => d === dir);
  if (di < 0) return;
  const go = (step) => {
    const [next] = DIRECTIONS[(di + step + DIRECTIONS.length) % DIRECTIONS.length];
    location.href = `../${next}/${page}.html`;
  };

  const bar = document.createElement("div");
  bar.className = "proto-bar";
  bar.setAttribute("aria-label", "Prototype switcher (not part of the design)");
  // Built with DOM methods only: no markup is parsed from strings.
  const el = (tag, props = {}, text = "") => Object.assign(document.createElement(tag), props, { textContent: text });
  const all = el("a", { href: "../index.html", title: "All directions" }, "All");
  const prev = el("button", { type: "button", ariaLabel: "Previous direction" }, "←");
  const label = el("span", { className: "label" }, DIRECTIONS[di][1]);
  const next = el("button", { type: "button", ariaLabel: "Next direction" }, "→");
  const select = el("select", { ariaLabel: "Page" });
  PAGES.forEach(([p, l]) => select.append(el("option", { value: p, selected: p === page }, l)));
  const theme = el("button", { type: "button" }, `Theme: ${store.get()}`);
  bar.append(all, prev, label, next, select, theme);

  prev.addEventListener("click", () => go(-1));
  next.addEventListener("click", () => go(1));
  theme.addEventListener("click", () => {
    const t = THEMES[(THEMES.indexOf(store.get()) + 1) % THEMES.length];
    store.set(t); applyTheme(t); theme.textContent = `Theme: ${t}`;
  });
  select.addEventListener("change", () => {
    // Navigate only to a known page name, never to whatever the control holds.
    const target = PAGES.find(([p]) => p === select.value);
    if (target) location.href = `${target[0]}.html`;
  });
  document.addEventListener("keydown", (e) => {
    if (e.target.closest("input, textarea, select, [contenteditable]")) return;
    if (e.key === "ArrowLeft") go(-1);
    if (e.key === "ArrowRight") go(1);
  });
  document.body.append(bar);
})();
