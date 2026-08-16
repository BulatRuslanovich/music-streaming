"use client";

import React, { createContext, useCallback, useContext, useEffect, useMemo, useState } from "react";
import { api } from "@/lib/api";
import type { AudioQuality, AudioQualityOption, UserSettings } from "@/lib/types";

interface SettingsState extends UserSettings {
  /** Ступени, которые эта установка действительно умеет отдавать. */
  qualities: AudioQualityOption[];

  /** Сколько секунд должен проиграться трек, чтобы попасть в историю. */
  historyThresholdSeconds: number;

  /** Потолок на один загружаемый файл. До ответа сервера — значение по умолчанию с бэкенда. */
  maxUploadBytes: number;

  /** Что просить у сервера прямо сейчас: экономия трафика перебивает выбранную ступень. */
  effectiveQuality: AudioQuality;

  /** Стоит ли предложить понизить качество: соединение медленное или система просит экономить. */
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

/** Столько же стоит в `StorageOptions.MaxUploadBytes`: до ответа сервера врать нельзя ни в одну сторону. */
const DEFAULT_MAX_UPLOAD_BYTES = 200 * 1024 * 1024;

/**
 * Настройки живут на сервере, а не в localStorage: часовой пояс нужен агрегациям статистики, а
 * качество и автоплей должны переезжать с пользователем на другое устройство. Здесь они
 * применяются сразу, а запрос уходит следом — переключатель не должен ждать сети.
 */
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

      // Часовой пояс определяет браузер, и сообщается он серверу здесь же, одной загрузкой:
      // спрашивать его у человека незачем, а без него статистика считала бы чужие сутки.
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

/**
 * Медленное соединение — это повод предложить понизить качество, но не повод сделать это молча:
 * подменять выбор человека тем, что подумал браузер, значит отнимать у него управление.
 */
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
