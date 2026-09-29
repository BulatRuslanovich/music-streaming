// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { Fragment, useEffect, useState } from "react";
import { isHelpShortcut, isTypingTarget, SHORTCUT_HELP } from "@/lib/shortcuts";
import { useT } from "@/contexts/I18nContext";
import { Dialog, DialogContent } from "./ui/dialog";

/**
 * Справка по горячим клавишам, по `?` из любого места. Клавиши плеера были всегда, но узнать
 * о них из интерфейса было нельзя. Смонтирована в каркасе, а не в плеере: без трека плеер не
 * рендерится, а справка нужна и до первого трека.
 */
export function ShortcutsHelp() {
  const t = useT();
  const [open, setOpen] = useState(false);

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (!isHelpShortcut(event) || isTypingTarget(event.target)) return;

      event.preventDefault();
      setOpen((current) => !current);
    };

    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, []);

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogContent title={t("shortcuts.title")}>
        <dl className="grid grid-cols-[minmax(0,1fr)_auto] items-center gap-x-6 gap-y-2.5 text-sm">
          {SHORTCUT_HELP.map((entry) => (
            <Fragment key={entry.keys.join()}>
              <dt className="text-muted-foreground">{t(entry.label, entry.values)}</dt>
              <dd className="flex justify-end gap-1.5">
                {entry.keys.map((key) => (
                  <kbd
                    key={key}
                    className="min-w-7 rounded-sm border border-border-strong px-1.5 py-0.5 text-center font-sans text-xs text-foreground"
                  >
                    {key === "Space" ? t("shortcuts.space") : key}
                  </kbd>
                ))}
              </dd>
            </Fragment>
          ))}
        </dl>
      </DialogContent>
    </Dialog>
  );
}
