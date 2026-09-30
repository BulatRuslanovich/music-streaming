// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/api";
import { queries } from "@/lib/queries";
import type { AudioQuality, UserSettings } from "@/lib/types";
import { useAuth } from "@/contexts/AuthContext";

/** Пока настройки не приехали (и у гостя на странице входа). */
const DEFAULTS: UserSettings = { quality: "Normal", dataSaver: false, timeZone: "UTC" };

const DEFAULT_MAX_UPLOAD_BYTES = 200 * 1024 * 1024;
const DEFAULT_MAX_IMAGE_UPLOAD_BYTES = 8 * 1024 * 1024;

/** Настройки слушателя и лимиты сервера. `update` применяется сразу и откатывается при отказе. */
export function useSettings() {
  const signedIn = useAuth().user !== null;
  const client = useQueryClient();
  const key = queries.settings().queryKey;

  const config = useQuery({ ...queries.config(), enabled: signedIn });
  const saved = useQuery({ ...queries.settings(), enabled: signedIn });

  const { mutate: update } = useMutation({
    mutationFn: (changes: Partial<UserSettings>) => api.updateSettings(changes),
    onMutate: (changes) => {
      const previous = client.getQueryData(key);
      client.setQueryData(key, { ...(previous ?? DEFAULTS), ...changes });
      return previous;
    },
    onError: (_, __, previous) => client.setQueryData(key, previous),
    onSuccess: (settings) => client.setQueryData(key, settings),
  });

  const settings = saved.data ?? DEFAULTS;
  const effectiveQuality: AudioQuality = settings.dataSaver ? "Low" : settings.quality;

  return {
    ...settings,
    effectiveQuality,
    maxUploadBytes: config.data?.maxUploadBytes ?? DEFAULT_MAX_UPLOAD_BYTES,
    maxImageUploadBytes: config.data?.maxImageUploadBytes ?? DEFAULT_MAX_IMAGE_UPLOAD_BYTES,
    update,
  };
}
