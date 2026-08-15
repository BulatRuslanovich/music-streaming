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
import { recordEvent, type PlaybackSource } from "@/lib/events";
import { mediaUrl } from "@/lib/media";
import type { Track } from "@/lib/types";
import { useMediaSession } from "@/lib/useMediaSession";
import { readPersistedPlayer, usePersistedPlayer } from "@/lib/usePlayerStorage";
import { useSettings } from "./SettingsContext";
import { useT } from "./I18nContext";
import { useToast } from "./ToastContext";

export type RepeatMode = "off" | "all" | "one";

/** Что сейчас делает радио — очередь показывает это вместо молчаливого конца списка. */
export type RadioState = "idle" | "loading" | "empty" | "failed";

export interface PlaybackOrigin {
  source?: PlaybackSource;
  sourceId?: string;
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
  next: () => void;
  previous: () => void;
  seek: (seconds: number) => void;
  seekBy: (deltaSeconds: number) => void;
  setVolume: (volume: number) => void;
  toggleMute: () => void;
  toggleShuffle: () => void;
  cycleRepeat: () => void;
  addToQueue: (track: Track) => void;
  removeFromQueue: (index: number) => void;
  clearQueue: () => void;
  jumpTo: (index: number) => void;
  patchTrack: (trackId: string, changes: Partial<Track>) => void;
}

interface PlayerProgress {
  position: number;
  duration: number;
  buffered: number;
}

const PlayerContext = createContext<PlayerState | null>(null);

const PlayerProgressContext = createContext<PlayerProgress | null>(null);

const STREAM_RETRY_DELAYS_MS = [800, 2500, 6000];

const MAX_LISTENING_STEP_SECONDS = 2;

const HEARTBEAT_INTERVAL_SECONDS = 30;

/** Сколько треков должно остаться впереди, чтобы радио начало готовить продолжение. */
const RADIO_PREFETCH_AT = 1;

export function PlayerProvider({ children }: { children: React.ReactNode }) {
  const { notify } = useToast();
  const t = useT();
  const settings = useSettings();
  const audioRef = useRef<HTMLAudioElement | null>(null);

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

  const listenedRef = useRef({ trackId: "", seconds: 0, position: 0, duration: 0 });
  const originRef = useRef<PlaybackOrigin>({});
  const heardRef = useRef(new Set<string>());
  const lastHeartbeatRef = useRef(0);
  const pendingSeekRef = useRef<number | null>(null);
  const positionRef = useRef(0);
  const retryRef = useRef<{ trackId: string; attempts: number }>({ trackId: "", attempts: 0 });
  const retryTimerRef = useRef<number | null>(null);

  // Запрос радио идёт по одному за раз, а зерно запоминается, чтобы одна и та же затравка не
  // приводила ко второй такой же пачке.
  const radioRef = useRef<{ inFlight: boolean; seed: string | null }>({
    inFlight: false,
    seed: null,
  });

  // С какого места очередь состоит из подобранного радио — с него прослушивания помечаются как
  // запущенные из радио, и движок рекомендаций отличает их от осознанного выбора.
  const radioFromRef = useRef(Number.MAX_SAFE_INTEGER);
  const currentTrack = currentIndex >= 0 ? (queue[currentIndex] ?? null) : null;

  useEffect(() => {
    /* eslint-disable react-hooks/set-state-in-effect */
    const saved = readPersistedPlayer();
    if (saved) {
      if (Array.isArray(saved.queue) && saved.queue.length > 0) {
        setQueue(saved.queue);
        orderRef.current = saved.queue.map((_, index) => index);

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
  }, []);

  usePersistedPlayer(
    { queue, index: currentIndex, position, volume, muted, shuffle, repeat },
    restored,
    isPlaying,
  );

  const buildOrder = useCallback((length: number, shuffled: boolean, startIndex: number) => {
    const indices = Array.from({ length }, (_, index) => index);
    if (!shuffled) return indices;

    for (let i = indices.length - 1; i > 0; i -= 1) {
      const j = Math.floor(Math.random() * (i + 1));
      [indices[i], indices[j]] = [indices[j], indices[i]];
    }

    if (startIndex >= 0) {
      const at = indices.indexOf(startIndex);
      if (at > 0) [indices[0], indices[at]] = [indices[at], indices[0]];
    }

    return indices;
  }, []);

  const finishPlay = useCallback((type: "trackCompleted" | "trackSkipped") => {
    const played = listenedRef.current;
    if (!played.trackId) return;

    recordEvent({
      type,
      trackId: played.trackId,
      positionSeconds: Math.floor(played.position),
      listenedSeconds: Math.floor(played.seconds),
      durationSeconds: played.duration,
      ...originRef.current,
    });

    listenedRef.current = { trackId: "", seconds: 0, position: 0, duration: 0 };
  }, []);

  const accumulateListening = useCallback((currentTime: number) => {
    const played = listenedRef.current;
    if (!played.trackId) return;

    const delta = currentTime - played.position;
    if (delta > 0 && delta < MAX_LISTENING_STEP_SECONDS) {
      played.seconds += delta;
    }

    played.position = currentTime;

    if (played.seconds - lastHeartbeatRef.current >= HEARTBEAT_INTERVAL_SECONDS) {
      lastHeartbeatRef.current = played.seconds;

      recordEvent({
        type: "trackPlayed",
        trackId: played.trackId,
        positionSeconds: Math.floor(played.position),
        listenedSeconds: Math.floor(played.seconds),
        durationSeconds: played.duration,
        ...originRef.current,
      });
    }
  }, []);

  const beginPlay = useCallback((track: Track) => {
    listenedRef.current = {
      trackId: track.id,
      seconds: 0,
      position: 0,
      duration: track.durationSeconds,
    };
    lastHeartbeatRef.current = 0;

    recordEvent({
      type: "trackStarted",
      trackId: track.id,
      durationSeconds: track.durationSeconds,
      ...originRef.current,
    });

    if (heardRef.current.has(track.id)) {
      recordEvent({
        type: "trackReplayed",
        trackId: track.id,
        durationSeconds: track.durationSeconds,
        ...originRef.current,
      });
    }

    heardRef.current.add(track.id);
  }, []);

  const playQueue = useCallback(
    (tracks: Track[], startIndex = 0, origin: PlaybackOrigin = {}) => {
      if (tracks.length === 0) return;

      const safeIndex = Math.min(Math.max(startIndex, 0), tracks.length - 1);

      finishPlay("trackSkipped");
      originRef.current = origin;

      radioRef.current = { inFlight: false, seed: null };
      radioFromRef.current = Number.MAX_SAFE_INTEGER;
      setRadio("idle");

      setQueue(tracks);
      orderRef.current = buildOrder(tracks.length, shuffle, safeIndex);
      setCurrentIndex(safeIndex);
      setPosition(0);
      setIsPlaying(true);
      pendingSeekRef.current = null;
    },
    [buildOrder, shuffle, finishPlay],
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

  const advance = useCallback(
    (direction: 1 | -1, { auto = false }: { auto?: boolean } = {}) => {
      const order = orderRef.current;
      if (order.length === 0 || currentIndex < 0) return;

      const positionInOrder = order.indexOf(currentIndex);
      if (positionInOrder === -1) return;

      const nextPositionInOrder = positionInOrder + direction;

      if (nextPositionInOrder < 0) {
        seekInternal(0);
        return;
      }

      if (nextPositionInOrder >= order.length) {
        if (repeat === "all") {
          setCurrentIndex(order[0]);
          setPosition(0);
          setIsPlaying(true);
          return;
        }

        setIsPlaying(false);
        if (auto) seekInternal(0);
        return;
      }

      setCurrentIndex(order[nextPositionInOrder]);
      setPosition(0);
      setIsPlaying(true);
    },
    [currentIndex, repeat],
  );

  function seekInternal(seconds: number) {
    const audio = audioRef.current;
    if (!audio) return;

    audio.currentTime = seconds;
    setPosition(seconds);
    positionRef.current = seconds;
  }

  const next = useCallback(() => advance(1), [advance]);
  const previous = useCallback(() => {
    if (audioRef.current && audioRef.current.currentTime > 3) {
      seekInternal(0);
      return;
    }
    advance(-1);
  }, [advance]);

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
    setShuffle((wasShuffled) => {
      const nowShuffled = !wasShuffled;
      orderRef.current = buildOrder(queue.length, nowShuffled, currentIndex);
      return nowShuffled;
    });
  }, [buildOrder, queue.length, currentIndex]);

  const cycleRepeat = useCallback(() => {
    setRepeat((mode) => (mode === "off" ? "all" : mode === "all" ? "one" : "off"));
  }, []);

  const addToQueue = useCallback((track: Track) => {
    recordEvent({ type: "trackAddedToQueue", trackId: track.id });

    setQueue((current) => {
      const appended = [...current, track];
      orderRef.current = [...orderRef.current, appended.length - 1];
      return appended;
    });

    setCurrentIndex((index) => (index < 0 ? 0 : index));
  }, []);

  const removeFromQueue = useCallback(
    (index: number) => {
      setQueue((current) => {
        if (index < 0 || index >= current.length) return current;

        const remaining = current.filter((_, position) => position !== index);
        orderRef.current = buildOrder(remaining.length, shuffle, -1);

        setCurrentIndex((activeIndex) => {
          if (remaining.length === 0) return -1;
          if (index < activeIndex) return activeIndex - 1;
          if (index === activeIndex) return Math.min(activeIndex, remaining.length - 1);
          return activeIndex;
        });

        return remaining;
      });
    },
    [buildOrder, shuffle],
  );

  const clearQueue = useCallback(() => {
    radioRef.current = { inFlight: false, seed: null };
    radioFromRef.current = Number.MAX_SAFE_INTEGER;
    setRadio("idle");

    setQueue([]);
    orderRef.current = [];
    setCurrentIndex(-1);
    setIsPlaying(false);
    setPosition(0);
    setDuration(0);
  }, []);

  const jumpTo = useCallback(
    (index: number) => {
      if (index < 0 || index >= queue.length) return;

      setCurrentIndex(index);
      setPosition(0);
      setIsPlaying(true);
    },
    [queue.length],
  );

  const patchTrack = useCallback((trackId: string, changes: Partial<Track>) => {
    setQueue((current) =>
      current.map((track) => (track.id === trackId ? { ...track, ...changes } : track)),
    );
  }, []);

  /**
   * Продолжение очереди похожими треками.
   *
   * Пачка запрашивается заранее — когда впереди остаётся не больше одного трека, — чтобы музыка
   * не прерывалась на время запроса. Один запрос за раз: повторное срабатывание (второй рендер,
   * возврат на вкладку, быстрый скип) не должно приводить ко второй такой же пачке в очереди.
   */
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

        setQueue((current) => {
          // Очередь могла смениться, пока шёл запрос: то, что уже в ней есть, добавлять нельзя.
          const known = new Set(current.map((track) => track.id));
          const fresh = tracks.filter((track) => !known.has(track.id));

          if (fresh.length === 0) return current;

          radioFromRef.current = Math.min(radioFromRef.current, current.length);
          orderRef.current = [
            ...orderRef.current,
            ...fresh.map((_, offset) => current.length + offset),
          ];

          return [...current, ...fresh];
        });

        setRadio("idle");
      })
      .catch(() => setRadio("failed"))
      .finally(() => {
        radioRef.current = { ...radioRef.current, inFlight: false };
      });
  }, [settings.autoplay, currentIndex, queue, repeat]);

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

  useEffect(() => {
    const audio = audioRef.current;
    if (!audio || !currentTrack) return;

    const sourceKey = `${currentTrack.id}:${quality}`;
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

      // Трек, доставшийся из радио, и запускается как радио — иначе движок посчитал бы его
      // осознанным выбором пользователя.
      if (currentIndex >= radioFromRef.current) {
        originRef.current = { source: "radio", sourceId: queue[radioFromRef.current - 1]?.id };
      }

      finishPlay("trackSkipped");
      beginPlay(currentTrack);
    }

    audio.dataset.trackId = currentTrack.id;
    audio.dataset.sourceKey = sourceKey;
    audio.src = mediaUrl.stream(currentTrack.id, quality);
    retryRef.current = { trackId: currentTrack.id, attempts: 0 };
    positionRef.current = pendingSeekRef.current ?? 0;
    setDuration(currentTrack.durationSeconds || 0);

    applyPendingSeek(audio);

    if (staysOnSameTrack && isPlaying) {
      void audio.play().catch(() => {});
    }
  }, [
    currentTrack,
    currentIndex,
    queue,
    quality,
    isPlaying,
    applyPendingSeek,
    finishPlay,
    beginPlay,
  ]);

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

      const played = listenedRef.current;
      if (played.trackId) {
        recordEvent({
          type: "trackPaused",
          trackId: played.trackId,
          positionSeconds: Math.floor(played.position),
          listenedSeconds: Math.floor(played.seconds),
          durationSeconds: played.duration,
          ...originRef.current,
        });
      }
    }
  }, [isPlaying, currentTrack, notify, t]);

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

    accumulateListening(audio.currentTime);

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
      void api.recordPlay(track.id, Math.floor(audio.currentTime)).catch(() => {});
    }
  }, [currentTrack, accumulateListening, settings.historyThresholdSeconds]);

  const handleProgress = useCallback(() => {
    const audio = audioRef.current;
    if (!audio) return;

    const ranges = audio.buffered;
    setBuffered(ranges.length > 0 ? ranges.end(ranges.length - 1) : 0);
  }, []);

  const handleEnded = useCallback(() => {
    finishPlay("trackCompleted");

    if (repeat === "one") {
      seekInternal(0);

      if (currentTrack) beginPlay(currentTrack);

      const audio = audioRef.current;
      void audio?.play().catch(() => setIsPlaying(false));
      return;
    }

    advance(1, { auto: true });
  }, [advance, repeat, finishPlay, beginPlay, currentTrack]);

  const handleError = useCallback(() => {
    const audio = audioRef.current;
    if (!audio || !currentTrack) return;

    if (retryRef.current.trackId !== currentTrack.id) {
      retryRef.current = { trackId: currentTrack.id, attempts: 0 };
    }

    const attempt = retryRef.current.attempts;
    if (attempt >= STREAM_RETRY_DELAYS_MS.length) {
      setIsPlaying(false);
      notify(t("player.trackLoadFailed", { title: currentTrack.title }), "error");
      return;
    }

    retryRef.current.attempts = attempt + 1;

    const resumeAt = audio.currentTime > 0 ? audio.currentTime : positionRef.current;
    const shouldResume = isPlaying || resumeAt > 0;

    retryTimerRef.current = window.setTimeout(() => {
      retryTimerRef.current = null;

      const element = audioRef.current;
      if (!element || element.dataset.trackId !== currentTrack.id) return;

      pendingSeekRef.current = resumeAt;
      element.src = mediaUrl.stream(currentTrack.id, quality);
      applyPendingSeek(element);
      element.load();

      if (shouldResume) void element.play().catch(() => {});
    }, STREAM_RETRY_DELAYS_MS[attempt]);
  }, [currentTrack, isPlaying, quality, notify, t, applyPendingSeek]);

  const play = useCallback(() => setIsPlaying(true), []);
  const pause = useCallback(() => setIsPlaying(false), []);

  useMediaSession(currentTrack, isPlaying, { play, pause, next, previous });

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
      next,
      previous,
      seek,
      seekBy,
      setVolume,
      toggleMute,
      toggleShuffle,
      cycleRepeat,
      addToQueue,
      removeFromQueue,
      clearQueue,
      jumpTo,
      patchTrack,
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
      next,
      previous,
      seek,
      seekBy,
      setVolume,
      toggleMute,
      toggleShuffle,
      cycleRepeat,
      addToQueue,
      removeFromQueue,
      clearQueue,
      jumpTo,
      patchTrack,
    ],
  );

  const progress = useMemo<PlayerProgress>(
    () => ({ position, duration, buffered }),
    [position, duration, buffered],
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
