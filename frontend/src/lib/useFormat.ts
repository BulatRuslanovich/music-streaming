"use client";

import { useMemo } from "react";
import { useI18n } from "@/contexts/I18nContext";
import type { TranslationKey } from "@/lib/i18n";

const BYTE_UNITS: TranslationKey[] = [
  "unit.byte",
  "unit.kilobyte",
  "unit.megabyte",
  "unit.gigabyte",
  "unit.terabyte",
];

export interface Formatters {
  totalDuration: (totalSeconds: number) => string;
  bytes: (bytes: number) => string;
  relativeDate: (isoDate: string) => string;
  timeOfDay: (isoDate: string) => string;

  /** Календарная дата без времени: приходит из статистики уже в местном поясе пользователя. */
  shortDate: (isoDate: string) => string;
}

export function useFormat(): Formatters {
  const { locale, t } = useI18n();

  return useMemo<Formatters>(
    () => ({
      totalDuration(totalSeconds) {
        if (totalSeconds < 60) return t("unit.seconds", { count: Math.round(totalSeconds) });

        const hours = Math.floor(totalSeconds / 3600);
        const minutes = Math.round((totalSeconds % 3600) / 60);

        if (hours === 0) return t("unit.minutes", { count: minutes });
        if (minutes === 0) return t("unit.hours", { count: hours });
        return t("unit.hoursMinutes", { hours, minutes });
      },

      bytes(value) {
        if (value <= 0) return `0 ${t("unit.byte")}`;

        const exponent = Math.min(
          Math.floor(Math.log(value) / Math.log(1024)),
          BYTE_UNITS.length - 1,
        );
        const scaled = value / 1024 ** exponent;
        const digits = scaled >= 10 || exponent === 0 ? 0 : 1;

        return `${scaled.toLocaleString(locale, {
          minimumFractionDigits: digits,
          maximumFractionDigits: digits,
        })} ${t(BYTE_UNITS[exponent])}`;
      },

      relativeDate(isoDate) {
        const date = new Date(isoDate);
        if (Number.isNaN(date.getTime())) return "";

        const startOfToday = new Date();
        startOfToday.setHours(0, 0, 0, 0);

        const startOfDate = new Date(date);
        startOfDate.setHours(0, 0, 0, 0);

        const dayDifference = Math.round(
          (startOfToday.getTime() - startOfDate.getTime()) / 86_400_000,
        );

        if (dayDifference <= 0) return t("date.today");
        if (dayDifference === 1) return t("date.yesterday");
        if (dayDifference < 7) return date.toLocaleDateString(locale, { weekday: "long" });

        return date.toLocaleDateString(locale, {
          day: "numeric",
          month: "short",
          year: date.getFullYear() === new Date().getFullYear() ? undefined : "numeric",
        });
      },

      timeOfDay(isoDate) {
        const date = new Date(isoDate);
        if (Number.isNaN(date.getTime())) return "";

        return date.toLocaleTimeString(locale, { hour: "2-digit", minute: "2-digit" });
      },

      shortDate(isoDate) {
        // Дата приходит как «2026-05-12» и уже посчитана в поясе пользователя, поэтому её нельзя
        // прогонять через Date: браузер прочтёт её как полночь UTC и в минусовых поясах сдвинет
        // на сутки назад.
        const [year, month, day] = isoDate.split("-").map(Number);
        if (!year || !month || !day) return isoDate;

        return new Date(year, month - 1, day).toLocaleDateString(locale, {
          day: "numeric",
          month: "short",
          year: year === new Date().getFullYear() ? undefined : "numeric",
        });
      },
    }),
    [locale, t],
  );
}
