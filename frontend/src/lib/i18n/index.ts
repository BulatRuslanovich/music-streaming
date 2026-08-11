import { en, type Dictionary, type TranslationKey } from "./en";
import { ru } from "./ru";
import { isLocale, type Locale, type Phrase, type TranslationValues } from "./types";

export { LOCALES, LOCALE_NAMES, isLocale } from "./types";
export type { Locale, Phrase, PluralPhrase, TranslationValues } from "./types";
export type { Dictionary, TranslationKey } from "./en";

export const DEFAULT_LOCALE: Locale = "en";

const dictionaries: Record<Locale, Dictionary> = { en, ru };

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

export function translate(
  locale: Locale,
  key: TranslationKey,
  values?: TranslationValues,
): string {
  const phrase = dictionaries[locale][key];
  const count = typeof values?.count === "number" ? values.count : undefined;

  if (process.env.NODE_ENV !== "production" && phrase === undefined) {
    console.warn(`[i18n] missing "${key}" in "${locale}"`);
  }

  const template =
    selectForm(phrase, locale, count) ?? selectForm(en[key], DEFAULT_LOCALE, count) ?? key;

  if (!values) return template;

  return template.replace(PLACEHOLDER, (placeholder, name: string) => {
    const value = values[name];
    if (value === undefined) return placeholder;
    return typeof value === "number" ? value.toLocaleString(locale) : value;
  });
}

export function detectLocale(): Locale {
  if (typeof navigator === "undefined") return DEFAULT_LOCALE;

  for (const tag of navigator.languages ?? [navigator.language]) {
    const base = tag.split("-")[0];
    if (isLocale(base)) return base;
  }

  return DEFAULT_LOCALE;
}
