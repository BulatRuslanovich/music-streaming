// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import React, { createContext, useCallback, useEffect, useMemo, useRef, useState } from "react";
import { recordEvent } from "@/lib/events";
import { useRequiredContext } from "@/lib/useRequiredContext";
import {
  advanceIn,
  appendTrack,
  buildOrder,
  indexAfterRemoval,
  insertAfter,
  moveInQueue as reorderQueue,
  remapIndexAfterMove,
} from "@/lib/playback/playerQueue";
import type {
  PlayerActions,
  PlayerNowPlaying,
  PlayerProgress,
  PlayerState,
  QueueSnapshot,
  RepeatMode,
} from "@/lib/playback/playerTypes";
import type { Track } from "@/lib/types";
import { useRadioSession } from "@/lib/playback/useRadioSession";
import { usePlaybackEngine } from "@/lib/playback/usePlaybackEngine";
import { useExclusivePlayback } from "@/lib/playback/useExclusivePlayback";
import { useMediaSession } from "@/lib/playback/useMediaSession";
import { readPersistedPlayer, usePersistedPlayer } from "@/lib/playback/usePlayerStorage";
import { useT } from "./I18nContext";
import { useToast } from "@/lib/useToast";

export type { RepeatMode } from "@/lib/playback/playerTypes";

const PlayerStateContext = createContext<PlayerState | null>(null);

const PlayerActionsContext = createContext<PlayerActions | null>(null);

const PlayerProgressContext = createContext<PlayerProgress | null>(null);

const PlayerNowPlayingContext = createContext<PlayerNowPlaying | null>(null);

export function PlayerProvider({ children }: { children: React.ReactNode }) {
  const { notify } = useToast();
  const t = useT();

  const [queue, setQueue] = useState<Track[]>([]);
  const [currentIndex, setCurrentIndex] = useState(-1);
  const [isPlaying, setIsPlaying] = useState(false);
  const [volume, setVolumeState] = useState(1);
  const [muted, setMuted] = useState(false);
  const [shuffle, setShuffle] = useState(false);
  const [repeat, setRepeat] = useState<RepeatMode>("off");
  const [restored, setRestored] = useState(false);

  const orderRef = useRef<number[]>([]);
  const queueRef = useRef<Track[]>([]);

  const [order, setOrder] = useState<number[]>([]);

  const applyQueue = useCallback((next: Track[], nextOrder: number[]) => {
    queueRef.current = next;
    orderRef.current = nextOrder;
    setQueue(next);
    setOrder(nextOrder);
  }, []);

  const currentTrack = currentIndex >= 0 ? (queue[currentIndex] ?? null) : null;

  const trackEnded = useRef(() => {});
  const onTrackEnded = useCallback(() => trackEnded.current(), []);

  const {
    session: radioSession,
    radio,
    start: startRadioSession,
    stop: stopRadioSession,
    resetRadio,
    restore: restoreRadioSession,
    noteInsert: noteRadioInsert,
    radioFrom,
  } = useRadioSession({
    queue,
    currentIndex,
    repeat,
    queueRef,
    orderRef,
    applyQueue,
  });

  const {
    audioRef,
    audioProps,
    position,
    duration,
    buffered,
    getPosition,
    trackedPosition,
    seek,
    seekBy,
    scrubBy,
    commitScrub,
    getDuration,
    recoverSource,
    startQueue,
    resetProgress,
    resumeAt,
  } = usePlaybackEngine({
    currentTrack,
    repeat,
    isPlaying,
    setIsPlaying,
    volume,
    muted,
    onTrackEnded,
  });

  useEffect(() => {
    /* eslint-disable react-hooks/set-state-in-effect */
    const saved = readPersistedPlayer();
    if (saved) {
      applyQueue(
        saved.queue,
        saved.queue.map((_, index) => index),
      );
      if (saved.index >= 0) {
        setCurrentIndex(saved.index);
        resumeAt(saved.queue[saved.index].id, saved.position);
      }

      setVolumeState(saved.volume);
      setMuted(saved.muted);
      setShuffle(saved.shuffle);
      setRepeat(saved.repeat);
      if (saved.radioSession) restoreRadioSession(saved.radioSession);
    }

    setRestored(true);
    /* eslint-enable react-hooks/set-state-in-effect */
  }, [applyQueue, restoreRadioSession, resumeAt]);

  usePersistedPlayer(
    { queue, index: currentIndex, position, volume, muted, shuffle, repeat, radioSession },
    restored,
    isPlaying,
  );

  const replaceQueue = useCallback(
    (tracks: Track[], startIndex = 0) => {
      if (tracks.length === 0) return;

      const safeIndex = Math.min(Math.max(startIndex, 0), tracks.length - 1);

      startQueue();
      resetRadio();

      applyQueue(tracks, buildOrder(tracks.length, shuffle, safeIndex));
      setCurrentIndex(safeIndex);
      setIsPlaying(true);
    },
    [applyQueue, resetRadio, shuffle, startQueue],
  );

  const playQueue = useCallback(
    (tracks: Track[], startIndex = 0) => {
      stopRadioSession();
      replaceQueue(tracks, startIndex);
    },
    [replaceQueue, stopRadioSession],
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
      const step = advanceIn(orderRef.current, currentIndex, direction, repeat === "all");

      switch (step.kind) {
        case "none":
          return;

        case "restart":
          seek(0);
          return;

        case "stop":
          setIsPlaying(false);
          if (auto) seek(0);
          return;

        case "play":
          setCurrentIndex(step.index);
          resetProgress();
          setIsPlaying(true);
      }
    },
    [currentIndex, repeat, resetProgress, seek],
  );

  useEffect(() => {
    trackEnded.current = () => advance(1, { auto: true });
  }, [advance]);

  const startRadio = useCallback(
    async (seedTrack?: Track | null) => {
      const tracks = await startRadioSession(seedTrack);
      if (tracks) replaceQueue(tracks, 0);
      return tracks !== null;
    },
    [replaceQueue, startRadioSession],
  );

  const next = useCallback(() => advance(1), [advance]);
  const previous = useCallback(() => {
    if (getPosition() > 3) {
      seek(0);
      return;
    }
    advance(-1);
  }, [advance, getPosition, seek]);

  const toggle = useCallback(() => {
    if (!currentTrack) return;

    recoverSource();
    setIsPlaying((playing) => !playing);
  }, [currentTrack, recoverSource]);

  const play = useCallback(() => {
    recoverSource();
    setIsPlaying(true);
  }, [recoverSource]);
  const pause = useCallback(() => setIsPlaying(false), []);

  const setVolume = useCallback((next: number) => {
    const clamped = Math.max(0, Math.min(1, next));
    setVolumeState(clamped);
    if (clamped > 0) setMuted(false);
  }, []);

  const toggleMute = useCallback(() => setMuted((value) => !value), []);

  const toggleShuffle = useCallback(() => {
    const nowShuffled = !shuffle;

    applyQueue(queueRef.current, buildOrder(queue.length, nowShuffled, currentIndex));
    setShuffle(nowShuffled);
  }, [applyQueue, queue.length, currentIndex, shuffle]);

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

      noteRadioInsert(currentIndex + 1, current.length);

      applyQueue(next.queue, next.order);
    },
    [addToQueue, applyQueue, currentIndex, noteRadioInsert],
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
      position: trackedPosition(),
      radioFrom: radioFrom(),
      radioSession,
    }),
    [currentIndex, radioSession, radioFrom, trackedPosition],
  );

  const restoreQueue = useCallback(
    (snapshot: QueueSnapshot) => {
      resumeAt(snapshot.queue[snapshot.index]?.id, snapshot.position);
      restoreRadioSession(snapshot.radioSession, snapshot.radioFrom);

      applyQueue(snapshot.queue, snapshot.order);
      setCurrentIndex(snapshot.index);
    },
    [applyQueue, restoreRadioSession, resumeAt],
  );

  const clearQueue = useCallback(() => {
    stopRadioSession();
    resetRadio();

    applyQueue([], []);
    setCurrentIndex(-1);
    setIsPlaying(false);
    resetProgress();
  }, [applyQueue, resetProgress, resetRadio, stopRadioSession]);

  const jumpTo = useCallback(
    (index: number) => {
      if (index < 0 || index >= queue.length) return;

      setCurrentIndex(index);
      resetProgress();
      setIsPlaying(true);
    },
    [queue.length, resetProgress],
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

  const nextTrack = useMemo<Track | null>(() => {
    const step = advanceIn(order, currentIndex, 1, repeat === "all");
    return step.kind === "play" ? (queue[step.index] ?? null) : null;
  }, [order, queue, currentIndex, repeat]);

  const state = useMemo<PlayerState>(
    () => ({
      queue,
      currentTrack,
      nextTrack,
      currentIndex,
      isPlaying,
      volume,
      muted,
      shuffle,
      repeat,
      radio,
      radioSession,
    }),
    [
      queue,
      currentTrack,
      nextTrack,
      currentIndex,
      isPlaying,
      volume,
      muted,
      shuffle,
      repeat,
      radio,
      radioSession,
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
      scrubBy,
      commitScrub,
      getDuration,
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
      startRadio,
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
      scrubBy,
      commitScrub,
      getDuration,
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
      startRadio,
    ],
  );

  const progress = useMemo<PlayerProgress>(
    () => ({ position, duration, buffered, getPosition }),
    [position, duration, buffered, getPosition],
  );

  const currentTrackId = currentTrack?.id ?? null;
  const currentAlbumId = currentTrack?.albumId ?? null;

  const nowPlaying = useMemo<PlayerNowPlaying>(
    () => ({ currentTrackId, currentAlbumId, isPlaying }),
    [currentTrackId, currentAlbumId, isPlaying],
  );

  return (
    <PlayerStateContext.Provider value={state}>
      <PlayerActionsContext.Provider value={actions}>
        <PlayerNowPlayingContext.Provider value={nowPlaying}>
          <PlayerProgressContext.Provider value={progress}>
            {children}
          </PlayerProgressContext.Provider>
        </PlayerNowPlayingContext.Provider>
        <audio ref={audioRef} {...audioProps} />
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

export function useNowPlaying(): PlayerNowPlaying {
  return useRequiredContext(PlayerNowPlayingContext, "useNowPlaying", "PlayerProvider");
}
