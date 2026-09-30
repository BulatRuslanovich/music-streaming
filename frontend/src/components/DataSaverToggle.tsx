// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { cn } from "@/lib/cn";
import { useSettings } from "@/lib/useSettings";
import { useT } from "@/contexts/I18nContext";
import { Button } from "./ui/button";
import { GaugeIcon } from "lucide-react";

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

  return (
    <Button
      variant="ghost"
      size={size}
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
      <GaugeIcon size={20} />
    </Button>
  );
}
