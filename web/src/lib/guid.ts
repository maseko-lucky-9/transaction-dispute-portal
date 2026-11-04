import { z } from "@/lib/zod";

/** Any 8-4-4-4-12 hex id. Not `z.uuid()`: that also demands RFC version bits the API never promised. */
export const isGuid = (value: string): boolean => z.guid().safeParse(value).success;
