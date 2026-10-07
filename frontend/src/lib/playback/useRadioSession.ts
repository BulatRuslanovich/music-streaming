// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import type { RefObject } from "react";
import { api } from "@/lib/api";
import { mergeRadioBatch, queueSignals, recommendationReasons } from "@/lib/playback/radioSession";
import { appendTracks, radioStartAfterInsert } from "@/lib/playback/playerQueue";
import type { RadioSessionState, RadioState, RepeatMode } from "@/lib/playback/playerTypes";
import type { Track } from "@/lib/types";
import { useT } from "@/contexts/I18nContext";
import { useToast } from "@/lib/useToast";

const RADIO_PREFETCH_AT = 1;

const RADIO_INITIAL_BATCH = 10;

interface RadioSessionInput {
  queue: Track[];
  currentIndex: number;
  repeat: RepeatMode;
  autoplay: boolean;
  queueRef: RefObject<Track[]>;
  orderRef: RefObject<number[]>;
  applyQueue: (queue: Track[], order: number[]) => void;
}

interface RadioSession {
  session: RadioSessionState | null;
  radio: RadioState;

  start: (seedTrack?: Track | null, mood?: string | null) => Promise<Track[] | null>;

  stop: () => void;
  resetRadio: () => void;
  restore: (session: RadioSessionState | null, radioFrom?: number) => void;

  noteInsert: (at: number, queueLength: number) => void;
  radioFrom: () => number;
}

export function useRadioSession({
  queue,
  currentIndex,
  repeat,
  autoplay,
  queueRef,
  orderRef,
  applyQueue,
}: RadioSessionInput): RadioSession {
  const { notify, notifyError } = useToast();
  const t = useT();

  const [session, setSession] = useState<RadioSessionState | null>(null);
  const [starting, setStarting] = useState(false);
  const [radio, setRadio] = useState<RadioState>("idle");

  const radioRef = useRef<{ inFlight: boolean; seed: string | null }>({
    inFlight: false,
    seed: null,
  });

  const radioFromRef = useRef(Number.MAX_SAFE_INTEGER);
  const generationRef = useRef(0);

  const resetRadio = useCallback(() => {
    generationRef.current += 1;
    radioRef.current = { inFlight: false, seed: null };
    radioFromRef.current = Number.MAX_SAFE_INTEGER;
    setRadio("idle");
  }, []);

  const stop = useCallback(() => {
    generationRef.current += 1;
    setSession(null);
    setStarting(false);
  }, []);

  const restore = useCallback((restored: RadioSessionState | null, radioFrom?: number) => {
    if (radioFrom !== undefined) radioFromRef.current = radioFrom;

    generationRef.current += 1;
    setSession(restored);
  }, []);

  const noteInsert = useCallback((at: number, queueLength: number) => {
    radioFromRef.current = radioStartAfterInsert(radioFromRef.current, at, queueLength);
  }, []);

  const radioFrom = useCallback(() => radioFromRef.current, []);

  const start = useCallback(
    async (seedTrack: Track | null = null, mood: string | null = null) => {
      const generation = ++generationRef.current;
      setStarting(true);

      try {
        const batch = await api.radio({
          seedTrackId: seedTrack?.id ?? null,
          exclude: [],
          limit: RADIO_INITIAL_BATCH - (seedTrack ? 1 : 0),
          mood,
        });
        if (generation !== generationRef.current) return null;

        if (batch.tracks.length === 0 && !seedTrack) {
          notify(t("radio.empty"), "info");
          return null;
        }

        setSession({
          seedTrackId: batch.seedTrackId,
          mood,
          reasons: recommendationReasons(batch.tracks),
          signals: queueSignals(batch.tracks),
        });
        return [...(seedTrack ? [seedTrack] : []), ...batch.tracks.map((item) => item.track)];
      } catch (error) {
        if (generation === generationRef.current) notifyError(error, t("radio.failed"));
        return null;
      } finally {
        setStarting(false);
      }
    },
    [notify, notifyError, t],
  );

  useEffect(() => {
    if (starting || currentIndex < 0 || repeat !== "off" || (!autoplay && !session)) return;

    const order = orderRef.current;
    const position = order.indexOf(currentIndex);
    if (position < 0 || order.length - position - 1 > RADIO_PREFETCH_AT) return;

    const seed = queue[currentIndex]?.id ?? session?.seedTrackId ?? null;
    if (radioRef.current.inFlight || radioRef.current.seed === seed) return;

    const generation = generationRef.current;
    radioRef.current = { inFlight: true, seed };
    setRadio("loading");

    void api
      .radio({
        seedTrackId: seed,
        exclude: queue.map((track) => track.id),
        mood: session?.mood,
      })
      .then((batch) => {
        if (generation !== generationRef.current) return;

        const current = queueRef.current;
        const merged = mergeRadioBatch(
          current,
          session?.reasons ?? {},
          session?.signals ?? {},
          batch.tracks,
        );

        if (merged.tracks.length === 0) {
          setRadio(batch.tracks.length === 0 ? "empty" : "idle");
          return;
        }

        radioFromRef.current = Math.min(radioFromRef.current, current.length);

        const next = appendTracks(current, orderRef.current, merged.tracks);
        applyQueue(next.queue, next.order);

        setSession((active) =>
          active
            ? {
                ...active,
                seedTrackId: batch.seedTrackId ?? active.seedTrackId,
                reasons: merged.reasons,
                signals: merged.signals,
              }
            : active,
        );
        setRadio("idle");
      })
      .catch(() => {
        if (generation === generationRef.current) setRadio("failed");
      })
      .finally(() => {
        radioRef.current = { ...radioRef.current, inFlight: false };
      });
  }, [session, starting, currentIndex, queue, repeat, autoplay, applyQueue, queueRef, orderRef]);

  return {
    session,
    radio,
    start,
    stop,
    resetRadio,
    restore,
    noteInsert,
    radioFrom,
  };
}
