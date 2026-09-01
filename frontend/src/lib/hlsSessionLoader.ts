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

// Проба готовности уже скачивает master.m3u8 целиком — и раньше выбрасывала его, после чего
// hls.js запрашивал ровно тот же URL второй раз. Здесь текст лежит до первого обращения загрузчика.
// Только мастер: подменять статистику загрузки фрагментов нельзя, на ней стоит выбор уровня в ABR.
const primed = new Map<string, string>();

export function primeManifest(url: string, payload: string): void {
  primed.set(url, payload);
}

export function forgetPrimedManifest(url: string): void {
  primed.delete(url);
}

function primedStats(length: number): LoaderStats {
  const now = performance.now();

  return {
    aborted: false,
    loaded: length,
    retry: 0,
    total: length,
    chunkCount: 1,
    bwEstimate: 0,
    loading: { start: now, first: now, end: now },
    parsing: { start: 0, end: 0 },
    buffering: { start: 0, first: 0, end: 0 },
  };
}

export function createSessionAwareLoader(BaseLoader: LoaderConstructor): LoaderConstructor {
  return class SessionAwareLoader implements Loader<LoaderContext> {
    private readonly hlsConfig: HlsConfig;
    private inner: Loader<LoaderContext>;
    private refreshed = false;
    private destroyed = false;
    private primedTimer: number | null = null;

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

      const ready = primed.get(context.url);
      if (ready !== undefined) {
        primed.delete(context.url);
        this.stats = primedStats(ready.length);

        // Асинхронно: hls.js дочитывает своё состояние уже после возврата из load().
        this.primedTimer = window.setTimeout(() => {
          this.primedTimer = null;
          if (this.destroyed) return;
          callbacks.onSuccess({ url: context.url, data: ready }, this.stats, context, null);
        }, 0);

        return;
      }

      this.inner.load(context, config, this.reauthorizing(context, config, callbacks));
    }

    abort(): void {
      this.clearPrimedTimer();
      this.inner.abort();
    }

    destroy(): void {
      this.destroyed = true;
      this.clearPrimedTimer();
      this.inner.destroy();
    }

    private clearPrimedTimer(): void {
      if (this.primedTimer === null) return;
      window.clearTimeout(this.primedTimer);
      this.primedTimer = null;
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
