"use client";

import { useEffect, useRef } from "react";
import { deviceId } from "@/lib/events";
import { API_BASE, refreshSession } from "@/lib/http";

const RECONNECT_DELAYS_MS = [1000, 3000, 8000];

export function useExclusivePlayback(isPlaying: boolean, onDisplaced: () => void): void {
  const displaced = useRef(onDisplaced);
  useEffect(() => {
    displaced.current = onDisplaced;
  });

  useEffect(() => {
    if (!isPlaying) return;

    let source: EventSource | null = null;
    let timer: number | null = null;
    let attempt = 0;
    let stopped = false;

    const open = () => {
      if (stopped) return;

      source = new EventSource(
        `${API_BASE}/playback/session?deviceId=${encodeURIComponent(deviceId())}`,
      );

      source.addEventListener("open", () => {
        attempt = 0;
      });

      source.addEventListener("displaced", () => {
        stopped = true;
        source?.close();
        displaced.current();
      });

      source.addEventListener("error", () => {
        if (source?.readyState !== EventSource.CLOSED) return;

        source.close();
        source = null;

        const delay = RECONNECT_DELAYS_MS[attempt];
        if (delay === undefined) return;
        attempt += 1;

        timer = window.setTimeout(() => {
          timer = null;

          void refreshSession().finally(open);
        }, delay);
      });
    };

    open();

    return () => {
      stopped = true;
      if (timer !== null) window.clearTimeout(timer);
      source?.close();
    };
  }, [isPlaying]);
}
