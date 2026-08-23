// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import type {
  HlsConfig,
  Loader,
  LoaderCallbacks,
  LoaderConfiguration,
  LoaderContext,
  LoaderStats,
} from "hls.js";
import { refreshSession } from "@/lib/http";

const UNAUTHORIZED = 401;

type LoaderConstructor = HlsConfig["loader"];

/**
 * hls.js тянет плейлисты и сегменты мимо `request()` из lib/http, так что истёкший
 * access-токен для него — не повод продлить сессию, а обычная ошибка загрузки. На 4xx
 * внутренний ретрай не полагается, и фрагмент сразу уходит в фатальный NETWORK_ERROR.
 * Токен живёт десять минут, а плейлист в дороге — дольше: без этой обёртки звук рвётся
 * на каждом продлении сессии.
 *
 * Свежий экземпляр базового загрузчика на повтор нужен не для красоты: BaseLoader
 * бросает «Loader can only be used once», если позвать load() дважды.
 *
 * BaseLoader приходит параметром, а не берётся из `Hls.DefaultConfig`: иначе модуль
 * тянул бы hls.js статическим импортом и ронял его в бандл рут-лейаута.
 */
export function createSessionAwareLoader(BaseLoader: LoaderConstructor): LoaderConstructor {
  return class SessionAwareLoader implements Loader<LoaderContext> {
    private readonly hlsConfig: HlsConfig;
    private inner: Loader<LoaderContext>;
    private refreshed = false;
    private destroyed = false;

    constructor(hlsConfig: HlsConfig) {
      this.hlsConfig = hlsConfig;
      this.inner = new BaseLoader(hlsConfig);
    }

    get context(): LoaderContext | null {
      return this.inner.context;
    }

    set context(value: LoaderContext | null) {
      this.inner.context = value;
    }

    get stats(): LoaderStats {
      return this.inner.stats;
    }

    set stats(value: LoaderStats) {
      this.inner.stats = value;
    }

    load(
      context: LoaderContext,
      config: LoaderConfiguration,
      callbacks: LoaderCallbacks<LoaderContext>,
    ): void {
      this.refreshed = false;
      this.inner.load(context, config, this.reauthorizing(context, config, callbacks));
    }

    abort(): void {
      this.inner.abort();
    }

    destroy(): void {
      this.destroyed = true;
      this.inner.destroy();
    }

    getCacheAge(): number | null {
      return this.inner.getCacheAge?.() ?? null;
    }

    getResponseHeader(name: string): string | null {
      return this.inner.getResponseHeader?.(name) ?? null;
    }

    private reauthorizing(
      context: LoaderContext,
      config: LoaderConfiguration,
      callbacks: LoaderCallbacks<LoaderContext>,
    ): LoaderCallbacks<LoaderContext> {
      const wrapped: LoaderCallbacks<LoaderContext> = {
        ...callbacks,
        onError: (error, errorContext, networkDetails, stats) => {
          const surrender = () => callbacks.onError(error, errorContext, networkDetails, stats);

          if (error.code !== UNAUTHORIZED || this.refreshed || this.destroyed) {
            surrender();
            return;
          }

          this.refreshed = true;
          void refreshSession().then((renewed) => {
            if (!renewed || this.destroyed) {
              surrender();
              return;
            }

            this.inner = new BaseLoader(this.hlsConfig);
            this.inner.load(context, config, wrapped);
          });
        },
      };

      return wrapped;
    }
  };
}
