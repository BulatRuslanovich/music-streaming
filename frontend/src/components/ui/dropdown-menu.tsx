// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import * as DropdownMenuPrimitive from "@radix-ui/react-dropdown-menu";
import type { ComponentProps } from "react";
import { cn } from "@/lib/cn";

export const DropdownMenu = DropdownMenuPrimitive.Root;
export const DropdownMenuTrigger = DropdownMenuPrimitive.Trigger;

export function DropdownMenuContent({
  className,
  sideOffset = 6,
  align = "end",
  ...props
}: ComponentProps<typeof DropdownMenuPrimitive.Content>) {
  return (
    <DropdownMenuPrimitive.Portal>
      <DropdownMenuPrimitive.Content
        sideOffset={sideOffset}
        align={align}
        className={cn(
          "z-95 max-h-80 min-w-[13rem] overflow-y-auto rounded-xl bg-popover p-1.5 text-popover-foreground shadow-pop",
          "origin-(--radix-dropdown-menu-content-transform-origin)",
          "data-[state=open]:animate-in data-[state=open]:fade-in-0 data-[state=open]:zoom-in-95",
          "data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=closed]:zoom-out-95",
          className,
        )}
        {...props}
      />
    </DropdownMenuPrimitive.Portal>
  );
}

export function DropdownMenuItem({
  className,
  variant = "default",
  onAction,
  ...props
}: ComponentProps<typeof DropdownMenuPrimitive.Item> & {
  variant?: "default" | "destructive";
  onAction?: () => void;
}) {
  return (
    <DropdownMenuPrimitive.Item
      data-variant={variant}
      onSelect={
        onAction
          ? (event) => {
              event.preventDefault();
              onAction();
            }
          : props.onSelect
      }
      className={cn(
        "relative flex w-full cursor-pointer items-center gap-2.5 rounded-lg px-2.5 py-2 text-sm outline-none select-none",
        "focus:bg-accent focus:text-accent-foreground",
        "data-[variant=destructive]:text-destructive data-[variant=destructive]:focus:bg-destructive/10",
        "data-[disabled]:pointer-events-none data-[disabled]:text-faint",
        "[&_svg]:size-4 [&_svg]:shrink-0",
        className,
      )}
      {...props}
    />
  );
}

export function DropdownMenuLabel({
  className,
  ...props
}: ComponentProps<typeof DropdownMenuPrimitive.Label>) {
  return (
    <DropdownMenuPrimitive.Label
      className={cn(
        "px-2.5 py-1.5 text-2xs font-bold tracking-[0.08em] text-faint uppercase",
        className,
      )}
      {...props}
    />
  );
}

export function DropdownMenuSeparator({
  className,
  ...props
}: ComponentProps<typeof DropdownMenuPrimitive.Separator>) {
  return (
    <DropdownMenuPrimitive.Separator
      className={cn("-mx-0.5 my-1.5 h-px bg-border", className)}
      {...props}
    />
  );
}
