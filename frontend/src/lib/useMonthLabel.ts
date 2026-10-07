// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useI18n } from "@/contexts/I18nContext";

// «Сентябрь», а для прошлых лет — «Сентябрь 2025».
export function useMonthLabel() {
  const { locale } = useI18n();

  return (year: number, month: number, capitalized = true) => {
    const date = new Date(year, month - 1, 1);
    const name = date.toLocaleDateString(locale, {
      month: "long",
      year: year === new Date().getFullYear() ? undefined : "numeric",
    });

    return capitalized ? name.charAt(0).toUpperCase() + name.slice(1) : name;
  };
}
