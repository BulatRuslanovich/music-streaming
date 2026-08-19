"use client";

import { shortcutHelp } from "@/lib/shortcuts";
import { useSearchShortcutLabel } from "@/lib/useSearchShortcut";
import { useT } from "@/contexts/I18nContext";
import { Dialog, DialogContent } from "./ui/dialog";
import { Overline } from "./ui/label";

export function ShortcutsDialog({ onClose }: { onClose: () => void }) {
  const t = useT();
  const commandKey = useSearchShortcutLabel();

  return (
    <Dialog open onOpenChange={(open) => !open && onClose()}>
      <DialogContent title={t("shortcuts.title")} description={t("shortcuts.hint")}>
        <div className="flex flex-col gap-6">
          {shortcutHelp(commandKey).map((group) => (
            <section key={group.titleKey} className="flex flex-col gap-2">
              <Overline>{t(group.titleKey)}</Overline>

              <dl className="flex flex-col gap-1.5">
                {group.items.map((item) => (
                  <div key={item.labelKey} className="flex items-baseline justify-between gap-4">
                    <dt className="text-sm text-muted-foreground">{t(item.labelKey)}</dt>
                    <dd className="flex shrink-0 gap-1">
                      {item.keys.map((key) => (
                        <kbd
                          key={key}
                          className="rounded-sm border border-border-strong bg-raised px-1.5 py-0.5 text-xs whitespace-nowrap"
                        >
                          {key}
                        </kbd>
                      ))}
                    </dd>
                  </div>
                ))}
              </dl>
            </section>
          ))}
        </div>
      </DialogContent>
    </Dialog>
  );
}
