// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import React, { createContext, useCallback, useEffect, useMemo, useState } from "react";
import { useAuth } from "@/contexts/AuthContext";
import { useRequiredContext } from "@/lib/useRequiredContext";
import { fetchMedia } from "@/lib/http";
import { BrowserOfflineStorage } from "@/lib/offline/browserOfflineStorage";
import {
  createOfflineLibrary,
  type OfflineLibrary,
  type OfflineQuality,
  type OfflineRecord,
  type OfflineSource,
} from "@/lib/offline/offlineLibrary";
import type { Track } from "@/lib/types";

interface OfflineDownloadsState {
  loaded: boolean;
  tracks: OfflineRecord[];
  download: (tracks: Track[], quality: OfflineQuality) => Promise<void>;
  remove: (trackIds: string[]) => Promise<void>;
  resolve: (trackId: string) => Promise<OfflineSource | null>;
}

const OfflineDownloadsContext = createContext<OfflineDownloadsState | null>(null);

export function OfflineDownloadsProvider({ children }: { children: React.ReactNode }) {
  const { user, loading: authLoading } = useAuth();
  const [library, setLibrary] = useState<OfflineLibrary | null>(null);
  const [tracks, setTracks] = useState<OfflineRecord[]>([]);
  const [loaded, setLoaded] = useState(false);

  useEffect(() => {
    if (authLoading) return;

    let active = true;
    let unsubscribe = () => {};

    if (!user) {
      /* eslint-disable react-hooks/set-state-in-effect -- the authenticated browser store changed. */
      setLibrary(null);
      setTracks([]);
      setLoaded(true);
      /* eslint-enable react-hooks/set-state-in-effect */
      return;
    }

    setLoaded(false);
    void createOfflineLibrary({
      userId: user.id,
      storage: new BrowserOfflineStorage(),
      fetchMedia,
    })
      .then((created) => {
        if (!active) return;

        const update = () => setTracks(created.getSnapshot().tracks);
        setLibrary(created);
        update();
        unsubscribe = created.subscribe(update);
        setLoaded(true);
      })
      .catch(() => {
        if (!active) return;
        setLibrary(null);
        setTracks([]);
        setLoaded(true);
      });

    return () => {
      active = false;
      unsubscribe();
    };
  }, [authLoading, user]);

  const download = useCallback(
    async (selected: Track[], quality: OfflineQuality) => {
      if (!library) throw new Error("Offline downloads are not ready.");
      await library.download({ tracks: selected, quality });
    },
    [library],
  );

  const remove = useCallback(
    async (trackIds: string[]) => {
      if (!library) return;
      await library.remove(trackIds);
    },
    [library],
  );

  const resolve = useCallback(
    (trackId: string) => library?.resolve(trackId) ?? Promise.resolve(null),
    [library],
  );

  const value = useMemo<OfflineDownloadsState>(
    () => ({ loaded, tracks, download, remove, resolve }),
    [loaded, tracks, download, remove, resolve],
  );

  return (
    <OfflineDownloadsContext.Provider value={value}>{children}</OfflineDownloadsContext.Provider>
  );
}

export function useOfflineDownloads(): OfflineDownloadsState {
  return useRequiredContext(
    OfflineDownloadsContext,
    "useOfflineDownloads",
    "OfflineDownloadsProvider",
  );
}
