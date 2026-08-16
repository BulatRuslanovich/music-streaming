import { clsx, type ClassValue } from "clsx";
import { twMerge } from "tailwind-merge";

/**
 * Склейка классов, в которой последний победивший класс действительно побеждает: twMerge
 * выкидывает конфликтующие утилиты, поэтому переданный снаружи className переопределяет
 * умолчание примитива, а не соседствует с ним в непредсказуемом порядке.
 */
export function cn(...inputs: ClassValue[]): string {
  return twMerge(clsx(inputs));
}
