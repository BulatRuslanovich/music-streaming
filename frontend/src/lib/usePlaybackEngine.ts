// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { ComponentPropsWithoutRef, Dispatch, RefObject, SetStateAction } from "react";
import { api } from "@/lib/api";
import { AdaptivePlayback, warmUpHls } from "@/lib/adaptivePlayback";
import { bestFallbackTier } from "@/lib/audioFormats";
import { refreshSession } from "@/lib/http";
import { mediaUrl } from "@/lib/media";
import {
  createListeningTracker,
  historyThresholdFor,
  type ListeningTracker,
} from "@/lib/playbackTelemetry";
import type { PlaybackOrigin, RepeatMode } from "@/lib/playerTypes";
import { PlaybackRecovery } from "@/lib/playbackRecovery";
import { registerStreamWorker } from "@/lib/streamCache";
import type { AudioQuality, Track } from "@/lib/types";
import { useInvalidate } from "@/lib/useInvalidate";
import { useStreamPrefetch } from "@/lib/useStreamPrefetch";
import { useSettings } from "@/contexts/SettingsContext";
import { useT } from "@/contexts/I18nContext";
import { useToast } from "@/contexts/ToastContext";

export interface PlaybackEngineInput {
  currentTrack: Track | null;
  currentIndex: number;
  queue: Track[];
  orderRef: RefObject<number[]>;
  repeat: RepeatMode;
  isPlaying: boolean;
  setIsPlaying: Dispatch<SetStateAction<boolean>>;
  volume: number;
  muted: boolean;

  // INFO: откуда взялся текущий трек, если очередь пополнил не пользователь, а радио или диджей.
  resolveOrigin: (index: number) => PlaybackOrigin | null;
  onTrackEnded: () => void;
}

export interface PlaybackEngine {
  audioRef: RefObject<HTMLAudioElement | null>;
  audioProps: ComponentPropsWithoutRef<"audio">;

  position: number;
  duration: number;
  buffered: number;

  getPosition: () => number;
  getDuration: () => number;
  // INFO: снимок очереди сохраняет последнюю отсчитанную позицию, а не текущее время <audio>:
  // снимок могут снять в момент, когда источник уже пересобирается и currentTime обнулён.
  trackedPosition: () => number;

  seek: (seconds: number) => void;
  seekBy: (deltaSeconds: number) => void;
  seekTo: (seconds: number) => void;

  recoverSource: () => boolean;

  startQueue: (origin: PlaybackOrigin) => void;
  resetProgress: () => void;
  clearProgress: () => void;
  restoreProgress: (trackId: string | undefined, seconds: number) => void;
  resumeSavedPosition: (seconds: number) => void;
}

export function usePlaybackEngine({
  currentTrack,
  currentIndex,
  queue,
  orderRef,
  repeat,
  isPlaying,
  setIsPlaying,
  volume,
  muted,
  resolveOrigin,
  onTrackEnded,
}: PlaybackEngineInput): PlaybackEngine {
  const { notify } = useToast();
  const t = useT();
  const settings = useSettings();
  const invalidate = useInvalidate();

  const audioRef = useRef<HTMLAudioElement | null>(null);
  const adaptiveRef = useRef<AdaptivePlayback | null>(null);

  const [position, setPosition] = useState(0);
  const [duration, setDuration] = useState(0);
  const [buffered, setBuffered] = useState(0);
  const [sourceRevision, setSourceRevision] = useState(0);
  const [online, setOnline] = useState(() =>
    typeof navigator === "undefined" ? true : navigator.onLine,
  );

  const recordedRef = useRef<string | null>(null);
  const wasPlayingRef = useRef(false);

  const trackerRef = useRef<ListeningTracker | null>(null);
  const tracker = (trackerRef.current ??= createListeningTracker());

  const originRef = useRef<PlaybackOrigin>({});
  const pendingSeekRef = useRef<number | null>(null);
  const positionRef = useRef(0);
  const retryTimerRef = useRef<number | null>(null);

  // Вся память о сорванных источниках, откатах и деградации — в одном объекте.
  // Ленивый инициализатор useState, а не ref: так его стабильность видна и линтеру,
  // который иначе считает выражение присваивания меняющейся зависимостью хуков.
  const [recovery] = useState(() => new PlaybackRecovery());

  // INFO: после обрыва связи <audio> остаётся с мёртвым источником, а эффект ниже сравнивает
  // sourceKey и ничего не пересобирает — без пометки плеер залипал бы до перезагрузки страницы.
  const failSource = useCallback(
    (resume: boolean): boolean => {
      const first = recovery.fail(audioRef.current?.dataset.trackId, resume);
      setIsPlaying(false);

      return first;
    },
    [recovery, setIsPlaying],
  );

  // INFO: возвращает, слушал ли пользователь в момент обрыва, — решение о возобновлении за вызывающим.
  const recoverSource = useCallback((): boolean => {
    const resumed = recovery.recover();
    if (!resumed) return false;

    setSourceRevision((revision) => revision + 1);

    return resumed.resume;
  }, [recovery]);

  useEffect(() => {
    registerStreamWorker();

    // Чанк hls.js весит около 180 КБ в gzip и раньше скачивался в момент первого нажатия play,
    // то есть лежал прямо на пути к первому звуку. Тянем его заранее, но не на монтировании:
    // на узком канале он отнял бы полосу у контента страницы. Простой браузера — подходящий момент,
    // а если пользователь потянулся к play раньше, ждать простоя незачем.
    const warm = () => warmUpHls();
    const idle = window.requestIdleCallback?.(warm, { timeout: 10_000 }) ?? null;
    window.addEventListener("pointerdown", warm, { once: true, passive: true });

    const wentOnline = () => {
      setOnline(true);
      if (recoverSource()) setIsPlaying(true);
    };
    const wentOffline = () => setOnline(false);
    window.addEventListener("online", wentOnline);
    window.addEventListener("offline", wentOffline);

    return () => {
      if (idle !== null) window.cancelIdleCallback?.(idle);
      window.removeEventListener("pointerdown", warm);
      window.removeEventListener("online", wentOnline);
      window.removeEventListener("offline", wentOffline);
    };
  }, [recoverSource, setIsPlaying]);

  const { noteStall } = useStreamPrefetch({
    currentTrack,
    currentIndex,
    queue,
    orderRef,
    repeat,
    online,
    isPlaying,
    position,
    buffered,
    duration,
  });

  const seekTo = useCallback((seconds: number) => {
    const audio = audioRef.current;
    if (!audio) return;

    audio.currentTime = seconds;
    setPosition(seconds);
    positionRef.current = seconds;
  }, []);

  const seek = useCallback((seconds: number) => {
    const audio = audioRef.current;
    if (!audio) return;

    const clamped = Math.max(0, Math.min(seconds, audio.duration || seconds));
    audio.currentTime = clamped;
    setPosition(clamped);
    positionRef.current = clamped;
  }, []);

  const seekBy = useCallback(
    (deltaSeconds: number) => {
      const audio = audioRef.current;
      if (!audio) return;

      seek(audio.currentTime + deltaSeconds);
    },
    [seek],
  );

  const startQueue = useCallback(
    (origin: PlaybackOrigin) => {
      tracker.finish("trackSkipped", originRef.current);
      originRef.current = origin;

      setPosition(0);
      pendingSeekRef.current = null;
    },
    [tracker],
  );

  const resetProgress = useCallback(() => setPosition(0), []);

  const clearProgress = useCallback(() => {
    setPosition(0);
    setDuration(0);
  }, []);

  const restoreProgress = useCallback((trackId: string | undefined, seconds: number) => {
    const audio = audioRef.current;

    if (audio && trackId && audio.dataset.trackId !== trackId) {
      pendingSeekRef.current = seconds;
    }

    setPosition(seconds);
    positionRef.current = seconds;
  }, []);

  const resumeSavedPosition = useCallback((seconds: number) => {
    pendingSeekRef.current = seconds;
    setPosition(seconds);
  }, []);

  const applyPendingSeek = useCallback((audio: HTMLAudioElement) => {
    if (pendingSeekRef.current === null) return;

    const resumeAt = pendingSeekRef.current;
    pendingSeekRef.current = null;

    const applyResume = () => {
      audio.currentTime = resumeAt;
      audio.removeEventListener("loadedmetadata", applyResume);
    };
    audio.addEventListener("loadedmetadata", applyResume);
  }, []);

  const quality = settings.effectiveQuality;

  const fallbackTier = useMemo(() => bestFallbackTier(settings.qualities), [settings.qualities]);

  const tierFor = useCallback(
    (track: Track): AudioQuality =>
      recovery.tierFor(track, quality, settings.qualities, fallbackTier),
    [recovery, quality, fallbackTier, settings.qualities],
  );

  useEffect(() => {
    recovery.reset();
  }, [recovery, quality]);

  useEffect(() => {
    const audio = audioRef.current;
    if (!audio || !currentTrack) return;

    const forceAdaptive = recovery.forceAdaptive(quality, settings.networkIsSlow, currentTrack.id);
    const sourceKey = `${currentTrack.id}:${quality}:${forceAdaptive ? "adaptive" : "direct"}:${sourceRevision}`;
    if (audio.dataset.sourceKey === sourceKey) return;

    recovery.clearFailure();

    const staysOnSameTrack = audio.dataset.trackId === currentTrack.id;

    if (retryTimerRef.current !== null) {
      window.clearTimeout(retryTimerRef.current);
      retryTimerRef.current = null;
    }

    if (staysOnSameTrack) {
      pendingSeekRef.current = audio.currentTime || positionRef.current;
    } else {
      recordedRef.current = null;

      const automatic = resolveOrigin(currentIndex);
      if (automatic) originRef.current = automatic;

      tracker.finish("trackSkipped", originRef.current);
      tracker.begin(currentTrack, originRef.current);
    }

    const startAt = staysOnSameTrack
      ? audio.currentTime || positionRef.current
      : (pendingSeekRef.current ?? 0);
    pendingSeekRef.current = null;

    audio.dataset.trackId = currentTrack.id;
    audio.dataset.sourceKey = sourceKey;
    positionRef.current = startAt;
    setDuration(currentTrack.durationSeconds || 0);

    const reportLoadFailure = () => {
      const offline = typeof navigator !== "undefined" && !navigator.onLine;
      if (!failSource(isPlaying)) return;

      if (offline) notify(t("player.offlineWaiting"), "info");
      else notify(t("player.trackLoadFailed", { title: currentTrack.title }), "error");
    };

    adaptiveRef.current?.destroy();
    const playback = new AdaptivePlayback(audio, {
      onFatalError: () => reportLoadFailure(),
    });
    adaptiveRef.current = playback;

    void playback
      .load({
        trackId: currentTrack.id,
        codec: currentTrack.codec,
        quality,
        qualities: settings.qualities,
        hlsEnabled: settings.hlsEnabled,
        forceAdaptive,
        slowNetwork: settings.networkIsSlow || settings.dataSaver,
        startAt,
        play: isPlaying,
      })
      .then(({ tier }) => {
        if (adaptiveRef.current === playback) recovery.loaded(currentTrack.id, tier);
      })
      .catch(() => {
        if (adaptiveRef.current === playback) reportLoadFailure();
      });
  }, [
    currentTrack,
    currentIndex,
    resolveOrigin,
    quality,
    sourceRevision,
    isPlaying,
    settings.hlsEnabled,
    settings.networkIsSlow,
    settings.dataSaver,
    settings.qualities,
    notify,
    t,
    tracker,
    recovery,
    failSource,
  ]);

  useEffect(
    () => () => {
      if (retryTimerRef.current !== null) window.clearTimeout(retryTimerRef.current);
      adaptiveRef.current?.destroy();
    },
    [],
  );

  useEffect(() => {
    const audio = audioRef.current;
    if (!audio) return;

    if (isPlaying && audio.dataset.sourceLoading !== "true") {
      audio
        .play()
        .catch((reason: unknown) => {
          const name = reason instanceof DOMException ? reason.name : "";
          if (name !== "AbortError") {
            setIsPlaying(false);
            notify(
              name === "NotAllowedError" ? t("player.autoplayBlocked") : t("player.trackFailed"),
              "error",
            );
          }
        })
        .catch(() => {});
    } else {
      audio.pause();

      if (wasPlayingRef.current) tracker.pause(originRef.current);
    }

    wasPlayingRef.current = isPlaying;
  }, [isPlaying, currentTrack, notify, t, tracker, setIsPlaying]);

  useEffect(() => {
    const audio = audioRef.current;
    if (audio) {
      audio.volume = volume;
      audio.muted = muted;
    }
  }, [volume, muted]);

  const handleTimeUpdate = useCallback(() => {
    const audio = audioRef.current;
    if (!audio) return;

    tracker.accumulate(audio.currentTime, originRef.current);

    setPosition(audio.currentTime);
    positionRef.current = audio.currentTime;

    const track = currentTrack;
    if (!track || recordedRef.current === track.id) return;

    const threshold = historyThresholdFor(track.durationSeconds, settings.historyThresholdSeconds);
    if (audio.currentTime >= threshold) {
      recordedRef.current = track.id;

      void api
        .recordPlay(track.id, Math.floor(audio.currentTime))
        .then(() => invalidate("history"))
        .catch(() => {});
    }
  }, [currentTrack, tracker, settings.historyThresholdSeconds, invalidate]);

  const handleProgress = useCallback(() => {
    const audio = audioRef.current;
    if (!audio) return;

    const ranges = audio.buffered;
    setBuffered(ranges.length > 0 ? ranges.end(ranges.length - 1) : 0);
  }, []);

  const handleEnded = useCallback(() => {
    tracker.finish("trackCompleted", originRef.current);

    if (repeat === "one") {
      seekTo(0);

      if (currentTrack) tracker.begin(currentTrack, originRef.current);

      const audio = audioRef.current;
      void audio?.play().catch(() => setIsPlaying(false));
      return;
    }

    onTrackEnded();
  }, [onTrackEnded, repeat, tracker, currentTrack, seekTo, setIsPlaying]);

  const handleError = useCallback(() => {
    const audio = audioRef.current;
    if (!audio || !currentTrack) return;
    if (audio.dataset.sourceLoading === "true") return;
    if (audio.dataset.playbackMode !== "progressive") return;

    const resumeAt = audio.currentTime > 0 ? audio.currentTime : positionRef.current;
    const shouldResume = isPlaying || resumeAt > 0;

    const decision = recovery.decide({
      trackId: currentTrack.id,
      errorCode: audio.error?.code,
      offline: typeof navigator !== "undefined" && !navigator.onLine,
      fallbackTier,
      tier: tierFor(currentTrack),
    });

    if (decision.kind === "offline") {
      if (failSource(isPlaying)) notify(t("player.offlineWaiting"), "info");
      return;
    }

    if (decision.kind === "unsupported") {
      setIsPlaying(false);
      notify(t("player.formatUnsupported", { title: currentTrack.title }), "error");
      return;
    }

    if (decision.kind === "giveUp") {
      if (failSource(isPlaying)) {
        notify(t("player.trackLoadFailed", { title: currentTrack.title }), "error");
      }
      return;
    }

    if (decision.kind === "fallback") {
      // Откат и выдержку `recovery.decide` уже записал за себя — здесь только последствия.
      pendingSeekRef.current = resumeAt;
      setSourceRevision((revision) => revision + 1);

      notify(t("player.preparingPlayable"), "info");
      return;
    }

    const { attempt, tier } = decision;

    retryTimerRef.current = window.setTimeout(() => {
      retryTimerRef.current = null;

      const element = audioRef.current;
      if (!element || element.dataset.trackId !== currentTrack.id) return;

      const retry = () => {
        pendingSeekRef.current = resumeAt;
        element.src = mediaUrl.stream(currentTrack.id, tier);
        applyPendingSeek(element);
        element.load();

        if (shouldResume) void element.play().catch(() => {});
      };

      if (attempt === 0) void refreshSession().then(retry);
      else retry();
    }, decision.delayMs);
  }, [
    currentTrack,
    isPlaying,
    tierFor,
    fallbackTier,
    notify,
    t,
    applyPendingSeek,
    recovery,
    failSource,
    setIsPlaying,
  ]);

  const handleWaiting = useCallback(() => {
    const audio = audioRef.current;
    noteStall();

    // Только Original: понижать имеет смысл там, где есть куда понижать. Слушателю на Normal,
    // которому отдали оригинал из-за неготового HLS, пересборка источника не поможет — его
    // подхватит schedulePreparationProbe, как только рендишен доготовится.
    if (
      !audio ||
      !currentTrack ||
      quality !== "Original" ||
      audio.dataset.playbackMode !== "progressive" ||
      audio.currentTime <= 0 ||
      recovery.coolingDown()
    ) {
      return;
    }

    const ranges = audio.buffered;
    const bufferedUntil = ranges.length > 0 ? ranges.end(ranges.length - 1) : audio.currentTime;
    if (bufferedUntil - audio.currentTime > 2) return;

    pendingSeekRef.current = audio.currentTime;
    recovery.degrade();
    setSourceRevision((revision) => revision + 1);
    notify(t("player.networkDegraded"), "info");
  }, [currentTrack, quality, notify, t, recovery, noteStall]);

  const getPosition = useCallback(() => audioRef.current?.currentTime ?? positionRef.current, []);

  const getDuration = useCallback(() => {
    const decoded = audioRef.current?.duration;
    return decoded !== undefined && Number.isFinite(decoded) ? decoded : 0;
  }, []);

  const trackedPosition = useCallback(() => positionRef.current, []);

  const audioProps: ComponentPropsWithoutRef<"audio"> = {
    preload: "metadata",
    onTimeUpdate: handleTimeUpdate,
    onProgress: handleProgress,
    onLoadedMetadata: (event) => setDuration(event.currentTarget.duration || 0),
    onDurationChange: (event) => setDuration(event.currentTarget.duration || 0),
    onEnded: handleEnded,
    onError: handleError,
    onWaiting: handleWaiting,
    onStalled: handleWaiting,
    onPlay: () => setIsPlaying(true),
    onPause: (event) => {
      if (event.currentTarget.dataset.sourceLoading !== "true") setIsPlaying(false);
    },
    onPlaying: () => recovery.playing(),
  };

  return {
    audioRef,
    audioProps,
    position,
    duration,
    buffered,
    getPosition,
    getDuration,
    trackedPosition,
    seek,
    seekBy,
    seekTo,
    recoverSource,
    startQueue,
    resetProgress,
    clearProgress,
    restoreProgress,
    resumeSavedPosition,
  };
}
