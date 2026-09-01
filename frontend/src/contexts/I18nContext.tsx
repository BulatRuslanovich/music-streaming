// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { createContext, useCallback, useEffect, useMemo, useState } from "react";

import {
  DEFAULT_LOCALE,
  detectLocale,
  isLocale,
  loadDictionary,
  localeCookieValue,
  registerDictionary,
  setActiveLocale,
  translateWith,
  type Dictionary,
  type Locale,
  type TranslationKey,
  type TranslationValues,
} from "@/lib/i18n";
import { useRequiredContext } from "@/lib/useRequiredContext";

export type Translate = (key: TranslationKey, values?: TranslationValues) => string;

interface I18nState {
  locale: Locale;
  setLocale: (locale: Locale) => void;
  t: Translate;
}

const I18nContext = createContext<I18nState | null>(null);

const STORAGE_KEY = "music-streaming.locale";

function readLocale(): Locale {
  try {
    const saved = window.localStorage.getItem(STORAGE_KEY);
    if (saved && isLocale(saved)) return saved;
  } catch {}
  return detectLocale();
}

function persistLocale(next: Locale): void {
  try {
    window.localStorage.setItem(STORAGE_KEY, next);
  } catch {}
  // Кука — чтобы следующий заход отрендерился на сервере уже на этом языке.
  document.cookie = localeCookieValue(next);
}

export function I18nProvider({
  children,
  initialLocale = DEFAULT_LOCALE,
  initialDictionary,
}: {
  children: React.ReactNode;
  /** Локаль, выбранная сервером по куке, — чтобы первый рендер был уже на нужном языке. */
  initialLocale?: Locale;
  /** Словарь этой локали. Приезжает с сервера, а не из клиентского бандла. */
  initialDictionary?: Dictionary;
}) {
  const [active, setActive] = useState<{ locale: Locale; dictionary?: Dictionary }>(() => ({
    locale: initialLocale,
    dictionary: initialDictionary,
  }));

  // Реестр нужен `tr()` — он зовётся вне React, из обработки ошибок в http.ts. Заполняем его
  // эффектом, а не во время рендера: рендер обязан быть чистым.
  useEffect(() => {
    if (active.dictionary) registerDictionary(active.locale, active.dictionary);
    setActiveLocale(active.locale);
  }, [active]);

  useEffect(() => {
    document.documentElement.lang = active.locale;
  }, [active.locale]);

  // Сервер выбирает язык по куке. Её может не быть — первый заход после появления этой куки
  // или свежий браузер; тогда берём сохранённый или системный выбор, догружаем словарь и
  // ставим куку, чтобы следующий заход отрендерился на сервере уже правильно.
  useEffect(() => {
    const preferred = readLocale();
    if (preferred === active.locale) return;

    let cancelled = false;
    void loadDictionary(preferred).then((dictionary) => {
      if (cancelled) return;
      persistLocale(preferred);
      setActive({ locale: preferred, dictionary });
    });

    return () => {
      cancelled = true;
    };
  }, [active.locale]);

  const setLocale = useCallback((next: Locale) => {
    void loadDictionary(next).then((dictionary) => {
      persistLocale(next);
      setActive({ locale: next, dictionary });
    });
  }, []);

  const locale = active.locale;

  const t = useCallback<Translate>(
    (key, values) => translateWith(active.dictionary, active.locale, key, values),
    [active],
  );

  const value = useMemo<I18nState>(() => ({ locale, setLocale, t }), [locale, setLocale, t]);

  return <I18nContext.Provider value={value}>{children}</I18nContext.Provider>;
}

export function useI18n(): I18nState {
  return useRequiredContext(I18nContext, "useI18n", "I18nProvider");
}

export function useT(): Translate {
  return useI18n().t;
}
