// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { cn } from "@/lib/cn";

/**
 * Знак объединяет Caimack и звук в одном силуэте: волна начинается внутри открытого C и выходит
 * через его апертуру. Семь штрихов дают первому эскизу нужный ритм, но ещё не слипаются в малых
 * размерах. Инлайн-SVG подхватывает цвет темы, а центр волны остаётся фирменным янтарным акцентом.
 */
export function BrandMark({ className }: { className?: string }) {
  return (
    <svg
      viewBox="0 0 32 32"
      fill="none"
      strokeLinecap="round"
      aria-hidden="true"
      className={cn("shrink-0", className)}
    >
      <path
        d="M23.955 23.955A11.25 11.25 0 1 1 23.955 8.045"
        stroke="currentColor"
        strokeWidth="4.25"
      />
      <path d="M8.8 14V18M12 12V20" stroke="currentColor" strokeWidth="2.1" />
      <path d="M15.2 10V22M18.4 8.5V23.5M21.6 11.5V20.5" stroke="var(--brand)" strokeWidth="2.1" />
      <path d="M24.8 12.5V19.5M28 14V18" stroke="currentColor" strokeWidth="2.1" />
    </svg>
  );
}

export function BrandWordmark({ className }: { className?: string }) {
  return (
    <span
      className={cn(
        // Разрядка добавляет пустоту справа от последней буквы; без компенсации
        // вордмарк визуально прижат влево от своей коробки и не центрируется.
        "font-bold tracking-[0.2em] uppercase [margin-inline-end:-0.2em]",
        className,
      )}
    >
      Caimack
    </span>
  );
}
