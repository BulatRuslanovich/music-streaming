// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { createContext, useCallback, useEffect, useMemo, useSyncExternalStore } from "react";

import {
  DEFAULT_LOCALE,
  detectLocale,
  isLocale,
  setActiveLocale,
  translate,
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

let currentLocale: Locale | null = null;
const listeners = new Set<() => void>();

function readLocale(): Locale {
  try {
    const saved = window.localStorage.getItem(STORAGE_KEY);
    if (saved && isLocale(saved)) return saved;
  } catch {}
  return detectLocale();
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

function getSnapshot(): Locale {
  if (currentLocale === null) {
    currentLocale = readLocale();
    setActiveLocale(currentLocale);
  }
  return currentLocale;
}

function getServerSnapshot(): Locale {
  return DEFAULT_LOCALE;
}

function storeLocale(next: Locale): void {
  currentLocale = next;
  setActiveLocale(next);
  try {
    window.localStorage.setItem(STORAGE_KEY, next);
  } catch {}
  listeners.forEach((listener) => listener());
}

export function I18nProvider({ children }: { children: React.ReactNode }) {
  const locale = useSyncExternalStore(subscribe, getSnapshot, getServerSnapshot);

  useEffect(() => {
    document.documentElement.lang = locale;
  }, [locale]);

  const setLocale = useCallback((next: Locale) => storeLocale(next), []);

  const t = useCallback<Translate>((key, values) => translate(locale, key, values), [locale]);

  const value = useMemo<I18nState>(() => ({ locale, setLocale, t }), [locale, setLocale, t]);

  return <I18nContext.Provider value={value}>{children}</I18nContext.Provider>;
}

export function useI18n(): I18nState {
  return useRequiredContext(I18nContext, "useI18n", "I18nProvider");
}

export function useT(): Translate {
  return useI18n().t;
}
