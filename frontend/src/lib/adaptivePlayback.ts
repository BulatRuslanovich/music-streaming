// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import Hls, { ErrorTypes, Events, type ErrorData } from "hls.js";
import { playableTier } from "@/lib/audioFormats";
import { createSessionAwareLoader } from "@/lib/hlsSessionLoader";
import { refreshSession } from "@/lib/http";
import { mediaUrl } from "@/lib/media";
import type { AudioQuality, AudioQualityOption } from "@/lib/types";

export type AdaptiveQuality = Exclude<AudioQuality, "Original">;
export type PlaybackTransport = "hls.js" | "native-hls" | "progressive";

export interface PlaybackRequest {
  trackId: string;
  codec?: string | null;
  quality: AudioQuality;
  qualities: AudioQualityOption[];
  hlsEnabled: boolean;
  forceAdaptive: boolean;
  startAt: number;
  play: boolean;
}

export interface PlaybackLoadResult {
  transport: PlaybackTransport;
  tier: AudioQuality;
}

interface PlaybackCallbacks {
  onFatalError: () => void;
  onLevelChanged?: (quality: AdaptiveQuality) => void;
}

interface TransportSupport {
  hlsJs: boolean;
  nativeHls: boolean;
}

const HLS_RETRY_DELAYS = [800, 2500, 6000];
const HLS_PREPARATION_RETRY_MS = 10_000;
const HLS_PREPARATION_ATTEMPTS = 6;

const SessionAwareLoader = createSessionAwareLoader();

export function adaptiveCap(quality: AudioQuality): AdaptiveQuality {
  return quality === "Original" ? "High" : quality;
}

export function choosePlaybackTransport(
  request: Pick<PlaybackRequest, "quality" | "hlsEnabled" | "forceAdaptive"> & {
    progressiveTier: AudioQuality;
  },
  support: TransportSupport,
): PlaybackTransport {
  const adaptiveWanted = request.forceAdaptive || request.progressiveTier !== "Original";
  if (!request.hlsEnabled || !adaptiveWanted) return "progressive";
  if (support.hlsJs) return "hls.js";
  if (support.nativeHls) return "native-hls";
  return "progressive";
}

export class AdaptivePlayback {
  private readonly audio: HTMLAudioElement;
  private readonly callbacks: PlaybackCallbacks;
  private hls: Hls | null = null;
  private request: PlaybackRequest | null = null;
  private generation = 0;
  private retryTimer: number | null = null;
  private retries = 0;
  private preparationAttempts = 0;

  transport: PlaybackTransport = "progressive";

  constructor(audio: HTMLAudioElement, callbacks: PlaybackCallbacks) {
    this.audio = audio;
    this.callbacks = callbacks;
    this.audio.addEventListener("error", this.handleNativeError);
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
    this.audio.pause();
    this.audio.removeAttribute("src");
    this.audio.load();

    const progressiveTier = playableTier(request.codec, request.quality, request.qualities);
    const support = {
      hlsJs: Hls.isSupported(),
      nativeHls: this.audio.canPlayType("application/vnd.apple.mpegurl") !== "",
    };
    const wanted = choosePlaybackTransport({ ...request, progressiveTier }, support);

    if (wanted !== "progressive") {
      const cap = adaptiveCap(request.quality);
      const url = mediaUrl.hls(request.trackId, cap);
      if (await this.hlsReady(url)) {
        if (generation !== this.generation)
          return { transport: this.transport, tier: progressiveTier };
        this.attachAdaptive(wanted, url, cap, request.startAt, request.play);
        return { transport: wanted, tier: cap };
      }

      this.schedulePreparationProbe(generation, wanted, url, cap);
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
    this.audio.removeEventListener("error", this.handleNativeError);
  }

  private attachAdaptive(
    transport: Exclude<PlaybackTransport, "progressive">,
    url: string,
    cap: AdaptiveQuality,
    startAt: number,
    play: boolean,
  ): void {
    this.destroyDriver();
    this.transport = transport;
    this.audio.dataset.playbackMode = transport;
    this.audio.dataset.sourceLoading = "false";

    if (transport === "hls.js") {
      const hls = new Hls({
        loader: SessionAwareLoader,
        startLevel: -1,
        abrEwmaDefaultEstimate: 128_000,
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
      return;
    }

    this.audio.src = url;
    this.audio.load();
    this.resumeAt(startAt, play);
  }

  private attachProgressive(tier: AudioQuality, startAt: number, play: boolean): void {
    this.destroyDriver();
    this.transport = "progressive";
    this.audio.dataset.playbackMode = "progressive";
    this.audio.dataset.sourceLoading = "false";
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
    if (!data.fatal || !this.hls) return;

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

  private readonly handleNativeError = () => {
    if (this.transport !== "native-hls" || !this.request) return;

    const position = this.audio.currentTime;
    const shouldPlay = !this.audio.paused;
    const cap = adaptiveCap(this.request.quality);
    const url = mediaUrl.hls(this.request.trackId, cap);

    // Нативный HLS грузит сегменты сам, подменить загрузчик там нечем, поэтому
    // единственный доступный ответ на протухшую сессию — продлить её вслепую
    // перед первым же повтором.
    const firstAttempt = this.retries === 0;
    const retry = () => this.attachAdaptive("native-hls", url, cap, position, shouldPlay);

    if (this.scheduleRetry(() => (firstAttempt ? void refreshSession().then(retry) : retry()))) {
      return;
    }

    this.callbacks.onFatalError();
  };

  private scheduleRetry(action: () => void): boolean {
    if (this.retries >= HLS_RETRY_DELAYS.length) return false;
    const delay = HLS_RETRY_DELAYS[this.retries++];
    this.clearRetryTimer();
    this.retryTimer = window.setTimeout(action, delay);
    return true;
  }

  private schedulePreparationProbe(
    generation: number,
    transport: Exclude<PlaybackTransport, "progressive">,
    url: string,
    cap: AdaptiveQuality,
  ): void {
    if (this.preparationAttempts >= HLS_PREPARATION_ATTEMPTS) return;

    this.preparationAttempts += 1;
    this.clearRetryTimer();
    this.retryTimer = window.setTimeout(() => {
      void (async () => {
        if (generation !== this.generation || !this.request) return;
        if (!(await this.hlsReady(url))) {
          this.schedulePreparationProbe(generation, transport, url, cap);
          return;
        }

        const position = this.audio.currentTime;
        const shouldPlay = !this.audio.paused;
        this.attachAdaptive(transport, url, cap, position, shouldPlay);
      })();
    }, HLS_PREPARATION_RETRY_MS);
  }

  private async hlsReady(url: string): Promise<boolean> {
    const controller = new AbortController();
    const timeout = window.setTimeout(() => controller.abort(), 5_000);
    const fetchManifest = () =>
      fetch(url, { credentials: "include", cache: "no-store", signal: controller.signal });

    try {
      let response = await fetchManifest();
      if (response.status === 401 && (await refreshSession())) response = await fetchManifest();
      await response.body?.cancel().catch(() => {});
      return response.ok && response.status !== 202;
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
