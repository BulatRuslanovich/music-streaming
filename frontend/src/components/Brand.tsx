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
  return <span className={cn("font-extrabold tracking-[-0.055em]", className)}>Caimack</span>;
}
