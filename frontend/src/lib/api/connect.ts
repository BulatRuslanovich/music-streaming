// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { request } from "@/lib/http";
import type { ConnectCommandKind, ConnectPoll, ConnectSnapshot } from "@/lib/connect";
import type { Track } from "@/lib/types";

export const connectApi = {
  connectPoll: (
    id: string,
    name: string,
    state: ConnectSnapshot,
    acknowledged: string[],
    signal: AbortSignal,
  ) =>
    request<ConnectPoll>(`/connect/devices/${encodeURIComponent(id)}`, {
      method: "PUT",
      body: { name, state, acknowledged },
      signal,
    }),
  connectCommand: (id: string, kind: ConnectCommandKind, value?: number, sourceDeviceId?: string) =>
    request<void>(`/connect/devices/${encodeURIComponent(id)}/commands`, {
      method: "POST",
      body: { kind, value, sourceDeviceId },
    }),
  connectRemove: (id: string) =>
    request<void>(`/connect/devices/${encodeURIComponent(id)}`, { method: "DELETE" }),
  connectTracks: (ids: string[], signal: AbortSignal) =>
    request<Track[]>("/connect/tracks", { method: "POST", body: ids, signal }),
};
