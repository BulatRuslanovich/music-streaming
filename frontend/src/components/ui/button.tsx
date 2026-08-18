"use client";

import { Slot } from "@radix-ui/react-slot";
import { cva, type VariantProps } from "class-variance-authority";
import { motion, useReducedMotion } from "motion/react";
import type { ComponentProps } from "react";
import { cn } from "@/lib/cn";

export const buttonVariants = cva(
  "inline-flex items-center justify-center gap-2 whitespace-nowrap font-semibold transition-[background-color,border-color,color,opacity] duration-150 ease-brand outline-none focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring disabled:pointer-events-none disabled:opacity-50 [&_svg]:shrink-0",
  {
    variants: {
      variant: {
        outline: "rounded-full border border-border-strong text-foreground hover:bg-card",
        primary:
          "rounded-full bg-primary text-primary-foreground hover:bg-primary-hover shadow-panel",
        soft: "rounded-full bg-primary-soft text-primary hover:bg-primary-surface",
        secondary: "rounded-full bg-raised text-foreground hover:bg-accent",
        ghost: "rounded-full text-muted-foreground hover:bg-accent hover:text-foreground",
        text: "text-muted-foreground hover:text-foreground hover:underline font-semibold",
        destructive:
          "rounded-full border border-destructive/45 text-destructive hover:bg-destructive/10",
        play: "rounded-full bg-primary text-primary-foreground hover:bg-primary-hover shadow-art",
      },
      size: {
        sm: "h-8 px-3 text-xs",
        md: "h-10 px-4 text-sm",
        lg: "h-12 px-6 text-sm",
        icon: "size-9 max-md:size-10",
        "icon-sm": "size-7",
        auto: "",
        "play-sm": "size-11",
        play: "size-13",
        "play-lg": "size-16",
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
