// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import * as DialogPrimitive from "@radix-ui/react-dialog";
import type { ComponentProps } from "react";
import { cn } from "@/lib/cn";

export const Sheet = DialogPrimitive.Root;
export const SheetTrigger = DialogPrimitive.Trigger;
export const SheetClose = DialogPrimitive.Close;
export const SheetTitle = DialogPrimitive.Title;

const sides = {
  bottom:
    "inset-x-0 bottom-0 max-h-[82dvh] rounded-t-2xl border-t data-[state=open]:slide-in-from-bottom data-[state=closed]:slide-out-to-bottom",
  right:
    "inset-y-0 right-0 w-[min(22rem,100%)] border-l data-[state=open]:slide-in-from-right data-[state=closed]:slide-out-to-right",
} as const;

export function SheetContent({
  className,
  children,
  side = "bottom",
  ...props
}: ComponentProps<typeof DialogPrimitive.Content> & { side?: keyof typeof sides }) {
  return (
    <DialogPrimitive.Portal>
      <DialogPrimitive.Overlay
        className={cn(
          "fixed inset-0 z-80 bg-black/55 backdrop-blur-sm",
          "data-[state=open]:animate-in data-[state=open]:fade-in-0",
          "data-[state=closed]:animate-out data-[state=closed]:fade-out-0",
        )}
      />
      <DialogPrimitive.Content
        aria-describedby={undefined}
        className={cn(
          "fixed z-80 flex flex-col overflow-y-auto border-border-strong bg-popover/95 shadow-pop backdrop-blur-xl",
          "duration-200 data-[state=open]:animate-in data-[state=closed]:animate-out",
          sides[side],
          side === "bottom" && "px-3 pt-2 pb-[calc(1.25rem+env(safe-area-inset-bottom))]",
          className,
        )}
        {...props}
      >
        {side === "bottom" && (
          <div className="mx-auto mt-1.5 mb-2.5 h-1 w-9 shrink-0 rounded-full bg-border-strong" />
        )}
        {children}
      </DialogPrimitive.Content>
    </DialogPrimitive.Portal>
  );
}
