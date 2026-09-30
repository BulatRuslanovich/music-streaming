// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useRef, useState, type DragEvent, type ReactNode } from "react";
import { ACCEPT_ATTRIBUTE } from "@/lib/playback/audioFormats";

/**
 * Принимает файлы, брошенные в любое место страницы, а не в отдельную рамку: рамку приходилось
 * искать глазами, а страница целиком — цель, в которую не промахнуться.
 *
 * Счётчик вместо флага: `dragleave` приходит при переходе на каждый дочерний элемент, и флаг
 * мигал бы под курсором. Чужое перетаскивание (текст, ссылка) страницу не подсвечивает. Пока
 * идёт загрузка, брошенный файл гасится, а не открывается браузером поверх страницы.
 */
export function FileDrop({
  onFiles,
  disabled = false,
  className,
  children,
}: {
  onFiles: (files: FileList | null) => void;
  disabled?: boolean;
  className?: string;
  children: (drop: { dragging: boolean; choose: () => void }) => ReactNode;
}) {
  const [input, setInput] = useState<HTMLInputElement | null>(null);
  const depth = useRef(0);
  const [dragging, setDragging] = useState(false);

  const carriesFiles = (event: DragEvent) => event.dataTransfer.types.includes("Files");

  return (
    <div
      className={className}
      onDragEnter={(event) => {
        if (disabled || !carriesFiles(event)) return;
        depth.current += 1;
        setDragging(true);
      }}
      onDragOver={(event) => {
        if (!carriesFiles(event)) return;
        event.preventDefault();
      }}
      onDragLeave={(event) => {
        if (!carriesFiles(event)) return;
        depth.current = Math.max(0, depth.current - 1);
        if (depth.current === 0) setDragging(false);
      }}
      onDrop={(event) => {
        if (!carriesFiles(event)) return;
        event.preventDefault();
        depth.current = 0;
        setDragging(false);
        if (!disabled) onFiles(event.dataTransfer.files);
      }}
    >
      {children({ dragging, choose: () => input?.click() })}

      <input
        ref={setInput}
        type="file"
        accept={ACCEPT_ATTRIBUTE}
        multiple
        hidden
        onChange={(event) => {
          onFiles(event.target.files);
          event.target.value = "";
        }}
      />
    </div>
  );
}
