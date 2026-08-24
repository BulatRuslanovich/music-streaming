// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { SESSION_HINT_COOKIE } from "@/lib/earlyFetch";
import type { User } from "@/lib/types";

export function readSessionHint(): User | null {
  if (typeof document === "undefined") return null;

  const prefix = `${SESSION_HINT_COOKIE}=`;
  const raw = document.cookie
    .split("; ")
    .find((entry) => entry.startsWith(prefix))
    ?.slice(prefix.length);

  if (!raw) return null;

  try {
    return parseUser(decodeBase64Url(raw));
  } catch {
    return null;
  }
}

function decodeBase64Url(value: string): unknown {
  const base64 = value.replace(/-/g, "+").replace(/_/g, "/");
  const padded = base64.padEnd(base64.length + ((4 - (base64.length % 4)) % 4), "=");
  const bytes = Uint8Array.from(atob(padded), (character) => character.charCodeAt(0));

  return JSON.parse(new TextDecoder().decode(bytes));
}

function parseUser(value: unknown): User | null {
  if (!value || typeof value !== "object") return null;

  const candidate = value as Record<string, unknown>;
  if (typeof candidate.id !== "string" || typeof candidate.username !== "string") return null;

  return {
    id: candidate.id,
    username: candidate.username,
    displayName: typeof candidate.displayName === "string" ? candidate.displayName : "",
    isAdmin: candidate.isAdmin === true,
  };
}
