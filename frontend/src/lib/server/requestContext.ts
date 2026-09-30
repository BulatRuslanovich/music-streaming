// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import "server-only";
import { AsyncLocalStorage } from "node:async_hooks";

interface ServerRequestContext {
  cookie: string;
  origin: string;
}

const GLOBAL_KEY = "__msServerRequest";

type ContextHolder = Record<string, AsyncLocalStorage<ServerRequestContext> | undefined>;

export const requestContext: AsyncLocalStorage<ServerRequestContext> = ((
  globalThis as unknown as ContextHolder
)[GLOBAL_KEY] ??= new AsyncLocalStorage<ServerRequestContext>());

export function backendOrigin(): string {
  return process.env.BACKEND_INTERNAL_URL ?? "http://localhost:5199";
}
