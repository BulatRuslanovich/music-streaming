"use client";

import { useEffect, useMemo, useRef, type ReactNode } from "react";
import { cn } from "@/lib/cn";
import { accentFor } from "@/lib/format";
import { useT } from "@/contexts/I18nContext";
import { useToast } from "@/contexts/ToastContext";
import { Button } from "./ui/button";
import { ImageIcon, TrashIcon } from "./Icons";

const ACCEPTED_TYPES = "image/jpeg,image/png,image/webp";
const MAX_IMAGE_BYTES = 8 * 1024 * 1024;

export interface ImageChoice {
  /** Выбранный файл, если его выбрали. */
  file: File | null;
  /** Просили ли снять уже стоящую картинку. */
  removed: boolean;
}

export const noImageChosen: ImageChoice = { file: null, removed: false };

/**
 * Выбор обложки. Прежде эти шестьдесят строк были скопированы в диалог исполнителя и в диалог
 * плейлиста, отличаясь только формой превью и подписями кнопок.
 */
export function ImagePicker({
  value,
  onChange,
  currentUrl,
  name,
  fallback,
  disabled,
  round = false,
  labels,
}: {
  value: ImageChoice;
  onChange: (choice: ImageChoice) => void;
  /** Что стоит сейчас; null — не стоит ничего. */
  currentUrl: string | null;
  name: string;
  fallback: ReactNode;
  disabled?: boolean;
  round?: boolean;
  labels: { choose: string; replace: string; remove: string; hint: string; alt: string };
}) {
  const t = useT();
  const { notify } = useToast();
  const input = useRef<HTMLInputElement | null>(null);

  // Ссылка на превью живёт ровно столько, сколько выбранный файл: иначе утекает память.
  const preview = useMemo(
    () => (value.file ? URL.createObjectURL(value.file) : null),
    [value.file],
  );

  useEffect(() => {
    if (!preview) return;
    return () => URL.revokeObjectURL(preview);
  }, [preview]);

  const shown = preview ?? (value.removed ? null : currentUrl);
  const hasSomethingToRemove = (currentUrl !== null || value.file !== null) && !value.removed;

  return (
    <div className="flex items-start gap-5 border-b border-border pb-4 max-md:flex-col max-md:items-center">
      <div
        style={shown ? undefined : { background: accentFor(name || "?") }}
        className={cn(
          "grid size-24 shrink-0 place-items-center overflow-hidden text-lg font-semibold text-primary-foreground",
          round ? "rounded-full" : "rounded-lg",
        )}
      >
        {shown ? (
          // eslint-disable-next-line @next/next/no-img-element -- локальный blob или уже закэшированная обложка
          <img src={shown} alt={labels.alt} className="size-full object-cover" />
        ) : (
          fallback
        )}
      </div>

      <div className="flex min-w-0 flex-col items-start gap-2">
        <input
          ref={input}
          type="file"
          accept={ACCEPTED_TYPES}
          hidden
          onChange={(event) => {
            const chosen = event.target.files?.[0] ?? null;
            if (!chosen) return;

            if (chosen.size > MAX_IMAGE_BYTES) {
              notify(t("dialog.imageTooLarge"), "error");
              return;
            }

            onChange({ file: chosen, removed: false });
          }}
        />

        <Button onClick={() => input.current?.click()} disabled={disabled}>
          <ImageIcon size={16} />
          {shown ? labels.replace : labels.choose}
        </Button>

        {hasSomethingToRemove && (
          <Button
            variant="text"
            size="auto"
            className="text-destructive hover:text-destructive"
            disabled={disabled}
            onClick={() => {
              if (input.current) input.current.value = "";
              onChange({ file: null, removed: currentUrl !== null });
            }}
          >
            <TrashIcon size={16} />
            {labels.remove}
          </Button>
        )}

        <p className="text-sm text-muted-foreground">{labels.hint}</p>
      </div>
    </div>
  );
}
