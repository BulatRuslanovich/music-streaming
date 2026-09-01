// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import type { Dictionary, TranslationKey } from "./en";
import { isLocale, type Locale, type Phrase, type TranslationValues } from "./types";

export { LOCALES, LOCALE_NAMES, isLocale } from "./types";
export type { Locale, Phrase, PluralPhrase, TranslationValues } from "./types";
export type { Dictionary, TranslationKey } from "./en";

export const DEFAULT_LOCALE: Locale = "en";

/**
 * Активный словарь. Заполняется тем, кто знает локаль, — раньше здесь лежали оба сразу.
 *
 * Оба словаря статическим импортом попадали в главный чанк: около 20 КБ в gzip, из которых
 * половина заведомо не нужна. Импорт здесь только типовой, поэтому в бандл ничего не тянет;
 * содержимое подставляет сервер, который знает локаль из куки ещё до рендера.
 */
const dictionaries = new Map<Locale, Dictionary>();

export function registerDictionary(locale: Locale, dictionary: Dictionary): void {
  dictionaries.set(locale, dictionary);
}

/** Динамический импорт словаря. Вызывается на сервере, в клиентский бандл не попадает. */
export async function loadDictionary(locale: Locale): Promise<Dictionary> {
  if (locale === "ru") return (await import("./ru")).ru;
  return (await import("./en")).en;
}

const PLACEHOLDER = /\{(\w+)\}/g;

const pluralRules = new Map<Locale, Intl.PluralRules>();

function pluralRulesFor(locale: Locale): Intl.PluralRules {
  let rules = pluralRules.get(locale);
  if (!rules) {
    rules = new Intl.PluralRules(locale);
    pluralRules.set(locale, rules);
  }
  return rules;
}

function selectForm(
  phrase: Phrase | undefined,
  locale: Locale,
  count: number | undefined,
): string | undefined {
  if (phrase === undefined) return undefined;
  if (typeof phrase === "string") return phrase;
  return phrase[pluralRulesFor(locale).select(count ?? 0)] ?? phrase.other;
}

export function translate(locale: Locale, key: TranslationKey, values?: TranslationValues): string {
  return translateWith(dictionaries.get(locale), locale, key, values);
}

/**
 * Перевод по явно переданному словарю.
 *
 * Нужен React-пути: там словарь приходит пропом с сервера, и заглядывать за ним в модульный
 * реестр во время рендера — побочный эффект. Реестр остаётся для `tr()`, который вызывается
 * вне React (сообщения об ошибках в http.ts).
 */
export function translateWith(
  dictionary: Dictionary | undefined,
  locale: Locale,
  key: TranslationKey,
  values?: TranslationValues,
): string {
  const phrase = dictionary?.[key];
  const count = typeof values?.count === "number" ? values.count : undefined;

  if (process.env.NODE_ENV !== "production" && phrase === undefined) {
    console.warn(`[i18n] missing "${key}" in "${locale}"`);
  }

  // Запасного словаря больше нет: ru.ts типизирован ключами en.ts и не собирается, пока в нём
  // чего-то не хватает, поэтому промах здесь означает ошибку сборки, а не пропущенный перевод.
  const template = selectForm(phrase, locale, count) ?? key;

  if (!values) return template;

  return template.replace(PLACEHOLDER, (placeholder, name: string) => {
    const value = values[name];
    if (value === undefined) return placeholder;
    return typeof value === "number" ? value.toLocaleString(locale) : value;
  });
}

let activeLocale: Locale = DEFAULT_LOCALE;

export function setActiveLocale(locale: Locale): void {
  activeLocale = locale;
}

/**
 * Кука с локалью. Раньше выбор жил только в localStorage, и сервер о нём не знал: серверный
 * снимок всегда возвращал английский, а на русский страница переключалась уже после гидратации.
 * Кука видна серверу до рендера, поэтому язык приезжает сразу правильным.
 */
export const LOCALE_COOKIE = "ms_locale";

export function localeCookieValue(locale: Locale): string {
  const year = 60 * 60 * 24 * 365;
  return `${LOCALE_COOKIE}=${locale}; path=/; max-age=${year}; samesite=lax`;
}

export function tr(key: TranslationKey, values?: TranslationValues): string {
  return translate(activeLocale, key, values);
}

export function detectLocale(): Locale {
  if (typeof navigator === "undefined") return DEFAULT_LOCALE;

  for (const tag of navigator.languages ?? [navigator.language]) {
    const base = tag.split("-")[0];
    if (isLocale(base)) return base;
  }

  return DEFAULT_LOCALE;
}
