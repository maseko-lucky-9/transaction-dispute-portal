import { setupServer } from "msw/node";

/** Shared MSW server. Tests add handlers with `server.use(...)`; setup.ts resets them. */
export const server = setupServer();
