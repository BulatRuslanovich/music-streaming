// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { request } from "@/lib/http";
import type { LastfmStatus } from "@/lib/types";

export const integrationsApi = {
  lastfmStatus: () => request<LastfmStatus>("/lastfm/status"),
  lastfmConnect: () => request<{ authorizeUrl: string }>("/lastfm/connect", { method: "POST" }),
  lastfmDisconnect: () => request<void>("/lastfm", { method: "DELETE" }),
};
