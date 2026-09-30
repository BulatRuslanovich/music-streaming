// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import type Hls from "hls.js";
import type { ErrorData, HlsConfig } from "hls.js";
import { canDecodeOriginal } from "@/lib/playback/audioFormats";
import {
  createSessionAwareLoader,
  forgetPrimedManifest,
  primeManifest,
} from "@/lib/playback/hlsSessionLoader";
import { fetchWithSession } from "@/lib/http";
import { mediaUrl } from "@/lib/media";
import type { AudioQuality } from "@/lib/types";

export type AdaptiveQuality = Exclude<AudioQuality, "Original">;

interface PlaybackRequest {
  trackId: string;
  codec?: string | null;
  quality: AudioQuality;
  forceAdaptive: boolean;
  slowNetwork: boolean;
  startAt: number;
  play: boolean;
}

interface PlaybackCallbacks {
  onFatalError: () => void;
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
 * Без этого он скачивается в момент первого нажатия play и целиком лежит на пути к первому
 * звуку. Вызывать на монтировании не стоит: на узком канале он отнимет полосу у контента.
 */
export function warmUpHls(): void {
  void loadHls();
}

function adaptiveCap(quality: AudioQuality): AdaptiveQuality {
  return quality === "Original" ? "High" : quality;
}

// Прямой поток — всегда оригинал, перекодированные ступени живут только в HLS. Поэтому адаптивная
// подача нужна всюду, где оригинал не годится: выбрано качество ниже, сеть не тянет или браузер
// не декодирует сам формат.
export function adaptiveWanted(
  request: Pick<PlaybackRequest, "quality" | "forceAdaptive"> & { originalPlayable: boolean },
): boolean {
  return request.forceAdaptive || request.quality !== "Original" || !request.originalPlayable;
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

  // Счётчик поколений разводит загрузки внутри одного экземпляра, но `audio` у всех экземпляров
  // общий. Уничтоженный экземпляр, чей `load` уже был в полёте, без этого флага доходил до
  // `audio.src = ...` и перезапускал предыдущий трек поверх нового.
  private destroyed = false;

  constructor(audio: HTMLAudioElement, callbacks: PlaybackCallbacks) {
    this.audio = audio;
    this.callbacks = callbacks;
  }

  async load(request: PlaybackRequest): Promise<void> {
    if (this.destroyed) return;

    const generation = ++this.generation;
    this.request = request;
    this.retries = 0;
    this.preparationAttempts = 0;
    this.destroyDriver();
    this.audio.dataset.playbackMode = "progressive";
    this.audio.dataset.sourceLoading = "true";

    // Пауза — сразу, это реакция на действие пользователя. А вот обнулять src до того, как новый
    // источник готов, нельзя: элемент оставался пустым на всю цепочку старта и успевал выстрелить
    // emptied/error, которые движок принимал за сбой загрузки.
    this.audio.pause();

    const adaptive = adaptiveWanted({
      ...request,
      originalPlayable: canDecodeOriginal(request.codec),
    });
    if (adaptive) this.hlsApi = await loadHls();

    if (adaptive && this.hlsApi?.default.isSupported()) {
      const cap = adaptiveCap(request.quality);
      const url = mediaUrl.hls(request.trackId, cap);
      if (await this.hlsReady(url)) {
        if (generation !== this.generation) {
          // Загрузку обогнала следующая — иначе припасённый манифест остался бы висеть.
          forgetPrimedManifest(url);
          return;
        }
        this.attachAdaptive(url, request.startAt, request.play);
        return;
      }

      this.schedulePreparationProbe(generation, url);
    }

    if (generation === this.generation) this.attachProgressive(request.startAt, request.play);
  }

  destroy(): void {
    this.destroyed = true;
    this.generation += 1;
    this.request = null;
    this.destroyDriver();
  }

  private attachAdaptive(url: string, startAt: number, play: boolean): void {
    this.destroyDriver();

    if (!this.hlsApi) {
      this.attachProgressive(startAt, play);
      return;
    }

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
    hls.on(Events.ERROR, (_, data) => this.handleHlsError(data));
    hls.attachMedia(this.audio);
  }

  private attachProgressive(startAt: number, play: boolean): void {
    this.destroyDriver();
    this.audio.dataset.playbackMode = "progressive";
    this.audio.dataset.sourceLoading = "false";
    // Присваивание src само заменяет источник — обнулять его отдельно не нужно.
    this.audio.src = mediaUrl.stream(this.request!.trackId);
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

  private schedulePreparationProbe(generation: number, url: string): void {
    if (this.preparationAttempts >= HLS_PREPARATION_ATTEMPTS) return;

    this.preparationAttempts += 1;
    this.clearRetryTimer();
    this.retryTimer = window.setTimeout(() => {
      void (async () => {
        if (generation !== this.generation || !this.request) return;
        if (!(await this.hlsReady(url))) {
          this.schedulePreparationProbe(generation, url);
          return;
        }

        const position = this.audio.currentTime;
        const shouldPlay = !this.audio.paused;
        this.attachAdaptive(url, position, shouldPlay);
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
      const response = await fetchWithSession(url, { signal: controller.signal });

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
