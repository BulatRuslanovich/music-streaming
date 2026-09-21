// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { SkeletonGroup } from "@/components/ui/skeleton";

// Скелет на время серверного ожидания. Повторяет тот, что рисует Query внутри страницы:
// переход из него в содержимое читается как продолжение, а не как вторая загрузка.
export default function Loading() {
  return <SkeletonGroup variant="detail" count={8} />;
}
