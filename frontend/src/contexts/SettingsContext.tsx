// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import React, { createContext, useCallback, useEffect, useMemo, useState } from "react";
import { api } from "@/lib/api";
import { refreshSession } from "@/lib/http";
import { isStale, renewalIntervalMs } from "@/lib/sessionRenewal";
import { useRequiredContext } from "@/lib/useRequiredContext";
import type { AudioQuality, AudioQualityOption, UserSettings } from "@/lib/types";
import { useAuth } from "./AuthContext";

interface SettingsState extends UserSettings {
  qualities: AudioQualityOption[];

  historyThresholdSeconds: number;

  maxUploadBytes: number;

  maxImageUploadBytes: number;

  hlsEnabled: boolean;

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

const DEFAULT_MAX_IMAGE_UPLOAD_BYTES = 8 * 1024 * 1024;

export function SettingsProvider({ children }: { children: React.ReactNode }) {
  const { user, loading: authLoading } = useAuth();
  const [settings, setSettings] = useState<UserSettings>(DEFAULTS);
  const [qualities, setQualities] = useState<AudioQualityOption[]>([]);
  const [historyThresholdSeconds, setHistoryThreshold] = useState(DEFAULT_HISTORY_THRESHOLD);
  const [maxUploadBytes, setMaxUploadBytes] = useState(DEFAULT_MAX_UPLOAD_BYTES);
  const [maxImageUploadBytes, setMaxImageUploadBytes] = useState(DEFAULT_MAX_IMAGE_UPLOAD_BYTES);
  const [hlsEnabled, setHlsEnabled] = useState(false);
  const [accessTokenMinutes, setAccessTokenMinutes] = useState(0);
  const [loaded, setLoaded] = useState(false);
  const networkIsSlow = useSlowNetwork();

  useSessionRenewal(user !== null, accessTokenMinutes);

  useEffect(() => {
    if (authLoading) return;

    if (!user) {
      /* eslint-disable react-hooks/set-state-in-effect */
      setSettings(DEFAULTS);
      setQualities([]);
      setHistoryThreshold(DEFAULT_HISTORY_THRESHOLD);
      setMaxUploadBytes(DEFAULT_MAX_UPLOAD_BYTES);
      setMaxImageUploadBytes(DEFAULT_MAX_IMAGE_UPLOAD_BYTES);
      setHlsEnabled(false);
      setAccessTokenMinutes(0);
      setLoaded(true);
      /* eslint-enable react-hooks/set-state-in-effect */
      return;
    }

    setLoaded(false);

    let active = true;

    void (async () => {
      const [config, saved] = await Promise.allSettled([api.config(), api.settings()]);

      if (!active) return;

      if (config.status === "fulfilled") {
        setQualities(config.value.audioQualities);
        setHlsEnabled(config.value.hlsEnabled);
        setAccessTokenMinutes(config.value.accessTokenMinutes);
        if (config.value.historyThresholdSeconds > 0) {
          setHistoryThreshold(config.value.historyThresholdSeconds);
        }
        if (config.value.maxUploadBytes > 0) {
          setMaxUploadBytes(config.value.maxUploadBytes);
        }
        if (config.value.maxImageUploadBytes > 0) {
          setMaxImageUploadBytes(config.value.maxImageUploadBytes);
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
  }, [authLoading, user]);

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
      maxImageUploadBytes,
      hlsEnabled,
      effectiveQuality: settings.dataSaver ? "Low" : settings.quality,
      networkIsSlow,
      loaded,
      update,
    }),
    [
      settings,
      qualities,
      historyThresholdSeconds,
      maxUploadBytes,
      maxImageUploadBytes,
      hlsEnabled,
      networkIsSlow,
      loaded,
      update,
    ],
  );

  return <SettingsContext.Provider value={value}>{children}</SettingsContext.Provider>;
}

export function useSettings(): SettingsState {
  return useRequiredContext(SettingsContext, "useSettings", "SettingsProvider");
}

/**
 * Держит сессию живой, пока вкладка открыта.
 *
 * `send()` из lib/http продлевает токен только в ответ на 401, но за время непрерывного
 * воспроизведения запросов к API может не быть вообще: список отдаётся из кэша, а звук
 * аудиоэлемент тянет сам, мимо обёртки. Без таймера токен истекал ровно посреди трека.
 */
function useSessionRenewal(signedIn: boolean, accessTokenMinutes: number): void {
  useEffect(() => {
    if (!signedIn || accessTokenMinutes <= 0) return;

    const intervalMs = renewalIntervalMs(accessTokenMinutes);
    let lastRenewedAt = Date.now();

    const renew = () => {
      lastRenewedAt = Date.now();
      void refreshSession();
    };

    const timer = window.setInterval(renew, intervalMs);

    // В фоновой вкладке таймеры душатся, поэтому при возврате расписание сверяется по часам.
    const onVisible = () => {
      if (document.visibilityState !== "visible") return;
      if (isStale(lastRenewedAt, Date.now(), intervalMs)) renew();
    };

    document.addEventListener("visibilitychange", onVisible);

    return () => {
      window.clearInterval(timer);
      document.removeEventListener("visibilitychange", onVisible);
    };
  }, [signedIn, accessTokenMinutes]);
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
