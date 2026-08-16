"use client";

import { useI18n } from "@/contexts/I18nContext";
import { LOCALES, LOCALE_NAMES } from "@/lib/i18n";
import { Button } from "./ui/button";

export function LocaleSwitcher() {
  const { locale, setLocale, t } = useI18n();

  const next = LOCALES[(LOCALES.indexOf(locale) + 1) % LOCALES.length];
  const label = t("action.switchLanguage", { language: LOCALE_NAMES[next] });

  return (
    <Button
      variant="ghost"
      size="icon"
      className="text-2xs font-bold tracking-wider"
      onClick={() => setLocale(next)}
      aria-label={label}
      title={label}
    >
      {locale.toUpperCase()}
    </Button>
  );
}
