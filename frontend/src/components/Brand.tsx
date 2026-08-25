// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import Image from "next/image";
import { cn } from "@/lib/cn";

export function BrandMark({ className }: { className?: string }) {
  return (
    <Image
      src="/logo.png"
      alt=""
      width={512}
      height={512}
      className={cn("shrink-0", className)}
      priority
    />
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
