// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import type Hls from "hls.js";
import type { ErrorData, HlsConfig } from "hls.js";
import { playableTier } from "@/lib/audioFormats";
import {
  createSessionAwareLoader,
  forgetPrimedManifest,
  primeManifest,
} from "@/lib/hlsSessionLoader";
import { fetchMedia } from "@/lib/http";
import { mediaUrl } from "@/lib/media";
import type { AudioQuality, AudioQualityOption } from "@/lib/types";

export type AdaptiveQuality = Exclude<AudioQuality, "Original">;
type PlaybackTransport = "hls.js" | "progressive";

interface PlaybackRequest {
  trackId: string;
  codec?: string | null;
  quality: AudioQuality;
  qualities: AudioQualityOption[];
  hlsEnabled: boolean;
  forceAdaptive: boolean;
  slowNetwork: boolean;
  startAt: number;
  play: boolean;
}

interface PlaybackLoadResult {
  transport: PlaybackTransport;
  tier: AudioQuality;
}

interface PlaybackCallbacks {
  onFatalError: () => void;
  onLevelChanged?: (quality: AdaptiveQuality) => void;
}

const HLS_RETRY_DELAYS = [800, 2500, 6000];
const HLS_PREPARATION_RETRY_MS = 10_000;

// Шесть попыток — это минута, после которой слушатель оставался на оригинале до конца трека,
// даже если рендишен доготавливался на второй минуте. Проба стоит одного запроса за крошечным
// манифестом, так что дешевле держать её всё время звучания трека, чем гнать многомегабайтный
// оригинал по узкому каналу.
const HLS_PREPARATION_ATTEMPTS = 30;

type HlsModule = typeof import("hls.js");

let hlsLoading: Promise<HlsModule | null> | null = null;
let sessionAwareLoader: HlsConfig["loader"] | null = null;

function loadHls(): Promise<HlsModule | null> {
  hlsLoading ??= import("hls.js")
    .then((module) => {
      sessionAwareLoader = createSessionAwareLoader(module.default.DefaultConfig.loader);
      return module;
    })
    .catch(() => {
      hlsLoading = null;
      return null;
    });

  return hlsLoading;
}

/**
 * Заранее тянет чанк hls.js (около 180 КБ в gzip), не блокируя ничего.
 *
 * Раньше он скачивался в момент первого нажатия play и целиком лежал на пути к первому звуку.
 * Вызывать это на монтировании не стоит: на узком канале он отнимет полосу у контента страницы.
 */
export function warmUpHls(): void {
  void loadHls();
}

export function adaptiveCap(quality: AudioQuality): AdaptiveQuality {
  return quality === "Original" ? "High" : quality;
}

export function choosePlaybackTransport(
  request: Pick<PlaybackRequest, "quality" | "hlsEnabled" | "forceAdaptive"> & {
    progressiveTier: AudioQuality;
  },
  hlsJsSupported: boolean,
): PlaybackTransport {
  const adaptiveWanted = request.forceAdaptive || request.progressiveTier !== "Original";
  if (!request.hlsEnabled || !adaptiveWanted) return "progressive";
  return hlsJsSupported ? "hls.js" : "progressive";
}

export class AdaptivePlayback {
  private readonly audio: HTMLAudioElement;
  private readonly callbacks: PlaybackCallbacks;
  private hls: Hls | null = null;
  private hlsApi: HlsModule | null = null;
  private request: PlaybackRequest | null = null;
  private generation = 0;
  private retryTimer: number | null = null;
  private retries = 0;
  private preparationAttempts = 0;

  transport: PlaybackTransport = "progressive";

  constructor(audio: HTMLAudioElement, callbacks: PlaybackCallbacks) {
    this.audio = audio;
    this.callbacks = callbacks;
  }

  async load(request: PlaybackRequest): Promise<PlaybackLoadResult> {
    const generation = ++this.generation;
    this.request = request;
    this.retries = 0;
    this.preparationAttempts = 0;
    this.destroyDriver();
    this.transport = "progressive";
    this.audio.dataset.playbackMode = "progressive";
    this.audio.dataset.sourceLoading = "true";

    // Пауза — сразу, это реакция на действие пользователя. А вот обнулять src до того, как новый
    // источник готов, нельзя: элемент оставался пустым на всю цепочку старта и успевал выстрелить
    // emptied/error, которые движок принимал за сбой загрузки.
    this.audio.pause();

    const progressiveTier = playableTier(request.codec, request.quality, request.qualities);

    const adaptiveWanted =
      request.hlsEnabled && (request.forceAdaptive || progressiveTier !== "Original");
    if (adaptiveWanted) this.hlsApi = await loadHls();

    const hlsJsSupported = this.hlsApi?.default.isSupported() ?? false;
    const wanted = choosePlaybackTransport({ ...request, progressiveTier }, hlsJsSupported);

    if (wanted !== "progressive") {
      const cap = adaptiveCap(request.quality);
      const url = mediaUrl.hls(request.trackId, cap);
      if (await this.hlsReady(url)) {
        if (generation !== this.generation) {
          // Загрузку обогнала следующая — иначе припасённый манифест остался бы висеть.
          forgetPrimedManifest(url);
          return { transport: this.transport, tier: progressiveTier };
        }
        this.attachAdaptive(url, cap, request.startAt, request.play);
        return { transport: wanted, tier: cap };
      }

      this.schedulePreparationProbe(generation, url, cap);
    }

    if (generation === this.generation)
      this.attachProgressive(progressiveTier, request.startAt, request.play);

    return { transport: "progressive", tier: progressiveTier };
  }

  seek(seconds: number): void {
    this.audio.currentTime = seconds;
  }

  destroy(): void {
    this.generation += 1;
    this.request = null;
    this.destroyDriver();
  }

  private attachAdaptive(url: string, cap: AdaptiveQuality, startAt: number, play: boolean): void {
    this.destroyDriver();

    if (!this.hlsApi) {
      this.attachProgressive(cap, startAt, play);
      return;
    }

    this.transport = "hls.js";
    this.audio.dataset.playbackMode = "hls.js";
    this.audio.dataset.sourceLoading = "false";
    this.audio.removeAttribute("src");
    this.audio.load();

    const { default: HlsCtor, Events } = this.hlsApi;
    const hls = new HlsCtor({
      loader: sessionAwareLoader ?? undefined,
      startLevel: -1,
      // На заведомо узком канале стартовая оценка в 128 кбит/с — это ставка на Normal, и первый
      // сегмент приезжает дольше, чем длится. Занижаем, чтобы разгон шёл с Low вверх, а не наоборот.
      abrEwmaDefaultEstimate: this.request?.slowNetwork ? 56_000 : 128_000,
      // Первый фрагмент тянется параллельно разбору плейлиста, а не после него.
      startFragPrefetch: true,
      // Пробный запрос ради замера полосы — лишний round-trip ровно там, где он дороже всего.
      testBandwidth: false,
      maxBufferLength: 180,
      maxMaxBufferLength: 300,
      backBufferLength: 30,
    });

    this.hls = hls;
    hls.on(Events.MEDIA_ATTACHED, () => hls.loadSource(url));
    hls.on(Events.MANIFEST_PARSED, () => this.resumeAt(startAt, play));
    hls.on(Events.LEVEL_SWITCHED, (_, data) => {
      const bitrate = hls.levels[data.level]?.bitrate ?? 0;
      this.callbacks.onLevelChanged?.(qualityForBitrate(bitrate, cap));
    });
    hls.on(Events.ERROR, (_, data) => this.handleHlsError(data));
    hls.attachMedia(this.audio);
  }

  private attachProgressive(tier: AudioQuality, startAt: number, play: boolean): void {
    this.destroyDriver();
    this.transport = "progressive";
    this.audio.dataset.playbackMode = "progressive";
    this.audio.dataset.sourceLoading = "false";
    // Присваивание src само заменяет источник — обнулять его отдельно не нужно.
    this.audio.src = mediaUrl.stream(this.request!.trackId, tier);
    this.audio.load();
    this.resumeAt(startAt, play);
  }

  private resumeAt(startAt: number, play: boolean): void {
    const apply = () => {
      if (startAt > 0 && Number.isFinite(this.audio.duration)) {
        this.audio.currentTime = Math.min(startAt, this.audio.duration);
      }
      if (play) void this.audio.play().catch(() => {});
    };

    if (this.audio.readyState >= HTMLMediaElement.HAVE_METADATA) apply();
    else this.audio.addEventListener("loadedmetadata", apply, { once: true });
  }

  private handleHlsError(data: ErrorData): void {
    if (!data.fatal || !this.hls || !this.hlsApi) return;

    const { ErrorTypes } = this.hlsApi;

    if (data.type === ErrorTypes.MEDIA_ERROR && this.retries < HLS_RETRY_DELAYS.length) {
      this.retries += 1;
      this.hls.recoverMediaError();
      return;
    }

    if (data.type === ErrorTypes.NETWORK_ERROR && this.scheduleRetry(() => this.hls?.startLoad())) {
      return;
    }

    this.callbacks.onFatalError();
  }

  private scheduleRetry(action: () => void): boolean {
    if (this.retries >= HLS_RETRY_DELAYS.length) return false;
    const delay = HLS_RETRY_DELAYS[this.retries++];
    this.clearRetryTimer();
    this.retryTimer = window.setTimeout(action, delay);
    return true;
  }

  private schedulePreparationProbe(generation: number, url: string, cap: AdaptiveQuality): void {
    if (this.preparationAttempts >= HLS_PREPARATION_ATTEMPTS) return;

    this.preparationAttempts += 1;
    this.clearRetryTimer();
    this.retryTimer = window.setTimeout(() => {
      void (async () => {
        if (generation !== this.generation || !this.request) return;
        if (!(await this.hlsReady(url))) {
          this.schedulePreparationProbe(generation, url, cap);
          return;
        }

        const position = this.audio.currentTime;
        const shouldPlay = !this.audio.paused;
        this.attachAdaptive(url, cap, position, shouldPlay);
      })();
    }, HLS_PREPARATION_RETRY_MS);
  }

  // Проба не только отвечает «готов ли», но и оставляет скачанный манифест загрузчику hls.js —
  // иначе тот запросил бы тот же URL второй раз. no-store здесь больше не нужен: неготовый мастер
  // отдаётся с no-store самим бэкендом, а готовый можно и нужно брать из кэша.
  private async hlsReady(url: string): Promise<boolean> {
    const controller = new AbortController();
    const timeout = window.setTimeout(() => controller.abort(), 5_000);

    try {
      const response = await fetchMedia(url, { signal: controller.signal });

      if (!response.ok || response.status === 202) {
        await response.body?.cancel().catch(() => {});
        return false;
      }

      primeManifest(url, await response.text());
      return true;
    } catch {
      return false;
    } finally {
      window.clearTimeout(timeout);
    }
  }

  private destroyDriver(): void {
    this.clearRetryTimer();
    this.hls?.destroy();
    this.hls = null;
  }

  private clearRetryTimer(): void {
    if (this.retryTimer === null) return;
    window.clearTimeout(this.retryTimer);
    this.retryTimer = null;
  }
}

function qualityForBitrate(bitrate: number, cap: AdaptiveQuality): AdaptiveQuality {
  if (bitrate <= 80_000) return "Low";
  if (bitrate <= 160_000) return "Normal";
  return cap;
}
