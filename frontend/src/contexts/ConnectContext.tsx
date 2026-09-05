// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { createContext } from "react";
import type { ConnectCommandKind, ConnectDevice } from "@/lib/connect";
import { useRequiredContext } from "@/lib/useRequiredContext";

export interface ConnectState {
  id: string;
  devices: ConnectDevice[];
  connected: boolean;
  send: (
    id: string,
    kind: ConnectCommandKind,
    value?: number,
    sourceDeviceId?: string,
  ) => Promise<void>;
}
export const ConnectContext = createContext<ConnectState | null>(null);
export function useConnect() {
  return useRequiredContext(ConnectContext, "useConnect", "PlayerProvider");
}
