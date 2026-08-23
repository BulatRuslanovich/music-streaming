// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { useContext, type Context } from "react";

export function useRequiredContext<T>(
  context: Context<T | null>,
  hook: string,
  provider: string,
): T {
  const value = useContext(context);
  if (!value) throw new Error(`${hook} must be used inside <${provider}>`);
  return value;
}
