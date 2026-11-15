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
  bar.innerHTML = `
    <a href="../index.html" title="All directions">All</a>
    <button type="button" data-step="-1" aria-label="Previous direction">&larr;</button>
    <span class="label">${DIRECTIONS[di][1]}</span>
    <button type="button" data-step="1" aria-label="Next direction">&rarr;</button>
    <select aria-label="Page">${PAGES.map(([p, l]) => `<option value="${p}"${p === page ? " selected" : ""}>${l}</option>`).join("")}</select>
    <button type="button" data-theme-cycle>Theme: ${store.get()}</button>`;
  bar.addEventListener("click", (e) => {
    const b = e.target.closest("button");
    if (!b) return;
    if (b.dataset.step) go(Number(b.dataset.step));
    if (b.hasAttribute("data-theme-cycle")) {
      const t = THEMES[(THEMES.indexOf(store.get()) + 1) % THEMES.length];
      store.set(t); applyTheme(t); b.textContent = `Theme: ${t}`;
    }
  });
  bar.querySelector("select").addEventListener("change", (e) => { location.href = `${e.target.value}.html`; });
  document.addEventListener("keydown", (e) => {
    if (e.target.closest("input, textarea, select, [contenteditable]")) return;
    if (e.key === "ArrowLeft") go(-1);
    if (e.key === "ArrowRight") go(1);
  });
  document.body.append(bar);
})();
