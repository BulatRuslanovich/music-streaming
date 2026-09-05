// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import React, { createContext, useCallback, useEffect, useMemo, useRef, useState } from "react";
import { api } from "@/lib/api";
import { ConnectContext } from "./ConnectContext";
import { useConnectSession } from "@/lib/useConnectSession";
import { visualizer } from "@/lib/audioVisualizer";
import { validDjSession } from "@/lib/djSession";
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
} from "@/lib/playerQueue";
import type {
  PlaybackOrigin,
  PlayerActions,
  PlayerNowPlaying,
  PlayerProgress,
  PlayerState,
  QueueSnapshot,
  RepeatMode,
} from "@/lib/playerTypes";
import type { Track } from "@/lib/types";
import { useDjSession } from "@/lib/useDjSession";
import { usePlaybackEngine } from "@/lib/usePlaybackEngine";
import { useExclusivePlayback } from "@/lib/useExclusivePlayback";
import { useMediaSession } from "@/lib/useMediaSession";
import { readPersistedPlayer, usePersistedPlayer } from "@/lib/usePlayerStorage";
import { useT } from "./I18nContext";
import { useToast } from "./ToastContext";

export type { PlaybackOrigin, RepeatMode } from "@/lib/playerTypes";

const PlayerStateContext = createContext<PlayerState | null>(null);

const PlayerActionsContext = createContext<PlayerActions | null>(null);

const PlayerProgressContext = createContext<PlayerProgress | null>(null);

const PlayerNowPlayingContext = createContext<PlayerNowPlaying | null>(null);

// Контекст держит очередь и публичный API плеера, а всю оркестровку отдаёт двум модулям:
// usePlaybackEngine (звук, HLS, восстановление и адаптивный откат) и useDjSession
// (радио и диджей, которые пополняют очередь).
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

  // Порядок живёт в ref (движку и диджею нужно свежее значение без замыканий), но его
  // зеркало нужно и в состоянии: `nextTrack` считается на рендере, а читать там ref нельзя.
  const [order, setOrder] = useState<number[]>([]);

  const applyQueue = useCallback((next: Track[], nextOrder: number[]) => {
    queueRef.current = next;
    orderRef.current = nextOrder;
    setQueue(next);
    setOrder(nextOrder);
  }, []);

  const currentTrack = currentIndex >= 0 ? (queue[currentIndex] ?? null) : null;

  // INFO: движок и диджей замкнуты друг на друга — движку нужно знать, откуда взялся трек
  // (радио или диджей), а диджею нечем завести очередь, кроме того же кода, что и у движка.
  // Ссылка на свежие колбэки разрывает цикл, не заводя ни шину событий, ни фабрики.
  const wiring = useRef({
    startTracks: (() => {}) as (
      tracks: Track[],
      startIndex: number,
      origin: PlaybackOrigin,
    ) => void,
    trackEnded: () => {},
  });

  const startTracks = useCallback(
    (tracks: Track[], startIndex: number, origin: PlaybackOrigin) =>
      wiring.current.startTracks(tracks, startIndex, origin),
    [],
  );

  const onTrackEnded = useCallback(() => wiring.current.trackEnded(), []);

  const {
    session: dj,
    loading: djLoading,
    radio,
    start: startDj,
    setVariety: setDjVariety,
    stop: stopDjSession,
    resetRadio,
    restore: restoreDjSession,
    noteInsert: noteRadioInsert,
    radioFrom,
    resolveOrigin,
  } = useDjSession({
    queue,
    currentIndex,
    repeat,
    queueRef,
    orderRef,
    applyQueue,
    startTracks,
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
    seekTo,
    getDuration,
    recoverSource,
    startQueue,
    resetProgress,
    clearProgress,
    restoreProgress,
    resumeSavedPosition,
  } = usePlaybackEngine({
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
  });

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
          resumeSavedPosition(saved.position ?? 0);
        }
      }

      if (typeof saved.volume === "number") setVolumeState(saved.volume);
      if (typeof saved.muted === "boolean") setMuted(saved.muted);
      if (typeof saved.shuffle === "boolean") setShuffle(saved.shuffle);
      if (saved.repeat === "off" || saved.repeat === "all" || saved.repeat === "one") {
        setRepeat(saved.repeat);
      }
      if (validDjSession(saved.dj)) restoreDjSession(saved.dj);
    }

    setRestored(true);
    /* eslint-enable react-hooks/set-state-in-effect -- // INFO: дальнейшие эффекты не должны менять состояние синхронно. */
  }, [applyQueue, restoreDjSession, resumeSavedPosition]);

  usePersistedPlayer(
    { queue, index: currentIndex, position, volume, muted, shuffle, repeat, dj },
    restored,
    isPlaying,
  );

  const replaceQueue = useCallback(
    (tracks: Track[], startIndex = 0, origin: PlaybackOrigin = {}) => {
      if (tracks.length === 0) return;

      const safeIndex = Math.min(Math.max(startIndex, 0), tracks.length - 1);

      startQueue(origin);
      resetRadio();

      applyQueue(tracks, buildOrder(tracks.length, shuffle, safeIndex));
      setCurrentIndex(safeIndex);
      setIsPlaying(true);
    },
    [applyQueue, resetRadio, shuffle, startQueue],
  );

  const playQueue = useCallback(
    (tracks: Track[], startIndex = 0, origin: PlaybackOrigin = {}) => {
      stopDjSession();
      replaceQueue(tracks, startIndex, origin);
    },
    [replaceQueue, stopDjSession],
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
      const step = advanceIn(orderRef.current, currentIndex, direction, repeat === "all");

      switch (step.kind) {
        case "none":
          return;

        case "restart":
          seekTo(0);
          return;

        case "stop":
          setIsPlaying(false);
          if (auto) seekTo(0);
          return;

        case "play":
          setCurrentIndex(step.index);
          resetProgress();
          setIsPlaying(true);
      }
    },
    [currentIndex, repeat, resetProgress, seekTo],
  );

  useEffect(() => {
    wiring.current = {
      startTracks: replaceQueue,
      trackEnded: () => advance(1, { auto: true }),
    };
  }, [advance, replaceQueue]);

  const next = useCallback(() => advance(1), [advance]);
  const previous = useCallback(() => {
    if (getPosition() > 3) {
      seekTo(0);
      return;
    }
    advance(-1);
  }, [advance, getPosition, seekTo]);

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

    // Через applyQueue, а не присваиванием в ref: так у порядка остаётся один писатель
    // и зеркало в состоянии не разъезжается с ним.
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
      dj,
    }),
    [currentIndex, dj, radioFrom, trackedPosition],
  );

  const restoreQueue = useCallback(
    (snapshot: QueueSnapshot) => {
      restoreProgress(snapshot.queue[snapshot.index]?.id, snapshot.position);
      restoreDjSession(snapshot.dj, snapshot.radioFrom);

      applyQueue(snapshot.queue, snapshot.order);
      setCurrentIndex(snapshot.index);
    },
    [applyQueue, restoreDjSession, restoreProgress],
  );

  const clearQueue = useCallback(() => {
    stopDjSession();
    resetRadio();

    applyQueue([], []);
    setCurrentIndex(-1);
    setIsPlaying(false);
    clearProgress();
  }, [applyQueue, clearProgress, resetRadio, stopDjSession]);

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

  const visualizerTrackId = currentTrack?.id ?? null;

  // Элемент один на всё приложение, поэтому цепляем отвод спектра к нему один раз:
  // `createMediaElementSource` для одного элемента можно позвать только однажды.
  useEffect(() => {
    if (audioRef.current) visualizer.attach(audioRef.current);
  }, [audioRef]);

  useEffect(() => {
    visualizer.setTrack(visualizerTrackId);
  }, [visualizerTrackId]);

  useEffect(() => {
    visualizer.setPlaying(isPlaying);
  }, [isPlaying]);

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
      dj,
      djLoading,
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
      startDj,
      setDjVariety,
    ],
  );

  const connect = useConnectSession(
    () => ({
      queue: queueRef.current.map((track) => track.id),
      order: [...orderRef.current],
      index: currentIndex,
      position: Math.max(0, getPosition()),
      isPlaying,
      volume,
      muted,
      shuffle,
      repeat,
      title: currentTrack?.title ?? null,
    }),
    async (command, signal) => {
      switch (command.kind) {
        case "play":
          if (currentTrack) play();
          break;
        case "pause":
          pause();
          break;
        case "next":
          next();
          break;
        case "previous":
          previous();
          break;
        case "seek":
          seek(command.value ?? 0);
          break;
        case "volume":
          setVolume(command.value ?? 1);
          break;
        case "transfer": {
          const snapshot = command.state;
          if (!snapshot) return;
          const tracks = await api.connectTracks(snapshot.queue, signal);
          if (signal.aborted) return;
          stopDjSession();
          resetRadio();
          startQueue({ source: "queue" });
          const at = Math.min(
            snapshot.position,
            tracks[snapshot.index]?.durationSeconds || snapshot.position,
          );
          if (tracks[snapshot.index]?.id === currentTrack?.id) seekTo(at);
          else resumeSavedPosition(at);
          applyQueue(tracks, snapshot.order);
          setCurrentIndex(snapshot.index);
          setShuffle(snapshot.shuffle);
          setRepeat(snapshot.repeat);
          setIsPlaying(snapshot.isPlaying);
          break;
        }
      }
    },
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
            <ConnectContext.Provider value={connect}>{children}</ConnectContext.Provider>
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

/**
 * Для списков и карточек, которым нужно только «этот ли трек играет». В отличие от
 * `usePlayerState` не тянет за собой очередь, поэтому лайк одного трека не перерисовывает
 * все полки главной.
 */
export function useNowPlaying(): PlayerNowPlaying {
  return useRequiredContext(PlayerNowPlayingContext, "useNowPlaying", "PlayerProvider");
}
