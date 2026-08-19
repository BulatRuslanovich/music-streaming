// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { clsx, type ClassValue } from "clsx";
import { twMerge } from "tailwind-merge";

export function cn(...inputs: ClassValue[]): string {
  return twMerge(clsx(inputs));
}
