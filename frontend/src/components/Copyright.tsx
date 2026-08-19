// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { cn } from "@/lib/cn";

const YEAR = 2026;

const HOLDER = "Bulat Ruslanovich";

export function Copyright({ className }: { className?: string }) {
  return (
    <p className={cn("truncate text-2xs text-faint", className)}>
      © {YEAR} Caimack · {HOLDER}
    </p>
  );
}
