// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useMemo } from "react";
import { useI18n } from "@/contexts/I18nContext";
import { calendarDaysAgo } from "@/lib/format";
import type { TranslationKey } from "@/lib/i18n";

const BYTE_UNITS: TranslationKey[] = [
  "unit.byte",
  "unit.kilobyte",
  "unit.megabyte",
  "unit.gigabyte",
  "unit.terabyte",
];

interface Formatters {
  totalDuration: (totalSeconds: number) => string;
  bytes: (bytes: number) => string;
  relativeDate: (isoDate: string) => string;
  /** Когда трек прослушан: время сегодня, «вчера, 14:32», день недели со временем, дальше дата. */
  playedAt: (isoDate: string) => string;
  timeOfDay: (isoDate: string) => string;

  shortDate: (isoDate: string) => string;
}

export function useFormat(): Formatters {
  const { locale, t } = useI18n();

  return useMemo<Formatters>(() => {
    // Дата без времени: год только когда он не текущий.
    const calendarDate = (date: Date) =>
      date.toLocaleDateString(locale, {
        day: "numeric",
        month: "short",
        year: date.getFullYear() === new Date().getFullYear() ? undefined : "numeric",
      });

    return {
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

        const days = calendarDaysAgo(date);

        if (days <= 0) return t("date.today");
        if (days === 1) return t("date.yesterday");
        if (days < 7) return date.toLocaleDateString(locale, { weekday: "long" });

        return calendarDate(date);
      },

      playedAt(isoDate) {
        const date = new Date(isoDate);
        if (Number.isNaN(date.getTime())) return "";

        const days = calendarDaysAgo(date);
        const time = date.toLocaleTimeString(locale, { hour: "2-digit", minute: "2-digit" });

        // В истории почти всё — «сегодня», и одно это слово в каждой строке ничего не говорило.
        // Время отличает строки друг от друга; дальше недели оно уже не нужно, хватает даты.
        if (days <= 0) return time;
        if (days === 1) return `${t("date.yesterday")}, ${time}`;
        if (days < 7) return `${date.toLocaleDateString(locale, { weekday: "long" })}, ${time}`;

        return calendarDate(date);
      },

      timeOfDay(isoDate) {
        const date = new Date(isoDate);
        if (Number.isNaN(date.getTime())) return "";

        return date.toLocaleTimeString(locale, { hour: "2-digit", minute: "2-digit" });
      },

      shortDate(isoDate) {
        const [year, month, day] = isoDate.split("-").map(Number);
        if (!year || !month || !day) return isoDate;

        return new Date(year, month - 1, day).toLocaleDateString(locale, {
          day: "numeric",
          month: "short",
          year: year === new Date().getFullYear() ? undefined : "numeric",
        });
      },
    };
  }, [locale, t]);
}
