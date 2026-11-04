import { StrictMode } from "react";
import { createRoot } from "react-dom/client";

import "./lib/zod-csp";
import "./index.css";
import { App } from "./app";
import { createQueryClient } from "./api/query-client";
import { createAppRouter } from "./router";
import { applyStoredTheme } from "./lib/theme";

applyStoredTheme();

const rootElement = document.getElementById("root");
if (!rootElement) throw new Error("Missing #root element");

createRoot(rootElement).render(
  <StrictMode>
    <App queryClient={createQueryClient()} router={createAppRouter()} />
  </StrictMode>,
);
