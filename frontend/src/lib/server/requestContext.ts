// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import "server-only";
import { AsyncLocalStorage } from "node:async_hooks";

interface ServerRequestContext {
  /** Куки текущего запроса — на сервере они не подставляются сами. */
  cookie: string;
  /** Адрес бэкенда изнутри сети: относительный /api на сервере не резолвится. */
  origin: string;
}

const GLOBAL_KEY = "__msServerRequest";

type ContextHolder = Record<string, AsyncLocalStorage<ServerRequestContext> | undefined>;

/**
 * Контекст серверного запроса, доступный из общего кода без импорта серверных модулей.
 *
 * Хранилище кладётся на globalThis намеренно: `http.ts` используется и на клиенте, и статический
 * импорт `next/headers` или `node:async_hooks` оттуда утащил бы их в клиентский бандл. Здесь —
 * единственное место, которое знает про серверную половину, а `http.ts` только читает глобаль.
 */
export const requestContext: AsyncLocalStorage<ServerRequestContext> = ((
  globalThis as unknown as ContextHolder
)[GLOBAL_KEY] ??= new AsyncLocalStorage<ServerRequestContext>());

export function backendOrigin(): string {
  return process.env.BACKEND_INTERNAL_URL ?? "http://localhost:5199";
}
