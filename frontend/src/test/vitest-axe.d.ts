import type { AxeMatchers } from "vitest-axe/matchers";

// vitest-axe still augments the pre-1.0 global `Vi` namespace; Vitest 5 wants the module.
// The generic default must match jest-dom's declaration of the same interface.
declare module "vitest" {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any, @typescript-eslint/no-empty-object-type, @typescript-eslint/no-unused-vars
  interface Assertion<T = any> extends AxeMatchers {}
  // eslint-disable-next-line @typescript-eslint/no-empty-object-type
  interface AsymmetricMatchersContaining extends AxeMatchers {}
}
