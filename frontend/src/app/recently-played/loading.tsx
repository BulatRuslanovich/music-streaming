// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { PageLoading } from "@/components/PageLoading";

export default function Loading() {
  return <PageLoading title="nav.recentlyPlayed" variant="row" count={12} />;
}
