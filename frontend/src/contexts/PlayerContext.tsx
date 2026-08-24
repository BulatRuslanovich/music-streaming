// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import React, { createContext, useCallback, useEffect, useMemo, useRef, useState } from "react";
import { api } from "@/lib/api";
import { AdaptivePlayback } from "@/lib/adaptivePlayback";
import { bestFallbackTier, playableTier } from "@/lib/audioFormats";
import {
  defaultDjVariety,
  mergeDjBatch,
  recommendationReasons,
  validDjSession,
} from "@/lib/djSession";
import { recordEvent } from "@/lib/events";
import { refreshSession } from "@/lib/http";
import { mediaUrl } from "@/lib/media";
import { createListeningTracker, type ListeningTracker } from "@/lib/playbackTelemetry";
import { useRequiredContext } from "@/lib/useRequiredContext";
import {
  advanceIn,
  appendTrack,
  appendTracks,
  buildOrder,
  indexAfterRemoval,
  insertAfter,
  moveInQueue as reorderQueue,
  radioStartAfterInsert,
  remapIndexAfterMove,
} from "@/lib/playerQueue";
import type {
  PlaybackOrigin,
  PlayerActions,
  DjSessionState,
  PlayerProgress,
  PlayerState,
  QueueSnapshot,
  RadioState,
  RepeatMode,
} from "@/lib/playerTypes";
import { adaptiveCooldownMs, decideRecovery } from "@/lib/streamRecovery";
import {
  pinStreamTracks,
  prefetchHlsTracks,
  readyToPrefetch,
  registerStreamWorker,
} from "@/lib/streamCache";
import type { AudioQuality, DjMode, DjVariety, Track } from "@/lib/types";
import { useExclusivePlayback } from "@/lib/useExclusivePlayback";
import { useInvalidate } from "@/lib/useInvalidate";
import { useMediaSession } from "@/lib/useMediaSession";
import { readPersistedPlayer, usePersistedPlayer } from "@/lib/usePlayerStorage";
import { useSettings } from "./SettingsContext";
import { useT } from "./I18nContext";
import { useToast } from "./ToastContext";

export type { PlaybackOrigin, QueueSnapshot, RadioState, RepeatMode } from "@/lib/playerTypes";

const PlayerStateContext = createContext<PlayerState | null>(null);

const PlayerActionsContext = createContext<PlayerActions | null>(null);

const PlayerProgressContext = createContext<PlayerProgress | null>(null);

const RADIO_PREFETCH_AT = 1;

const DJ_INITIAL_BATCH = 10;

const DJ_NEXT_BATCH = 5;

export function PlayerProvider({ children }: { children: React.ReactNode }) {
  const { notify, notifyError } = useToast();
  const t = useT();
  const settings = useSettings();
  const invalidate = useInvalidate();
  const audioRef = useRef<HTMLAudioElement | null>(null);
  const adaptiveRef = useRef<AdaptivePlayback | null>(null);

  const [queue, setQueue] = useState<Track[]>([]);
  const [currentIndex, setCurrentIndex] = useState(-1);
  const [isPlaying, setIsPlaying] = useState(false);
  const [position, setPosition] = useState(0);
  const [duration, setDuration] = useState(0);
  const [buffered, setBuffered] = useState(0);
  const [volume, setVolumeState] = useState(1);
  const [muted, setMuted] = useState(false);
  const [shuffle, setShuffle] = useState(false);
  const [repeat, setRepeat] = useState<RepeatMode>("off");
  const [radio, setRadio] = useState<RadioState>("idle");
  const [dj, setDj] = useState<DjSessionState | null>(null);
  const [djLoading, setDjLoading] = useState(false);
  const [restored, setRestored] = useState(false);
  const [sourceRevision, setSourceRevision] = useState(0);
  const [online, setOnline] = useState(() =>
    typeof navigator === "undefined" ? true : navigator.onLine,
  );

  const orderRef = useRef<number[]>([]);
  const recordedRef = useRef<string | null>(null);

  const wasPlayingRef = useRef(false);

  const queueRef = useRef<Track[]>([]);

  const applyQueue = useCallback((next: Track[], order: number[]) => {
    queueRef.current = next;
    orderRef.current = order;
    setQueue(next);
  }, []);

  const trackerRef = useRef<ListeningTracker | null>(null);
  const tracker = (trackerRef.current ??= createListeningTracker());

  const originRef = useRef<PlaybackOrigin>({});
  const pendingSeekRef = useRef<number | null>(null);
  const positionRef = useRef(0);
  const retryRef = useRef<{ trackId: string; tier: AudioQuality; attempts: number }>({
    trackId: "",
    tier: "Original",
    attempts: 0,
  });
  const retryTimerRef = useRef<number | null>(null);

  const fellBackRef = useRef(new Set<string>());
  const degradedUntilRef = useRef(0);
  const degradationsRef = useRef(0);
  const adaptiveOriginalTrackRef = useRef<string | null>(null);
  const lastStallAtRef = useRef(0);
  const prefetchRef = useRef<{ key: string; controller: AbortController } | null>(null);
  const prefetchRetryAtRef = useRef(0);

  const radioRef = useRef<{ inFlight: boolean; seed: string | null }>({
    inFlight: false,
    seed: null,
  });

  const radioFromRef = useRef(Number.MAX_SAFE_INTEGER);
  const djGenerationRef = useRef(0);
  const djInFlightRef = useRef(false);
  const currentTrack = currentIndex >= 0 ? (queue[currentIndex] ?? null) : null;

  useEffect(() => {
    /* eslint-disable react-hooks/set-state-in-effect -- // INFO: восстанавливаем сохранённое состояние проигрывателя только при монтировании. */
    const saved = readPersistedPlayer();
    if (saved) {
      if (Array.isArray(saved.queue) && saved.queue.length > 0) {
        applyQueue(
          saved.queue,
          saved.queue.map((_, index) => index),
        );

        const index = typeof saved.index === "number" ? saved.index : 0;
        if (index >= 0 && index < saved.queue.length) {
          setCurrentIndex(index);
          pendingSeekRef.current = saved.position ?? 0;
          setPosition(saved.position ?? 0);
        }
      }

      if (typeof saved.volume === "number") setVolumeState(saved.volume);
      if (typeof saved.muted === "boolean") setMuted(saved.muted);
      if (typeof saved.shuffle === "boolean") setShuffle(saved.shuffle);
      if (saved.repeat === "off" || saved.repeat === "all" || saved.repeat === "one") {
        setRepeat(saved.repeat);
      }
      if (validDjSession(saved.dj)) setDj(saved.dj);
    }

    setRestored(true);
    /* eslint-enable react-hooks/set-state-in-effect -- // INFO: дальнейшие эффекты не должны менять состояние синхронно. */
  }, [applyQueue]);

  usePersistedPlayer(
    { queue, index: currentIndex, position, volume, muted, shuffle, repeat, dj },
    restored,
    isPlaying,
  );

  useEffect(() => {
    registerStreamWorker();

    const wentOnline = () => setOnline(true);
    const wentOffline = () => setOnline(false);
    window.addEventListener("online", wentOnline);
    window.addEventListener("offline", wentOffline);

    return () => {
      window.removeEventListener("online", wentOnline);
      window.removeEventListener("offline", wentOffline);
      prefetchRef.current?.controller.abort();
    };
  }, []);

  const replaceQueue = useCallback(
    (tracks: Track[], startIndex = 0, origin: PlaybackOrigin = {}) => {
      if (tracks.length === 0) return;

      const safeIndex = Math.min(Math.max(startIndex, 0), tracks.length - 1);

      tracker.finish("trackSkipped", originRef.current);
      originRef.current = origin;

      radioRef.current = { inFlight: false, seed: null };
      radioFromRef.current = Number.MAX_SAFE_INTEGER;
      setRadio("idle");

      applyQueue(tracks, buildOrder(tracks.length, shuffle, safeIndex));
      setCurrentIndex(safeIndex);
      setPosition(0);
      setIsPlaying(true);
      pendingSeekRef.current = null;
    },
    [applyQueue, shuffle, tracker],
  );

  const playQueue = useCallback(
    (tracks: Track[], startIndex = 0, origin: PlaybackOrigin = {}) => {
      djGenerationRef.current += 1;
      djInFlightRef.current = false;
      setDj(null);
      setDjLoading(false);
      replaceQueue(tracks, startIndex, origin);
    },
    [replaceQueue],
  );

  const startDj = useCallback(
    async (mode: DjMode, seedTrack: Track | null = null) => {
      const generation = ++djGenerationRef.current;
      const variety = defaultDjVariety(mode);
      djInFlightRef.current = false;
      setDj((session) => (session ? { ...session, status: "idle" } : session));
      setDjLoading(true);

      try {
        const batch = await api.dj(
          mode,
          variety,
          seedTrack?.id ?? null,
          [],
          DJ_INITIAL_BATCH - (seedTrack ? 1 : 0),
        );
        if (generation !== djGenerationRef.current) return false;

        if (batch.tracks.length === 0 && !seedTrack) {
          notify(t("dj.empty"), "info");
          return false;
        }

        const tracks = [
          ...(seedTrack ? [seedTrack] : []),
          ...batch.tracks.map((item) => item.track),
        ];
        djInFlightRef.current = false;
        replaceQueue(tracks, 0, { source: "dj", sourceId: batch.seedTrackId ?? undefined });
        setDj({
          mode: batch.mode,
          variety: batch.variety,
          seedTrackId: batch.seedTrackId,
          status: "idle",
          reasons: recommendationReasons(batch.tracks),
        });
        return true;
      } catch (error) {
        if (generation === djGenerationRef.current) notifyError(error, t("dj.failed"));
        return false;
      } finally {
        if (generation === djGenerationRef.current) setDjLoading(false);
      }
    },
    [notify, notifyError, replaceQueue, t],
  );

  const setDjVariety = useCallback((variety: DjVariety) => {
    setDj((session) => (session ? { ...session, variety, status: "idle" } : session));
  }, []);

  const playTrack = useCallback(
    (track: Track, contextTracks?: Track[], origin: PlaybackOrigin = {}) => {
      if (contextTracks && contextTracks.length > 0) {
        const index = contextTracks.findIndex((candidate) => candidate.id === track.id);
        playQueue(contextTracks, index >= 0 ? index : 0, origin);
        return;
      }

      playQueue([track], 0, origin);
    },
    [playQueue],
  );

  const seekInternal = useCallback((seconds: number) => {
    const audio = audioRef.current;
    if (!audio) return;

    audio.currentTime = seconds;
    setPosition(seconds);
    positionRef.current = seconds;
  }, []);

  const advance = useCallback(
    (direction: 1 | -1, { auto = false }: { auto?: boolean } = {}) => {
      const step = advanceIn(orderRef.current, currentIndex, direction, repeat === "all");

      switch (step.kind) {
        case "none":
          return;

        case "restart":
          seekInternal(0);
          return;

        case "stop":
          setIsPlaying(false);
          if (auto) seekInternal(0);
          return;

        case "play":
          setCurrentIndex(step.index);
          setPosition(0);
          setIsPlaying(true);
      }
    },
    [currentIndex, repeat, seekInternal],
  );

  const next = useCallback(() => advance(1), [advance]);
  const previous = useCallback(() => {
    if (audioRef.current && audioRef.current.currentTime > 3) {
      seekInternal(0);
      return;
    }
    advance(-1);
  }, [advance, seekInternal]);

  const toggle = useCallback(() => {
    if (!currentTrack) return;
    setIsPlaying((playing) => !playing);
  }, [currentTrack]);

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

  const setVolume = useCallback((next: number) => {
    const clamped = Math.max(0, Math.min(1, next));
    setVolumeState(clamped);
    if (clamped > 0) setMuted(false);
  }, []);

  const toggleMute = useCallback(() => setMuted((value) => !value), []);

  const toggleShuffle = useCallback(() => {
    const nowShuffled = !shuffle;

    orderRef.current = buildOrder(queue.length, nowShuffled, currentIndex);
    setShuffle(nowShuffled);
  }, [queue.length, currentIndex, shuffle]);

  const cycleRepeat = useCallback(() => {
    setRepeat((mode) => (mode === "off" ? "all" : mode === "all" ? "one" : "off"));
  }, []);

  const addToQueue = useCallback(
    (track: Track) => {
      recordEvent({ type: "trackAddedToQueue", trackId: track.id });

      const next = appendTrack(queueRef.current, orderRef.current, track);
      applyQueue(next.queue, next.order);

      setCurrentIndex((index) => (index < 0 ? 0 : index));
    },
    [applyQueue],
  );

  const playNext = useCallback(
    (track: Track) => {
      const current = queueRef.current;
      if (current.length === 0 || currentIndex < 0) {
        addToQueue(track);
        return;
      }

      recordEvent({ type: "trackAddedToQueue", trackId: track.id });

      const next = insertAfter(current, orderRef.current, currentIndex, track);

      radioFromRef.current = radioStartAfterInsert(
        radioFromRef.current,
        currentIndex + 1,
        current.length,
      );

      applyQueue(next.queue, next.order);
    },
    [addToQueue, applyQueue, currentIndex],
  );

  const removeFromQueue = useCallback(
    (index: number) => {
      const current = queueRef.current;
      if (index < 0 || index >= current.length) return;

      const remaining = current.filter((_, position) => position !== index);
      applyQueue(remaining, buildOrder(remaining.length, shuffle, -1));

      setCurrentIndex((activeIndex) => indexAfterRemoval(index, activeIndex, remaining.length));
    },
    [applyQueue, shuffle],
  );

  const moveInQueue = useCallback(
    (from: number, to: number) => {
      const current = queueRef.current;
      const next = reorderQueue(current, orderRef.current, from, to, shuffle);
      if (next.queue === current) return;

      applyQueue(next.queue, next.order);
      setCurrentIndex((index) => (index < 0 ? index : remapIndexAfterMove(from, to, index)));
    },
    [applyQueue, shuffle],
  );

  const snapshotQueue = useCallback(
    (): QueueSnapshot => ({
      queue: queueRef.current,
      order: [...orderRef.current],
      index: currentIndex,
      position: positionRef.current,
      radioFrom: radioFromRef.current,
      dj,
    }),
    [currentIndex, dj],
  );

  const restoreQueue = useCallback(
    (snapshot: QueueSnapshot) => {
      const audio = audioRef.current;
      const trackId = snapshot.queue[snapshot.index]?.id;

      if (audio && trackId && audio.dataset.trackId !== trackId) {
        pendingSeekRef.current = snapshot.position;
      }

      radioFromRef.current = snapshot.radioFrom;
      djGenerationRef.current += 1;
      djInFlightRef.current = false;
      setDj(snapshot.dj);

      applyQueue(snapshot.queue, snapshot.order);
      setCurrentIndex(snapshot.index);
      setPosition(snapshot.position);
      positionRef.current = snapshot.position;
    },
    [applyQueue],
  );

  const clearQueue = useCallback(() => {
    djGenerationRef.current += 1;
    djInFlightRef.current = false;
    setDj(null);
    setDjLoading(false);
    radioRef.current = { inFlight: false, seed: null };
    radioFromRef.current = Number.MAX_SAFE_INTEGER;
    setRadio("idle");

    applyQueue([], []);
    setCurrentIndex(-1);
    setIsPlaying(false);
    setPosition(0);
    setDuration(0);
  }, [applyQueue]);

  const jumpTo = useCallback(
    (index: number) => {
      if (index < 0 || index >= queue.length) return;

      setCurrentIndex(index);
      setPosition(0);
      setIsPlaying(true);
    },
    [queue.length],
  );

  const patchTrack = useCallback(
    (trackId: string, changes: Partial<Track>) => {
      applyQueue(
        queueRef.current.map((track) => (track.id === trackId ? { ...track, ...changes } : track)),
        orderRef.current,
      );
    },
    [applyQueue],
  );

  useEffect(() => {
    if (dj || !settings.autoplay || currentIndex < 0 || repeat !== "off") return;

    const order = orderRef.current;
    const position = order.indexOf(currentIndex);
    if (position < 0 || order.length - position - 1 > RADIO_PREFETCH_AT) return;

    const seed = queue[currentIndex]?.id ?? null;
    if (radioRef.current.inFlight || radioRef.current.seed === seed) return;

    radioRef.current = { inFlight: true, seed };
    setRadio("loading");

    void api
      .radio(
        seed,
        queue.map((track) => track.id),
      )
      .then((batch) => {
        const tracks = batch.tracks.map((item) => item.track);

        if (tracks.length === 0) {
          setRadio("empty");
          return;
        }

        const current = queueRef.current;
        const known = new Set(current.map((track) => track.id));
        const fresh = tracks.filter((track) => !known.has(track.id));

        if (fresh.length === 0) {
          setRadio("idle");
          return;
        }

        radioFromRef.current = Math.min(radioFromRef.current, current.length);

        const next = appendTracks(current, orderRef.current, fresh);
        applyQueue(next.queue, next.order);

        setRadio("idle");
      })
      .catch(() => setRadio("failed"))
      .finally(() => {
        radioRef.current = { ...radioRef.current, inFlight: false };
      });
  }, [dj, settings.autoplay, currentIndex, queue, repeat, applyQueue]);

  useEffect(() => {
    if (!dj || djLoading || dj.status === "empty" || currentIndex < 0 || repeat !== "off") return;

    const order = orderRef.current;
    const position = order.indexOf(currentIndex);
    if (position < 0 || order.length - position - 1 > RADIO_PREFETCH_AT) return;
    if (djInFlightRef.current) return;

    const generation = djGenerationRef.current;
    const seed =
      dj.mode === "Flow"
        ? (queue[currentIndex]?.id ?? dj.seedTrackId ?? null)
        : (dj.seedTrackId ?? null);

    djInFlightRef.current = true;
    setDj((session) => (session ? { ...session, status: "loading" } : session));

    void api
      .dj(
        dj.mode,
        dj.variety,
        seed,
        queue.map((track) => track.id),
        DJ_NEXT_BATCH,
      )
      .then((batch) => {
        if (generation !== djGenerationRef.current) return;

        const merged = mergeDjBatch(queueRef.current, dj.reasons, batch.tracks);

        if (merged.tracks.length === 0) {
          setDj((session) => (session ? { ...session, status: "empty" } : session));
          return;
        }

        const next = appendTracks(queueRef.current, orderRef.current, merged.tracks);
        applyQueue(next.queue, next.order);
        setDj((session) =>
          session
            ? {
                ...session,
                seedTrackId: batch.seedTrackId,
                status: "idle",
                reasons: merged.reasons,
              }
            : session,
        );
      })
      .catch(() => {
        if (generation === djGenerationRef.current) {
          setDj((session) => (session ? { ...session, status: "failed" } : session));
        }
      })
      .finally(() => {
        if (generation === djGenerationRef.current) djInFlightRef.current = false;
      });
  }, [dj, djLoading, currentIndex, queue, repeat, applyQueue]);

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
    (track: Track): AudioQuality => {
      if (quality === "Original" && fellBackRef.current.has(track.id)) {
        return fallbackTier ?? "Original";
      }

      return playableTier(track.codec, quality, settings.qualities);
    },
    [quality, fallbackTier, settings.qualities],
  );

  useEffect(() => {
    fellBackRef.current.clear();
    adaptiveOriginalTrackRef.current = null;
    degradationsRef.current = 0;
  }, [quality]);

  const degrade = useCallback(() => {
    degradedUntilRef.current = Date.now() + adaptiveCooldownMs(degradationsRef.current);
    degradationsRef.current += 1;
  }, []);

  useEffect(() => {
    const audio = audioRef.current;
    if (!audio || !currentTrack) return;

    if (
      quality === "Original" &&
      settings.networkIsSlow &&
      degradedUntilRef.current <= Date.now()
    ) {
      degrade();
    }

    const forceAdaptive =
      quality === "Original" &&
      (settings.networkIsSlow ||
        degradedUntilRef.current > Date.now() ||
        adaptiveOriginalTrackRef.current === currentTrack.id);
    if (forceAdaptive) adaptiveOriginalTrackRef.current = currentTrack.id;
    const sourceKey = `${currentTrack.id}:${quality}:${forceAdaptive ? "adaptive" : "direct"}:${sourceRevision}`;
    if (audio.dataset.sourceKey === sourceKey) return;

    const staysOnSameTrack = audio.dataset.trackId === currentTrack.id;

    if (retryTimerRef.current !== null) {
      window.clearTimeout(retryTimerRef.current);
      retryTimerRef.current = null;
    }

    if (staysOnSameTrack) {
      pendingSeekRef.current = audio.currentTime || positionRef.current;
    } else {
      recordedRef.current = null;

      if (dj) {
        originRef.current = { source: "dj", sourceId: dj.seedTrackId ?? undefined };
      } else if (currentIndex >= radioFromRef.current) {
        originRef.current = { source: "radio", sourceId: queue[radioFromRef.current - 1]?.id };
      }

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

    adaptiveRef.current?.destroy();
    const playback = new AdaptivePlayback(audio, {
      onFatalError: () => {
        setIsPlaying(false);
        notify(t("player.trackLoadFailed", { title: currentTrack.title }), "error");
      },
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
        startAt,
        play: isPlaying,
      })
      .then(({ tier }) => {
        if (adaptiveRef.current === playback) {
          retryRef.current = { trackId: currentTrack.id, tier, attempts: 0 };
        }
      })
      .catch(() => {
        if (adaptiveRef.current === playback) {
          setIsPlaying(false);
          notify(t("player.trackLoadFailed", { title: currentTrack.title }), "error");
        }
      });
  }, [
    currentTrack,
    currentIndex,
    queue,
    dj,
    quality,
    sourceRevision,
    isPlaying,
    settings.hlsEnabled,
    settings.networkIsSlow,
    settings.qualities,
    notify,
    t,
    tracker,
    degrade,
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
  }, [isPlaying, currentTrack, notify, t, tracker]);

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

    const threshold = Math.min(
      settings.historyThresholdSeconds,
      Math.max(track.durationSeconds - 1, 1),
    );
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
      seekInternal(0);

      if (currentTrack) tracker.begin(currentTrack, originRef.current);

      const audio = audioRef.current;
      void audio?.play().catch(() => setIsPlaying(false));
      return;
    }

    advance(1, { auto: true });
  }, [advance, repeat, tracker, currentTrack, seekInternal]);

  const handleError = useCallback(() => {
    const audio = audioRef.current;
    if (!audio || !currentTrack) return;
    if (audio.dataset.sourceLoading === "true") return;
    if (audio.dataset.playbackMode !== "progressive") return;

    if (retryRef.current.trackId !== currentTrack.id) {
      retryRef.current = { trackId: currentTrack.id, tier: tierFor(currentTrack), attempts: 0 };
    }

    const resumeAt = audio.currentTime > 0 ? audio.currentTime : positionRef.current;
    const shouldResume = isPlaying || resumeAt > 0;

    const recovery = decideRecovery({
      errorCode: audio.error?.code,
      tier: retryRef.current.tier,
      fallbackTier,
      fellBack: fellBackRef.current.has(currentTrack.id),
      attempts: retryRef.current.attempts,
      sessionRenewed: retryRef.current.attempts > 0,
    });

    if (recovery.kind === "unsupported") {
      setIsPlaying(false);
      notify(t("player.formatUnsupported", { title: currentTrack.title }), "error");
      return;
    }

    if (recovery.kind === "giveUp") {
      setIsPlaying(false);
      notify(t("player.trackLoadFailed", { title: currentTrack.title }), "error");
      return;
    }

    if (recovery.kind === "fallback") {
      fellBackRef.current.add(currentTrack.id);
      retryRef.current = { trackId: currentTrack.id, tier: recovery.tier, attempts: 0 };

      pendingSeekRef.current = resumeAt;
      degrade();
      setSourceRevision((revision) => revision + 1);

      notify(t("player.preparingPlayable"), "info");
      return;
    }

    const { attempt, tier } = recovery;
    retryRef.current.attempts = attempt + 1;

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
    }, recovery.delayMs);
  }, [currentTrack, isPlaying, tierFor, fallbackTier, notify, t, applyPendingSeek, degrade]);

  const play = useCallback(() => setIsPlaying(true), []);
  const pause = useCallback(() => setIsPlaying(false), []);

  const handleWaiting = useCallback(() => {
    const audio = audioRef.current;
    lastStallAtRef.current = Date.now();
    prefetchRef.current?.controller.abort();
    prefetchRef.current = null;

    if (
      !audio ||
      !currentTrack ||
      quality !== "Original" ||
      audio.dataset.playbackMode !== "progressive" ||
      audio.currentTime <= 0 ||
      degradedUntilRef.current > Date.now()
    ) {
      return;
    }

    const ranges = audio.buffered;
    const bufferedUntil = ranges.length > 0 ? ranges.end(ranges.length - 1) : audio.currentTime;
    if (bufferedUntil - audio.currentTime > 2) return;

    pendingSeekRef.current = audio.currentTime;
    degrade();
    setSourceRevision((revision) => revision + 1);
    notify(t("player.networkDegraded"), "info");
  }, [currentTrack, quality, notify, t, degrade]);

  useEffect(() => {
    if (!currentTrack) {
      pinStreamTracks([]);
      prefetchRef.current?.controller.abort();
      prefetchRef.current = null;
      prefetchRetryAtRef.current = 0;
      return;
    }

    pinStreamTracks([currentTrack.id]);

    const tracks = [currentTrack];
    if (repeat !== "one") {
      let index = currentIndex;
      for (let count = 0; count < 2; count += 1) {
        const step = advanceIn(orderRef.current, index, 1, repeat === "all");
        if (step.kind !== "play") break;
        const upcoming = queueRef.current[step.index];
        if (!upcoming || tracks.some((track) => track.id === upcoming.id)) break;
        tracks.push(upcoming);
        index = step.index;
      }
    }

    const reserveQuality = settings.dataSaver || settings.networkIsSlow ? "Low" : "Normal";
    const key = `${reserveQuality}:${tracks.map((track) => track.id).join(":")}`;

    if (prefetchRef.current?.key !== key) {
      prefetchRef.current?.controller.abort();
      prefetchRef.current = null;
      prefetchRetryAtRef.current = 0;
    }

    if (
      !settings.hlsEnabled ||
      prefetchRef.current ||
      Date.now() < prefetchRetryAtRef.current ||
      !readyToPrefetch({
        online,
        playing: isPlaying,
        position,
        bufferedUntil: buffered,
        duration,
        lastStallAt: lastStallAtRef.current,
        now: Date.now(),
      })
    ) {
      return;
    }

    const controller = new AbortController();
    prefetchRef.current = { key, controller };

    void prefetchHlsTracks(
      tracks.map((track) => track.id),
      reserveQuality,
      controller.signal,
    )
      .then((complete) => {
        if (!complete && prefetchRef.current?.controller === controller) {
          prefetchRef.current = null;
          prefetchRetryAtRef.current = Date.now() + 10_000;
        }
      })
      .catch(() => {
        if (prefetchRef.current?.controller === controller) {
          prefetchRef.current = null;
          prefetchRetryAtRef.current = Date.now() + 10_000;
        }
      });
  }, [
    currentTrack,
    currentIndex,
    queue,
    repeat,
    online,
    isPlaying,
    position,
    buffered,
    duration,
    settings.hlsEnabled,
    settings.dataSaver,
    settings.networkIsSlow,
  ]);

  const getPosition = useCallback(() => audioRef.current?.currentTime ?? positionRef.current, []);

  useMediaSession(currentTrack, isPlaying, duration, {
    play,
    pause,
    next,
    previous,
    seek,
    seekBy,
    getPosition,
  });

  useExclusivePlayback(
    isPlaying,
    useCallback(() => {
      setIsPlaying(false);
      notify(t("player.playingElsewhere"), "info");
    }, [notify, t]),
  );

  const state = useMemo<PlayerState>(
    () => ({
      queue,
      currentTrack,
      currentIndex,
      isPlaying,
      volume,
      muted,
      shuffle,
      repeat,
      radio,
      dj,
      djLoading,
    }),
    [
      queue,
      currentTrack,
      currentIndex,
      isPlaying,
      volume,
      muted,
      shuffle,
      repeat,
      radio,
      dj,
      djLoading,
    ],
  );

  const actions = useMemo<PlayerActions>(
    () => ({
      playQueue,
      playTrack,
      toggle,
      pause,
      next,
      previous,
      seek,
      seekBy,
      setVolume,
      toggleMute,
      toggleShuffle,
      cycleRepeat,
      addToQueue,
      playNext,
      removeFromQueue,
      moveInQueue,
      clearQueue,
      jumpTo,
      patchTrack,
      snapshotQueue,
      restoreQueue,
      startDj,
      setDjVariety,
    }),
    [
      playQueue,
      playTrack,
      toggle,
      pause,
      next,
      previous,
      seek,
      seekBy,
      setVolume,
      toggleMute,
      toggleShuffle,
      cycleRepeat,
      addToQueue,
      playNext,
      removeFromQueue,
      moveInQueue,
      clearQueue,
      jumpTo,
      patchTrack,
      snapshotQueue,
      restoreQueue,
      startDj,
      setDjVariety,
    ],
  );

  const progress = useMemo<PlayerProgress>(
    () => ({ position, duration, buffered, getPosition }),
    [position, duration, buffered, getPosition],
  );

  return (
    <PlayerStateContext.Provider value={state}>
      <PlayerActionsContext.Provider value={actions}>
        <PlayerProgressContext.Provider value={progress}>{children}</PlayerProgressContext.Provider>
        <audio
          ref={audioRef}
          preload="metadata"
          onTimeUpdate={handleTimeUpdate}
          onProgress={handleProgress}
          onLoadedMetadata={(event) => setDuration(event.currentTarget.duration || 0)}
          onDurationChange={(event) => setDuration(event.currentTarget.duration || 0)}
          onEnded={handleEnded}
          onError={handleError}
          onWaiting={handleWaiting}
          onStalled={handleWaiting}
          onPlay={() => setIsPlaying(true)}
          onPause={(event) => {
            if (event.currentTarget.dataset.sourceLoading !== "true") setIsPlaying(false);
          }}
          onPlaying={() => {
            retryRef.current.attempts = 0;
          }}
        />
      </PlayerActionsContext.Provider>
    </PlayerStateContext.Provider>
  );
}

export function usePlayerState(): PlayerState {
  return useRequiredContext(PlayerStateContext, "usePlayerState", "PlayerProvider");
}

export function usePlayerActions(): PlayerActions {
  return useRequiredContext(PlayerActionsContext, "usePlayerActions", "PlayerProvider");
}

export function usePlayer(): PlayerState & PlayerActions {
  const state = usePlayerState();
  const actions = usePlayerActions();
  return useMemo(() => ({ ...state, ...actions }), [state, actions]);
}

export function usePlayerProgress(): PlayerProgress {
  return useRequiredContext(PlayerProgressContext, "usePlayerProgress", "PlayerProvider");
}
