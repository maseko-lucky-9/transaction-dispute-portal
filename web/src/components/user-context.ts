import { createContext, useContext } from "react";

import type { User } from "@/api/types";

export const UserContext = createContext<User | null>(null);

/** The signed-in user. Only callable below <SessionGate>, which guarantees one exists. */
export function useUser(): User {
  const user = useContext(UserContext);
  if (!user) throw new Error("useUser must be used inside <SessionGate>");
  return user;
}
