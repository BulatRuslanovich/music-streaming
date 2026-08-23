// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import React, { createContext, useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useRequiredContext } from "@/lib/useRequiredContext";
import { usePlayer } from "./PlayerContext";
import { useT } from "./I18nContext";
import { useToast } from "./ToastContext";

export type SleepPlan = { kind: "off" } | { kind: "timer"; endsAt: number } | { kind: "track" };

interface SleepTimerState {
  plan: SleepPlan;
  minutesLeft: number | null;
  startTimer: (minutes: number) => void;
  stopAfterTrack: () => void;
  cancel: () => void;
}

const SleepTimerContext = createContext<SleepTimerState | null>(null);

const FADE_MS = 5_000;

const FADE_STEPS = 25;

export function SleepTimerProvider({ children }: { children: React.ReactNode }) {
  const player = usePlayer();
  const { notify } = useToast();
  const t = useT();

  const [plan, setPlan] = useState<SleepPlan>({ kind: "off" });
  const [now, setNow] = useState(() => Date.now());

  const playerRef = useRef(player);

  useEffect(() => {
    playerRef.current = player;
  }, [player]);

  const armedTrackRef = useRef<string | null>(null);

  const fadeOut = useCallback(() => {
    const startVolume = playerRef.current.volume;
    let step = 0;

    const timer = window.setInterval(() => {
      step += 1;

      if (step >= FADE_STEPS) {
        window.clearInterval(timer);
        playerRef.current.pause();
        playerRef.current.setVolume(startVolume);
        return;
      }

      playerRef.current.setVolume((startVolume * (FADE_STEPS - step)) / FADE_STEPS);
    }, FADE_MS / FADE_STEPS);
  }, []);

  useEffect(() => {
    if (plan.kind !== "timer") return;

    const fire = () => {
      setPlan({ kind: "off" });
      notify(t("sleep.done"), "info");
      fadeOut();
    };

    const timer = window.setTimeout(fire, Math.max(0, plan.endsAt - Date.now()));
    const tick = window.setInterval(() => setNow(Date.now()), 30_000);

    return () => {
      window.clearTimeout(timer);
      window.clearInterval(tick);
    };
  }, [plan, fadeOut, notify, t]);

  useEffect(() => {
    if (plan.kind !== "track") return;

    const current = player.currentTrack?.id ?? null;
    armedTrackRef.current ??= current;

    if (current !== null && current === armedTrackRef.current) return;

    armedTrackRef.current = null;
    setPlan({ kind: "off" });
    player.pause();
    notify(t("sleep.done"), "info");
  }, [plan, player, notify, t]);

  const startTimer = useCallback((minutes: number) => {
    armedTrackRef.current = null;
    setPlan({ kind: "timer", endsAt: Date.now() + minutes * 60_000 });
    setNow(Date.now());
  }, []);

  const stopAfterTrack = useCallback(() => {
    armedTrackRef.current = null;
    setPlan({ kind: "track" });
  }, []);

  const cancel = useCallback(() => {
    armedTrackRef.current = null;
    setPlan({ kind: "off" });
  }, []);

  const minutesLeft =
    plan.kind === "timer" ? Math.max(1, Math.round((plan.endsAt - now) / 60_000)) : null;

  const value = useMemo<SleepTimerState>(
    () => ({ plan, minutesLeft, startTimer, stopAfterTrack, cancel }),
    [plan, minutesLeft, startTimer, stopAfterTrack, cancel],
  );

  return <SleepTimerContext.Provider value={value}>{children}</SleepTimerContext.Provider>;
}

export function useSleepTimer(): SleepTimerState {
  return useRequiredContext(SleepTimerContext, "useSleepTimer", "SleepTimerProvider");
}
