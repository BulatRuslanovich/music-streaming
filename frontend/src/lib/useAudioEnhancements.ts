// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useEffect, useRef, useState, type RefObject } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useSettings } from "@/contexts/SettingsContext";
import { audioOutput } from "./audioOutput";
import { BufferedPlayback } from "./bufferedPlayback";
import { decodeTrack } from "./decodeTrack";
import { mediaUrl } from "./media";
import { queries } from "./queries";
import { advanceIn } from "./playerQueue";
import { useSoundSettings } from "./soundSettings";
import type { Track } from "./types";
import type { RepeatMode } from "./playerTypes";

export interface EnhancementEvents {
  transition: () => void;
  ended: () => void;
  progress: () => void;
  promoted: () => void;
  fallback: (position: number) => void;
}

export function useAudioEnhancements(input: {
  audioRef: RefObject<HTMLAudioElement | null>;
  currentTrack: Track | null;
  currentIndex: number;
  queue: Track[];
  orderRef: RefObject<number[]>;
  repeat: RepeatMode;
  isPlaying: boolean;
  volume: number;
  muted: boolean;
  events: RefObject<EnhancementEvents>;
}) {
  const settings = useSettings();
  const sound = useSoundSettings();
  const client = useQueryClient();
  const [buffered] = useState(() => new BufferedPlayback());
  const normalization = useQuery(
    queries.normalization(input.currentTrack?.id ?? "", sound.normalization),
  );
  const gain = sound.normalization === "off" ? 1 : (normalization.data?.gain ?? 1);
  const latest = useRef(input);
  useEffect(() => {
    latest.current = input;
  });
  const {
    audioRef,
    events,
    currentTrack,
    currentIndex,
    queue,
    orderRef,
    repeat,
    isPlaying,
    volume,
    muted,
  } = input;
  const enabled = sound.transition !== "off" && !settings.dataSaver && !settings.networkIsSlow;
  const quality = settings.effectiveQuality;

  useEffect(() => {
    const audio = audioRef.current;
    if (!audio) return;
    audioOutput.setNormalization(audio, gain);
    buffered.normalize(gain);
    const attach = () => {
      if (sound.normalization !== "off") {
        try {
          audioOutput.source(audio);
          void audioOutput.unlock();
        } catch {}
      }
    };
    if (!audio.paused) attach();
    audio.addEventListener("play", attach);
    return () => audio.removeEventListener("play", attach);
  }, [audioRef, buffered, gain, sound.normalization]);

  useEffect(() => {
    buffered.setVolume(muted ? 0 : volume);
    if (buffered.trackId) {
      if (isPlaying) {
        void audioOutput.unlock();
        buffered.play();
      } else buffered.pause();
    }
  }, [buffered, isPlaying, volume, muted]);

  useEffect(() => {
    if (enabled) return;
    if (buffered.trackId) {
      const position = buffered.position;
      buffered.stop();
      if (audioRef.current) delete audioRef.current.dataset.buffered;
      events.current.fallback(position);
    }
  }, [enabled, buffered, audioRef, events]);

  useEffect(() => {
    const controller = new AbortController();
    const audio = audioRef.current;
    if (!audio || !currentTrack || !enabled || !isPlaying || !navigator.onLine) return;
    // Ограничиваем память до декодирования. Длинные записи продолжают играть обычным потоком.
    if (currentTrack.durationSeconds <= 0 || currentTrack.durationSeconds > 600) return;
    const step = advanceIn(orderRef.current, currentIndex, 1, repeat === "all");
    const next = repeat !== "one" && step.kind === "play" ? queue[step.index] : undefined;
    const id = currentTrack.id;
    async function prepare() {
      try {
        if (buffered.trackId !== id) {
          const decoded = await decodeTrack(mediaUrl.stream(id, quality), controller.signal);
          if (controller.signal.aborted || audio!.dataset.trackId !== id) return;
          const context = audioOutput.getContext();
          if (context.state !== "running") return;
          const at = audio!.currentTime;
          buffered.load(id, decoded, at, latest.current.isPlaying, gain);
          audio!.dataset.buffered = "true";
          events.current.promoted();
          audio!.pause();
        }
        if (!next || next.durationSeconds <= 0 || next.durationSeconds > 600) return;
        const decodedNext = await decodeTrack(mediaUrl.stream(next.id, quality), controller.signal);
        let nextGain = 1;
        if (sound.normalization !== "off") {
          try {
            nextGain = (
              await client.ensureQueryData(queries.normalization(next.id, sound.normalization))
            ).gain;
          } catch {}
        }
        if (!controller.signal.aborted && buffered.trackId === id) {
          buffered.prepare(
            next.id,
            decodedNext,
            nextGain,
            sound.transition,
            sound.crossfadeSeconds,
          );
        }
      } catch {
        /* Сеть, формат или память: основной поток продолжает воспроизведение. */
      }
    }
    void prepare();
    return () => {
      controller.abort();
      buffered.cancelNext();
    };
  }, [
    currentTrack,
    currentIndex,
    queue,
    orderRef,
    repeat,
    enabled,
    isPlaying,
    quality,
    sound.transition,
    sound.crossfadeSeconds,
    sound.normalization,
    buffered,
    audioRef,
    client,
    gain,
    events,
  ]);

  useEffect(() => {
    const timer = setInterval(() => {
      const event = buffered.tick();
      if (event === "transition") events.current.transition();
      else if (event === "ended") events.current.ended();
      if (buffered.trackId) events.current.progress();
    }, 100);
    return () => {
      clearInterval(timer);
      buffered.stop();
    };
  }, [buffered, events]);

  return buffered;
}
