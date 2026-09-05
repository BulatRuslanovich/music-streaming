// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useState } from "react";
import Link from "next/link";
import { useQuery } from "@tanstack/react-query";
import { CloseIcon, SparkleIcon } from "@/components/Icons";
import { Button } from "@/components/ui/button";
import { Overline } from "@/components/ui/label";
import { useI18n, useT } from "@/contexts/I18nContext";
import { queries } from "@/lib/queries";
import { monthLabel } from "@/lib/recap";
import { useRecapWindow } from "@/lib/useRecapWindow";

const DISMISSED_KEY = "caimack.recapDismissed";

/**
 * Плашка первых семи дней месяца.
 *
 * Цифр здесь нет намеренно: итоги — событие, и раскрывать их на главной значит потратить
 * весь эффект до перехода. Плашка только сообщает, что подарок готов.
 */
export function RecapBanner() {
  const t = useT();
  const { locale } = useI18n();
  const period = useRecapWindow();
  const open = period?.open === true;

  const [dismissed, setDismissed] = useState(() => readDismissed());

  // Запрос нужен не ради цифр, а ради проверки: обещать итоги за месяц, в котором слушатель
  // ничего не включал, нельзя. Ключ общий со страницей, поэтому переход открывается мгновенно.
  const recap = useQuery({ ...queries.monthlyRecap(), enabled: open });

  if (!open || !period || dismissed === period.month) return null;
  if (!recap.data || recap.data.listenedSeconds === 0) return null;

  const dismiss = () => {
    setDismissed(period.month);
    try {
      localStorage.setItem(DISMISSED_KEY, period.month);
    } catch {
      // Приватное окно или запрет на хранилище — плашка просто вернётся в следующий заход.
    }
  };

  return (
    <div className="flex items-center gap-3 rounded-xl bg-primary-surface px-4 py-2.5">
      <SparkleIcon size={16} className="shrink-0 text-primary" />

      <Link href="/recap" className="flex min-w-0 flex-1 items-baseline gap-2 hover:underline">
        <Overline className="shrink-0 text-primary">{t("recap.title")}</Overline>
        <span className="truncate text-sm font-medium">
          {t("recap.bannerBody", { month: monthLabel(period.month, locale) })}
        </span>
      </Link>

      <Button variant="ghost" size="icon" aria-label={t("recap.dismiss")} onClick={dismiss}>
        <CloseIcon size={15} />
      </Button>
    </div>
  );
}

/** Запоминается месяц, а не флаг: следующие итоги должны показаться сами. */
function readDismissed(): string | null {
  try {
    return localStorage.getItem(DISMISSED_KEY);
  } catch {
    return null;
  }
}
