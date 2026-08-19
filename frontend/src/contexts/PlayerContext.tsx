// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import React, {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
} from "react";
import { api } from "@/lib/api";
import { bestFallbackTier, playableTier } from "@/lib/audioFormats";
import { recordEvent } from "@/lib/events";
import { refreshSession } from "@/lib/http";
import { mediaUrl } from "@/lib/media";
import {
  createListeningTracker,
  type ListeningTracker,
  type PlaybackOrigin,
} from "@/lib/playbackTelemetry";
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
import { decideRecovery } from "@/lib/streamRecovery";
import type { AudioQuality, Track } from "@/lib/types";
import { useExclusivePlayback } from "@/lib/useExclusivePlayback";
import { useInvalidate } from "@/lib/useInvalidate";
import { useMediaSession } from "@/lib/useMediaSession";
import { readPersistedPlayer, usePersistedPlayer } from "@/lib/usePlayerStorage";
import { useOffline } from "./OfflineContext";
import { useSettings } from "./SettingsContext";
import { useT } from "./I18nContext";
import { useToast } from "./ToastContext";

export type RepeatMode = "off" | "all" | "one";

export type RadioState = "idle" | "loading" | "empty" | "failed";

export type { PlaybackOrigin };

export interface QueueSnapshot {
  queue: Track[];
  order: number[];
  index: number;
  position: number;
  radioFrom: number;
}

interface PlayerState {
  queue: Track[];
  currentTrack: Track | null;
  currentIndex: number;
  isPlaying: boolean;
  volume: number;
  muted: boolean;
  shuffle: boolean;
  repeat: RepeatMode;
  radio: RadioState;

  playQueue: (tracks: Track[], startIndex?: number, origin?: PlaybackOrigin) => void;
  playTrack: (track: Track, contextTracks?: Track[], origin?: PlaybackOrigin) => void;
  toggle: () => void;
  pause: () => void;
  next: () => void;
  previous: () => void;
  seek: (seconds: number) => void;
  seekBy: (deltaSeconds: number) => void;
  setVolume: (volume: number) => void;
  toggleMute: () => void;
  toggleShuffle: () => void;
  cycleRepeat: () => void;
  addToQueue: (track: Track) => void;
  playNext: (track: Track) => void;
  removeFromQueue: (index: number) => void;
  moveInQueue: (from: number, to: number) => void;
  clearQueue: () => void;
  jumpTo: (index: number) => void;
  patchTrack: (trackId: string, changes: Partial<Track>) => void;
  snapshotQueue: () => QueueSnapshot;
  restoreQueue: (snapshot: QueueSnapshot) => void;
}

interface PlayerProgress {
  position: number;
  duration: number;
  buffered: number;

  // Точное время воспроизведения на момент вызова. `position` живёт на событии timeupdate, а его
  // браузер шлёт не чаще четырёх раз в секунду, и на коротких строках текста эта четверть секунды
  // уже заметна глазом — там, где важна точность, время надо брать отсюда.
  getPosition: () => number;
}

const PlayerContext = createContext<PlayerState | null>(null);

const PlayerProgressContext = createContext<PlayerProgress | null>(null);

const RADIO_PREFETCH_AT = 1;

const PRELOAD_BEFORE_END = 20;

export function PlayerProvider({ children }: { children: React.ReactNode }) {
  const { notify } = useToast();
  const t = useT();
  const settings = useSettings();
  const { isOffline } = useOffline();
  const invalidate = useInvalidate();
  const audioRef = useRef<HTMLAudioElement | null>(null);
  const preloadRef = useRef<HTMLAudioElement | null>(null);

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
  const [restored, setRestored] = useState(false);

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

  const radioRef = useRef<{ inFlight: boolean; seed: string | null }>({
    inFlight: false,
    seed: null,
  });

  const radioFromRef = useRef(Number.MAX_SAFE_INTEGER);
  const currentTrack = currentIndex >= 0 ? (queue[currentIndex] ?? null) : null;

  useEffect(() => {
    /* eslint-disable react-hooks/set-state-in-effect */
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
    }

    setRestored(true);
    /* eslint-enable react-hooks/set-state-in-effect */
  }, [applyQueue]);

  usePersistedPlayer(
    { queue, index: currentIndex, position, volume, muted, shuffle, repeat },
    restored,
    isPlaying,
  );

  const playQueue = useCallback(
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
    }),
    [currentIndex],
  );

  const restoreQueue = useCallback(
    (snapshot: QueueSnapshot) => {
      const audio = audioRef.current;
      const trackId = snapshot.queue[snapshot.index]?.id;

      if (audio && trackId && audio.dataset.trackId !== trackId) {
        pendingSeekRef.current = snapshot.position;
      }

      radioFromRef.current = snapshot.radioFrom;

      applyQueue(snapshot.queue, snapshot.order);
      setCurrentIndex(snapshot.index);
      setPosition(snapshot.position);
      positionRef.current = snapshot.position;
    },
    [applyQueue],
  );

  const clearQueue = useCallback(() => {
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
    if (!settings.autoplay || currentIndex < 0 || repeat !== "off") return;

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
  }, [settings.autoplay, currentIndex, queue, repeat, applyQueue]);

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
  }, [quality]);

  useEffect(() => {
    const audio = audioRef.current;
    if (!audio || !currentTrack) return;

    const tier = tierFor(currentTrack);
    const sourceKey = `${currentTrack.id}:${tier}`;
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

      if (currentIndex >= radioFromRef.current) {
        originRef.current = { source: "radio", sourceId: queue[radioFromRef.current - 1]?.id };
      }

      tracker.finish("trackSkipped", originRef.current);
      tracker.begin(currentTrack, originRef.current);
    }

    audio.dataset.trackId = currentTrack.id;
    audio.dataset.sourceKey = sourceKey;
    audio.src = mediaUrl.stream(currentTrack.id, tier);
    retryRef.current = { trackId: currentTrack.id, tier, attempts: 0 };
    positionRef.current = pendingSeekRef.current ?? 0;
    setDuration(currentTrack.durationSeconds || 0);

    applyPendingSeek(audio);

    if (staysOnSameTrack && isPlaying) {
      void audio.play().catch(() => {});
    }
  }, [currentTrack, currentIndex, queue, tierFor, isPlaying, applyPendingSeek, tracker]);

  useEffect(
    () => () => {
      if (retryTimerRef.current !== null) window.clearTimeout(retryTimerRef.current);
    },
    [],
  );

  useEffect(() => {
    const audio = audioRef.current;
    if (!audio) return;

    if (isPlaying) {
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
      audio.dataset.sourceKey = `${currentTrack.id}:${recovery.tier}`;
      audio.src = mediaUrl.stream(currentTrack.id, recovery.tier);
      applyPendingSeek(audio);
      audio.load();

      notify(t("player.preparingPlayable"), "info");

      if (shouldResume) void audio.play().catch(() => {});
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
  }, [currentTrack, isPlaying, tierFor, fallbackTier, notify, t, applyPendingSeek]);

  const play = useCallback(() => setIsPlaying(true), []);
  const pause = useCallback(() => setIsPlaying(false), []);

  useEffect(() => {
    const audio = preloadRef.current;
    if (!audio || !isPlaying || isOffline || settings.dataSaver || settings.networkIsSlow) return;
    if (duration <= 0 || position < duration - PRELOAD_BEFORE_END) return;

    const step = advanceIn(orderRef.current, currentIndex, 1, repeat === "all");
    if (step.kind !== "play") return;

    const upcoming = queueRef.current[step.index];
    if (!upcoming) return;

    const tier = tierFor(upcoming);
    const sourceKey = `${upcoming.id}:${tier}`;
    if (audio.dataset.sourceKey === sourceKey) return;

    audio.dataset.sourceKey = sourceKey;
    audio.src = mediaUrl.stream(upcoming.id, tier);
    audio.load();
  }, [
    position,
    duration,
    isPlaying,
    isOffline,
    currentIndex,
    repeat,
    tierFor,
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

  const value = useMemo<PlayerState>(
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
    ],
  );

  const progress = useMemo<PlayerProgress>(
    () => ({ position, duration, buffered, getPosition }),
    [position, duration, buffered, getPosition],
  );

  return (
    <PlayerContext.Provider value={value}>
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
        onPlay={() => setIsPlaying(true)}
        onPause={() => setIsPlaying(false)}
        onPlaying={() => {
          retryRef.current.attempts = 0;
        }}
      />
      <audio ref={preloadRef} preload="auto" muted aria-hidden="true" />
    </PlayerContext.Provider>
  );
}

export function usePlayer(): PlayerState {
  const context = useContext(PlayerContext);
  if (!context) throw new Error("usePlayer must be used inside <PlayerProvider>");
  return context;
}

export function usePlayerProgress(): PlayerProgress {
  const context = useContext(PlayerProgressContext);
  if (!context) throw new Error("usePlayerProgress must be used inside <PlayerProvider>");
  return context;
}
