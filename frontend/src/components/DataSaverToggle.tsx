// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { cn } from "@/lib/cn";
import { useSettings } from "@/contexts/SettingsContext";
import { useT } from "@/contexts/I18nContext";
import { Button } from "./ui/button";
import { DataSaverIcon } from "./Icons";

/**
 * Экономия трафика на весь сеанс. Ничего не показывает, когда ступень всего одна: выбирать
 * тогда не из чего.
 */
export function DataSaverToggle({
  size = "icon",
  className,
  withTitle = false,
}: {
  size?: "icon" | "icon-lg";
  className?: string;
  withTitle?: boolean;
}) {
  const settings = useSettings();
  const t = useT();

  if (settings.qualities.length <= 1) return null;

  return (
    <Button
      variant="ghost"
      size={size}
      // Акцент включённого состояния идёт последним: tailwind-merge оставляет то, что позже,
      // а вызывающий может гасить кнопку в покое (`text-faint` в футере).
      className={cn(className, settings.dataSaver && "text-primary")}
      onClick={() => settings.update({ dataSaver: !settings.dataSaver })}
      aria-label={t("player.dataSaver")}
      aria-pressed={settings.dataSaver}
      title={
        withTitle
          ? settings.dataSaver
            ? t("player.dataSaverOn")
            : t("player.dataSaverOff")
          : undefined
      }
    >
      <DataSaverIcon size={20} />
    </Button>
  );
}
