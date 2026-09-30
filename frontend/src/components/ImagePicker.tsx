// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useEffect, useMemo, useRef, type ReactNode } from "react";
import { cn } from "@/lib/cn";
import { useFormat } from "@/lib/useFormat";
import { useSettings } from "@/contexts/SettingsContext";
import { useT } from "@/contexts/I18nContext";
import { useToast } from "@/contexts/ToastContext";
import { Button } from "./ui/button";
import { ImageIcon, Trash2Icon } from "lucide-react";

const ACCEPTED_TYPES = "image/jpeg,image/png,image/webp";

export interface ImageChoice {
  file: File | null;
  removed: boolean;
}

export const noImageChosen: ImageChoice = { file: null, removed: false };

export function ImagePicker({
  value,
  onChange,
  currentUrl,
  fallback,
  disabled,
  kind,
  name,
  note,
}: {
  value: ImageChoice;
  onChange: (choice: ImageChoice) => void;
  currentUrl: string | null;
  fallback: ReactNode;
  disabled?: boolean;
  /** Фото артиста круглое, обложки — квадратные. */
  kind: "photo" | "cover";
  /** Чьё изображение — для alt. */
  name: string;
  /** Что станет с картинкой после загрузки; идёт после строки про форматы и лимит. */
  note: string;
}) {
  const t = useT();
  const format = useFormat();
  const { maxImageUploadBytes } = useSettings();
  const { notify } = useToast();
  const input = useRef<HTMLInputElement | null>(null);

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
  const photo = kind === "photo";

  return (
    <div className="flex items-start gap-5 border-b border-border pb-4 max-md:flex-col max-md:items-center">
      <div
        className={cn(
          "grid size-24 shrink-0 place-items-center overflow-hidden bg-raised text-lg font-semibold text-faint",
          photo ? "rounded-full" : "rounded-md",
        )}
      >
        {shown ? (
          <img
            src={shown}
            alt={t(photo ? "image.photoAlt" : "image.coverAlt", { name })}
            className="size-full object-cover"
          />
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

            if (chosen.size > maxImageUploadBytes) {
              notify(
                t("dialog.imageTooLarge", { limit: format.bytes(maxImageUploadBytes) }),
                "error",
              );
              return;
            }

            onChange({ file: chosen, removed: false });
          }}
        />

        <Button onClick={() => input.current?.click()} disabled={disabled}>
          <ImageIcon size={16} />
          {photo
            ? t(shown ? "image.replacePhoto" : "image.choosePhoto")
            : t(shown ? "image.replaceCover" : "image.chooseCover")}
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
            <Trash2Icon size={16} />
            {t(photo ? "image.removePhoto" : "image.removeCover")}
          </Button>
        )}

        <p className="text-sm text-muted-foreground">
          {t("image.formats", { limit: format.bytes(maxImageUploadBytes) })} {note}
        </p>
      </div>
    </div>
  );
}
