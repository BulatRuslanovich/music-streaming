"use client";

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
} from "react";
import { api, mediaUrl } from "@/lib/api";
import { formatArtists } from "@/lib/format";
import type { Track } from "@/lib/types";

export type RepeatMode = "off" | "all" | "one";

interface PlayerState {
  queue: Track[];
  currentTrack: Track | null;
  currentIndex: number;
  isPlaying: boolean;
  isLoading: boolean;
  position: number;
  duration: number;
  buffered: number;
  volume: number;
  muted: boolean;
  shuffle: boolean;
  repeat: RepeatMode;
  error: string | null;

  playQueue: (tracks: Track[], startIndex?: number) => void;
  playTrack: (track: Track, contextTracks?: Track[]) => void;
  toggle: () => void;
  next: () => void;
  previous: () => void;
  seek: (seconds: number) => void;
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

const PlayerContext = createContext<PlayerState | null>(null);

const STORAGE_KEY = "music-streaming.player";
const DEFAULT_HISTORY_THRESHOLD = 30;

interface PersistedState {
  queue: Track[];
  index: number;
  position: number;
  volume: number;
  muted: boolean;
  shuffle: boolean;
  repeat: RepeatMode;
}

export function PlayerProvider({ children }: { children: React.ReactNode }) {
  const audioRef = useRef<HTMLAudioElement | null>(null);

  const [queue, setQueue] = useState<Track[]>([]);
  const [currentIndex, setCurrentIndex] = useState(-1);
  const [isPlaying, setIsPlaying] = useState(false);
  const [isLoading, setIsLoading] = useState(false);
  const [position, setPosition] = useState(0);
  const [duration, setDuration] = useState(0);
  const [buffered, setBuffered] = useState(0);
  const [volume, setVolumeState] = useState(1);
  const [muted, setMuted] = useState(false);
  const [shuffle, setShuffle] = useState(false);
  const [repeat, setRepeat] = useState<RepeatMode>("off");
  const [error, setError] = useState<string | null>(null);
  const [restored, setRestored] = useState(false);

  const orderRef = useRef<number[]>([]);
  const historyThresholdRef = useRef(DEFAULT_HISTORY_THRESHOLD);
  const recordedRef = useRef<string | null>(null);
  const pendingSeekRef = useRef<number | null>(null);
  const currentTrack = currentIndex >= 0 ? (queue[currentIndex] ?? null) : null;

  useEffect(() => {
    try {
      const raw = window.localStorage.getItem(STORAGE_KEY);
      if (raw) {
        const saved = JSON.parse(raw) as Partial<PersistedState>;

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
    } catch {
      window.localStorage.removeItem(STORAGE_KEY);
    }

    setRestored(true);
  }, []);

  useEffect(() => {
    api
      .config()
      .then((config) => {
        if (config.historyThresholdSeconds > 0) {
          historyThresholdRef.current = config.historyThresholdSeconds;
        }
      })
      .catch(() => {});
  }, []);

  useEffect(() => {
    if (!restored) return;

    const snapshot: PersistedState = {
      queue,
      index: currentIndex,
      position,
      volume,
      muted,
      shuffle,
      repeat,
    };

    try {
      window.localStorage.setItem(STORAGE_KEY, JSON.stringify(snapshot));
    } catch {}
  }, [restored, queue, currentIndex, position, volume, muted, shuffle, repeat]);


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

  const playQueue = useCallback(
    (tracks: Track[], startIndex = 0) => {
      if (tracks.length === 0) return;

      const safeIndex = Math.min(Math.max(startIndex, 0), tracks.length - 1);

      setQueue(tracks);
      orderRef.current = buildOrder(tracks.length, shuffle, safeIndex);
      setCurrentIndex(safeIndex);
      setPosition(0);
      setError(null);
      setIsPlaying(true);
      pendingSeekRef.current = null;
    },
    [buildOrder, shuffle],
  );

  const playTrack = useCallback(
    (track: Track, contextTracks?: Track[]) => {
      if (contextTracks && contextTracks.length > 0) {
        const index = contextTracks.findIndex((candidate) => candidate.id === track.id);
        playQueue(contextTracks, index >= 0 ? index : 0);
        return;
      }

      playQueue([track], 0);
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
      setError(null);
      setIsPlaying(true);
    },
    [currentIndex, repeat],
  );

  function seekInternal(seconds: number) {
    const audio = audioRef.current;
    if (!audio) return;

    audio.currentTime = seconds;
    setPosition(seconds);
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
  }, []);

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

  const addToQueue = useCallback(
    (track: Track) => {
      setQueue((current) => {
        const appended = [...current, track];
        orderRef.current = [...orderRef.current, appended.length - 1];
        return appended;
      });

      setCurrentIndex((index) => (index < 0 ? 0 : index));
    },
    [],
  );

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
      setError(null);
      setIsPlaying(true);
    },
    [queue.length],
  );

  const patchTrack = useCallback((trackId: string, changes: Partial<Track>) => {
    setQueue((current) =>
      current.map((track) => (track.id === trackId ? { ...track, ...changes } : track)),
    );
  }, []);

  useEffect(() => {
    const audio = audioRef.current;
    if (!audio || !currentTrack) return;

    const nextSource = mediaUrl.stream(currentTrack.id);
    if (audio.dataset.trackId === currentTrack.id) return;

    audio.dataset.trackId = currentTrack.id;
    audio.src = nextSource;
    recordedRef.current = null;
    setDuration(currentTrack.durationSeconds || 0);

    if (pendingSeekRef.current !== null) {
      const resumeAt = pendingSeekRef.current;
      pendingSeekRef.current = null;

      const applyResume = () => {
        audio.currentTime = resumeAt;
        audio.removeEventListener("loadedmetadata", applyResume);
      };
      audio.addEventListener("loadedmetadata", applyResume);
    }
  }, [currentTrack]);

  useEffect(() => {
    const audio = audioRef.current;
    if (!audio) return;

    if (isPlaying) {
      audio
        .play()
        .then(() => setError(null))
        .catch((reason: unknown) => {
          const name = reason instanceof DOMException ? reason.name : "";
          if (name !== "AbortError") {
            setIsPlaying(false);
            setError(
              name === "NotAllowedError"
                ? "Press play to start audio — the browser blocked automatic playback."
                : "This track could not be played.",
            );
          }
        })
        .finally(() => setIsLoading(false));
    } else {
      audio.pause();
    }
  }, [isPlaying, currentTrack]);

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

    setPosition(audio.currentTime);

    const track = currentTrack;
    if (!track || recordedRef.current === track.id) return;

    const threshold = Math.min(historyThresholdRef.current, Math.max(track.durationSeconds - 1, 1));
    if (audio.currentTime >= threshold) {
      recordedRef.current = track.id;
      void api.recordPlay(track.id, Math.floor(audio.currentTime)).catch(() => {});
    }
  }, [currentTrack]);

  const handleProgress = useCallback(() => {
    const audio = audioRef.current;
    if (!audio) return;

    const ranges = audio.buffered;
    setBuffered(ranges.length > 0 ? ranges.end(ranges.length - 1) : 0);
  }, []);

  const handleEnded = useCallback(() => {
    if (repeat === "one") {
      seekInternal(0);
      const audio = audioRef.current;
      void audio?.play().catch(() => setIsPlaying(false));
      return;
    }

    advance(1, { auto: true });
  }, [advance, repeat]);

  const handleError = useCallback(() => {
    if (!currentTrack) return;
    setIsPlaying(false);
    setError(`"${currentTrack.title}" could not be loaded.`);
  }, [currentTrack]);

  useEffect(() => {
    if (!("mediaSession" in navigator) || !currentTrack) return;

    navigator.mediaSession.metadata = new MediaMetadata({
      title: currentTrack.title,
      artist: formatArtists(currentTrack),
      album: currentTrack.albumTitle ?? undefined,
      artwork: currentTrack.hasCover
        ? [{ src: mediaUrl.trackCover(currentTrack.id), sizes: "512x512", type: "image/jpeg" }]
        : undefined,
    });

    navigator.mediaSession.playbackState = isPlaying ? "playing" : "paused";

    const handlers: [MediaSessionAction, () => void][] = [
      ["play", () => setIsPlaying(true)],
      ["pause", () => setIsPlaying(false)],
      ["previoustrack", previous],
      ["nexttrack", next],
    ];

    for (const [action, handler] of handlers) {
      try {
        navigator.mediaSession.setActionHandler(action, handler);
      } catch {}
    }

    return () => {
      for (const [action] of handlers) {
        try {
          navigator.mediaSession.setActionHandler(action, null);
        } catch {}
      }
    };
  }, [currentTrack, isPlaying, next, previous]);

  const value = useMemo<PlayerState>(
    () => ({
      queue,
      currentTrack,
      currentIndex,
      isPlaying,
      isLoading,
      position,
      duration,
      buffered,
      volume,
      muted,
      shuffle,
      repeat,
      error,
      playQueue,
      playTrack,
      toggle,
      next,
      previous,
      seek,
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
      isLoading,
      position,
      duration,
      buffered,
      volume,
      muted,
      shuffle,
      repeat,
      error,
      playQueue,
      playTrack,
      toggle,
      next,
      previous,
      seek,
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

  return (
    <PlayerContext.Provider value={value}>
      {children}
      {}
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
        onWaiting={() => setIsLoading(true)}
        onPlaying={() => setIsLoading(false)}
        onCanPlay={() => setIsLoading(false)}
      />
    </PlayerContext.Provider>
  );
}

export function usePlayer(): PlayerState {
  const context = useContext(PlayerContext);
  if (!context) throw new Error("usePlayer must be used inside <PlayerProvider>");
  return context;
}
