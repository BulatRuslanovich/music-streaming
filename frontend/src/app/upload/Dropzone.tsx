// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useRef, useState } from "react";
import { ACCEPT_ATTRIBUTE } from "@/lib/audioFormats";
import { cn } from "@/lib/cn";
import { UploadIcon } from "@/components/Icons";
import { Button } from "@/components/ui/button";
import { useT } from "@/contexts/I18nContext";

export function Dropzone({
  onFiles,
  disabled = false,
}: {
  onFiles: (files: FileList | null) => void;
  disabled?: boolean;
}) {
  const t = useT();
  const inputRef = useRef<HTMLInputElement | null>(null);
  const [dragging, setDragging] = useState(false);

  return (
    <section
      onDragOver={(event) => {
        if (disabled) return;
        event.preventDefault();
        setDragging(true);
      }}
      onDragLeave={() => setDragging(false)}
      onDrop={(event) => {
        if (disabled) return;
        event.preventDefault();
        setDragging(false);
        onFiles(event.dataTransfer.files);
      }}
      className={cn(
        "flex flex-col items-center gap-4 rounded-2xl border border-dashed p-8 text-center",
        "transition-colors duration-150 ease-brand max-md:gap-3 max-md:p-6",
        dragging
          ? "border-primary bg-primary-surface"
          : "border-border-strong bg-[linear-gradient(125deg,color-mix(in_oklab,var(--primary)_10%,var(--card)),var(--card)_72%)]",
        disabled && "pointer-events-none opacity-55",
      )}
    >
      <span
        className={cn(
          "grid size-14 place-items-center rounded-full transition-colors duration-150 ease-brand",
          dragging ? "bg-primary text-primary-foreground" : "bg-primary-soft text-primary",
        )}
      >
        <UploadIcon size={26} />
      </span>

      <span className="text-lg font-bold">{t("upload.dropHint")}</span>

      <Button variant="primary" onClick={() => inputRef.current?.click()}>
        {t("upload.chooseFiles")}
      </Button>

      <input
        ref={inputRef}
        type="file"
        accept={ACCEPT_ATTRIBUTE}
        multiple
        hidden
        onChange={(event) => {
          onFiles(event.target.files);
          event.target.value = "";
        }}
      />
    </section>
  );
}
