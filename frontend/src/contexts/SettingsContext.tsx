// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import React, { createContext, useCallback, useContext, useEffect, useMemo, useState } from "react";
import { api } from "@/lib/api";
import type { AudioQuality, AudioQualityOption, UserSettings } from "@/lib/types";

interface SettingsState extends UserSettings {
  qualities: AudioQualityOption[];

  historyThresholdSeconds: number;

  maxUploadBytes: number;

  effectiveQuality: AudioQuality;

  networkIsSlow: boolean;

  loaded: boolean;
  update: (changes: Partial<UserSettings>) => void;
}

const SettingsContext = createContext<SettingsState | null>(null);

const DEFAULTS: UserSettings = {
  autoplay: true,
  quality: "Normal",
  dataSaver: false,
  timeZone: "UTC",
};

const DEFAULT_HISTORY_THRESHOLD = 30;

const DEFAULT_MAX_UPLOAD_BYTES = 200 * 1024 * 1024;

export function SettingsProvider({ children }: { children: React.ReactNode }) {
  const [settings, setSettings] = useState<UserSettings>(DEFAULTS);
  const [qualities, setQualities] = useState<AudioQualityOption[]>([]);
  const [historyThresholdSeconds, setHistoryThreshold] = useState(DEFAULT_HISTORY_THRESHOLD);
  const [maxUploadBytes, setMaxUploadBytes] = useState(DEFAULT_MAX_UPLOAD_BYTES);
  const [loaded, setLoaded] = useState(false);
  const networkIsSlow = useSlowNetwork();

  useEffect(() => {
    let active = true;

    void (async () => {
      const [config, saved] = await Promise.allSettled([api.config(), api.settings()]);

      if (!active) return;

      if (config.status === "fulfilled") {
        setQualities(config.value.audioQualities);
        if (config.value.historyThresholdSeconds > 0) {
          setHistoryThreshold(config.value.historyThresholdSeconds);
        }
        if (config.value.maxUploadBytes > 0) {
          setMaxUploadBytes(config.value.maxUploadBytes);
        }
      }

      const detected = Intl.DateTimeFormat().resolvedOptions().timeZone;

      if (saved.status === "fulfilled") {
        const current = saved.value;

        setSettings(
          detected && detected !== current.timeZone
            ? await api.updateSettings({ timeZone: detected }).catch(() => current)
            : current,
        );
      }

      setLoaded(true);
    })();

    return () => {
      active = false;
    };
  }, []);

  const update = useCallback((changes: Partial<UserSettings>) => {
    setSettings((current) => ({ ...current, ...changes }));
    void api.updateSettings(changes).catch(() => {});
  }, []);

  const value = useMemo<SettingsState>(
    () => ({
      ...settings,
      qualities,
      historyThresholdSeconds,
      maxUploadBytes,
      effectiveQuality: settings.dataSaver ? "Low" : settings.quality,
      networkIsSlow,
      loaded,
      update,
    }),
    [settings, qualities, historyThresholdSeconds, maxUploadBytes, networkIsSlow, loaded, update],
  );

  return <SettingsContext.Provider value={value}>{children}</SettingsContext.Provider>;
}

export function useSettings(): SettingsState {
  const context = useContext(SettingsContext);
  if (!context) throw new Error("useSettings must be used inside <SettingsProvider>");
  return context;
}

interface NetworkInformation extends EventTarget {
  effectiveType?: string;
  saveData?: boolean;
}

function useSlowNetwork(): boolean {
  const [slow, setSlow] = useState(false);

  useEffect(() => {
    const connection = (navigator as Navigator & { connection?: NetworkInformation }).connection;
    if (!connection) return;

    const read = () =>
      setSlow(
        connection.saveData === true ||
          connection.effectiveType === "2g" ||
          connection.effectiveType === "slow-2g",
      );

    read();
    connection.addEventListener("change", read);

    return () => connection.removeEventListener("change", read);
  }, []);

  return slow;
}
