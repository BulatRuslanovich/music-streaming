// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import type { RefObject } from "react";
import { api } from "@/lib/api";
import { defaultDjVariety, mergeDjBatch, recommendationReasons } from "@/lib/djSession";
import { appendTracks, radioStartAfterInsert } from "@/lib/playerQueue";
import type { DjSessionState, PlaybackOrigin, RadioState, RepeatMode } from "@/lib/playerTypes";
import type { DjMode, DjVariety, Track } from "@/lib/types";
import { useSettings } from "@/contexts/SettingsContext";
import { useT } from "@/contexts/I18nContext";
import { useToast } from "@/contexts/ToastContext";

const RADIO_PREFETCH_AT = 1;

const DJ_INITIAL_BATCH = 10;

const DJ_NEXT_BATCH = 5;

interface DjSessionInput {
  queue: Track[];
  currentIndex: number;
  repeat: RepeatMode;
  queueRef: RefObject<Track[]>;
  orderRef: RefObject<number[]>;
  applyQueue: (queue: Track[], order: number[]) => void;

  // INFO: очередь заводит вызывающий — диджей только приносит треки и не знает про shuffle и позицию.
  startTracks: (tracks: Track[], startIndex: number, origin: PlaybackOrigin) => void;
}

interface DjSession {
  session: DjSessionState | null;
  loading: boolean;
  radio: RadioState;

  start: (mode: DjMode, seedTrack?: Track | null) => Promise<boolean>;
  setVariety: (variety: DjVariety) => void;

  stop: () => void;
  resetRadio: () => void;
  restore: (session: DjSessionState | null, radioFrom?: number) => void;

  noteInsert: (at: number, queueLength: number) => void;
  radioFrom: () => number;
  resolveOrigin: (index: number) => PlaybackOrigin | null;
}

export function useDjSession({
  queue,
  currentIndex,
  repeat,
  queueRef,
  orderRef,
  applyQueue,
  startTracks,
}: DjSessionInput): DjSession {
  const { notify, notifyError } = useToast();
  const t = useT();
  const settings = useSettings();

  const [dj, setDj] = useState<DjSessionState | null>(null);
  const [djLoading, setDjLoading] = useState(false);
  const [radio, setRadio] = useState<RadioState>("idle");

  const radioRef = useRef<{ inFlight: boolean; seed: string | null }>({
    inFlight: false,
    seed: null,
  });

  const radioFromRef = useRef(Number.MAX_SAFE_INTEGER);
  const djGenerationRef = useRef(0);
  const djInFlightRef = useRef(false);

  const resetRadio = useCallback(() => {
    radioRef.current = { inFlight: false, seed: null };
    radioFromRef.current = Number.MAX_SAFE_INTEGER;
    setRadio("idle");
  }, []);

  const stop = useCallback(() => {
    djGenerationRef.current += 1;
    djInFlightRef.current = false;
    setDj(null);
    setDjLoading(false);
  }, []);

  const restore = useCallback((session: DjSessionState | null, radioFrom?: number) => {
    if (radioFrom !== undefined) radioFromRef.current = radioFrom;

    djGenerationRef.current += 1;
    djInFlightRef.current = false;
    setDj(session);
  }, []);

  const noteInsert = useCallback((at: number, queueLength: number) => {
    radioFromRef.current = radioStartAfterInsert(radioFromRef.current, at, queueLength);
  }, []);

  const radioFrom = useCallback(() => radioFromRef.current, []);

  const resolveOrigin = useCallback(
    (index: number): PlaybackOrigin | null => {
      if (dj) return { source: "dj", sourceId: dj.seedTrackId ?? undefined };

      if (index >= radioFromRef.current) {
        return { source: "radio", sourceId: queue[radioFromRef.current - 1]?.id };
      }

      return null;
    },
    [dj, queue],
  );

  const start = useCallback(
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
        resetRadio();
        startTracks(tracks, 0, { source: "dj", sourceId: batch.seedTrackId ?? undefined });
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
    [notify, notifyError, resetRadio, startTracks, t],
  );

  const setVariety = useCallback((variety: DjVariety) => {
    setDj((session) => (session ? { ...session, variety, status: "idle" } : session));
  }, []);

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
  }, [dj, settings.autoplay, currentIndex, queue, repeat, applyQueue, queueRef, orderRef]);

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
  }, [dj, djLoading, currentIndex, queue, repeat, applyQueue, queueRef, orderRef]);

  return {
    session: dj,
    loading: djLoading,
    radio,
    start,
    setVariety,
    stop,
    resetRadio,
    restore,
    noteInsert,
    radioFrom,
    resolveOrigin,
  };
}
