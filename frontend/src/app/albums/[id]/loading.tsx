// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { SkeletonGroup } from "@/components/ui/skeleton";

// Страница ждёт данных на сервере, и без этого файла навигация до самого ответа бэкенда
// не показывала ничего. Скелет повторяет тот, что рисует Query внутри страницы, — переход
// из него в содержимое читается как продолжение, а не как вторая загрузка.
export default function Loading() {
  return <SkeletonGroup variant="detail" count={8} />;
}
