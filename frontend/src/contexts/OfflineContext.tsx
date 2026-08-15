"use client";

import React, { createContext, useCallback, useContext, useEffect, useMemo, useState } from "react";
import {
  clearOffline,
  downloadTrack,
  listOffline,
  offlineSupported,
  removeOffline,
  type OfflineTrack,
} from "@/lib/offline";
import type { Track } from "@/lib/types";
import { useSettings } from "./SettingsContext";
import { useT } from "./I18nContext";
import { useToast } from "./ToastContext";

interface OfflineState {
  /** Поддерживает ли браузер офлайн вообще; без этого весь интерфейс скачивания прячется. */
  supported: boolean;

  /** Нет сети прямо сейчас. */
  isOffline: boolean;

  /** Скачанное, от свежего к старому. */
  downloads: OfflineTrack[];

  /** Доля скачанного по трекам, которые качаются прямо сейчас. */
  progress: Record<string, number>;

  totalBytes: number;
  has: (trackId: string) => boolean;
  download: (tracks: Track | Track[]) => Promise<void>;
  remove: (trackId: string) => Promise<void>;
  clear: () => Promise<void>;
}

const OfflineContext = createContext<OfflineState | null>(null);

/**
 * Офлайн живёт отдельным слоем и намеренно не внутри плеера.
 *
 * Плееру про офлайн знать нечего: service worker отвечает на тот же адрес потока, поэтому
 * скачанный трек играет сам собой. Здесь остаётся только то, чего service worker не умеет, —
 * список скачанного, прогресс и кнопки.
 */
export function OfflineProvider({ children }: { children: React.ReactNode }) {
  const { effectiveQuality } = useSettings();
  const { notify, notifyError } = useToast();
  const t = useT();

  const [supported, setSupported] = useState(false);
  const [isOffline, setIsOffline] = useState(false);
  const [downloads, setDownloads] = useState<OfflineTrack[]>([]);
  const [progress, setProgress] = useState<Record<string, number>>({});

  useEffect(() => {
    if (!offlineSupported()) return;

    /* eslint-disable react-hooks/set-state-in-effect */
    setSupported(true);
    setIsOffline(!navigator.onLine);
    /* eslint-enable react-hooks/set-state-in-effect */

    void listOffline().then(setDownloads);

    if ("serviceWorker" in navigator) {
      void navigator.serviceWorker.register("/sw.js", { scope: "/" }).catch(() => {});
    }

    const online = () => setIsOffline(false);
    const offline = () => setIsOffline(true);

    window.addEventListener("online", online);
    window.addEventListener("offline", offline);

    return () => {
      window.removeEventListener("online", online);
      window.removeEventListener("offline", offline);
    };
  }, []);

  const download = useCallback(
    async (tracks: Track | Track[]) => {
      const wanted = Array.isArray(tracks) ? tracks : [tracks];
      const saved: OfflineTrack[] = [];

      for (const track of wanted) {
        setProgress((current) => ({ ...current, [track.id]: 0 }));

        try {
          saved.push(
            await downloadTrack(track, effectiveQuality, (fraction) =>
              setProgress((current) => ({ ...current, [track.id]: fraction })),
            ),
          );
        } catch (error) {
          notifyError(error, t("offline.downloadFailed", { title: track.title }));
        } finally {
          setProgress((current) => {
            const rest = { ...current };
            delete rest[track.id];

            return rest;
          });
        }
      }

      if (saved.length === 0) return;

      setDownloads(await listOffline());
      notify(t("offline.downloaded", { count: saved.length }), "success");
    },
    [effectiveQuality, notify, notifyError, t],
  );

  const remove = useCallback(async (trackId: string) => {
    await removeOffline(trackId);
    setDownloads(await listOffline());
  }, []);

  const clear = useCallback(async () => {
    await clearOffline();
    setDownloads([]);
  }, []);

  const value = useMemo<OfflineState>(() => {
    const ids = new Set(downloads.map((entry) => entry.track.id));

    return {
      supported,
      isOffline,
      downloads,
      progress,
      totalBytes: downloads.reduce((sum, entry) => sum + entry.bytes, 0),
      has: (trackId: string) => ids.has(trackId),
      download,
      remove,
      clear,
    };
  }, [supported, isOffline, downloads, progress, download, remove, clear]);

  return <OfflineContext.Provider value={value}>{children}</OfflineContext.Provider>;
}

export function useOffline(): OfflineState {
  const context = useContext(OfflineContext);
  if (!context) throw new Error("useOffline must be used inside <OfflineProvider>");
  return context;
}
