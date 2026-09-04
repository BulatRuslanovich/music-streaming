// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import type { User } from "@/lib/types";

export function userAfterMeFailure(hint: User | null, online: boolean): User | null {
  return online ? null : hint;
}
