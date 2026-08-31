// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { SkeletonGroup } from "@/components/ui/skeleton";

export default function Loading() {
  return <SkeletonGroup variant="detail" count={12} />;
}
