// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { SkeletonGroup } from "@/components/ui/skeleton";

// Главная открывается сразу содержимым, без шапки, — скелет повторяет это же.
export default function Loading() {
  return <SkeletonGroup variant="card" count={6} />;
}
