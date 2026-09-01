// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { Slot } from "@radix-ui/react-slot";
import { cva, type VariantProps } from "class-variance-authority";
import { motion, useReducedMotion } from "motion/react";
import type { ComponentProps } from "react";
import { cn } from "@/lib/cn";

/**
 * Скругление живёт на размере, а не на варианте: круглым остаётся то, что круглое по форме —
 * иконочные кнопки и Play. Широкая пилюля рядом с почти прямоугольной карточкой (радиус 8px)
 * была единственным местом, где шкала скруглений спорила сама с собой.
 *
 * `primary` и `play` красятся `--action`, а не `--primary`: это действие, а не состояние.
 * Пока обе роли делили один токен, «нажми меня» и «это сейчас звучит» говорили одним цветом.
 */
const buttonVariants = cva(
  "inline-flex items-center justify-center gap-2 whitespace-nowrap font-medium transition-[background-color,border-color,color,opacity] duration-150 ease-brand outline-none focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring disabled:pointer-events-none disabled:opacity-50 [&_svg]:shrink-0",
  {
    variants: {
      variant: {
        outline: "border border-control-border text-foreground hover:border-foreground",
        primary: "bg-action text-action-foreground hover:bg-action-hover font-semibold",
        secondary: "bg-raised text-foreground hover:bg-accent",
        ghost: "text-muted-foreground hover:bg-accent hover:text-foreground",
        text: "text-muted-foreground hover:text-foreground hover:underline",
        destructive: "border border-destructive/60 text-destructive hover:bg-destructive/10",
        play: "bg-action text-action-foreground hover:bg-action-hover shadow-art",
      },
      size: {
        sm: "h-8 rounded-lg px-3 text-xs",
        md: "h-10 rounded-lg px-4 text-sm",
        lg: "h-12 rounded-lg px-6 text-sm",
        icon: "size-9 rounded-full max-md:size-10",
        "icon-sm": "size-7 rounded-full",
        "icon-lg": "size-11 rounded-full",
        auto: "",
        play: "size-13 rounded-full",
        "play-lg": "size-16 rounded-full",
      },
    },
    defaultVariants: { variant: "outline", size: "md" },
  },
);

type ButtonProps = ComponentProps<"button"> &
  VariantProps<typeof buttonVariants> & { asChild?: boolean };

export function Button({ className, variant, size, asChild, type, ...props }: ButtonProps) {
  const Component = asChild ? Slot : "button";

  return (
    <Component
      type={asChild ? undefined : (type ?? "button")}
      className={cn(buttonVariants({ variant, size }), className)}
      {...props}
    />
  );
}

export function PressButton({
  className,
  variant,
  size,
  type = "button",
  ...props
}: ComponentProps<typeof motion.button> & VariantProps<typeof buttonVariants>) {
  const reduceMotion = useReducedMotion();

  return (
    <motion.button
      type={type}
      whileTap={reduceMotion ? undefined : { scale: 0.92 }}
      transition={{ duration: 0.1 }}
      className={cn(buttonVariants({ variant, size }), className)}
      {...props}
    />
  );
}
