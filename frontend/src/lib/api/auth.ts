// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { request } from "@/lib/http";
import type { ClientConfig, SystemInfo, User } from "@/lib/types";

export const authApi = {
  login: (username: string, password: string) =>
    request<User>("/auth/login", { method: "POST", body: { username, password } }),
  logout: () => request<void>("/auth/logout", { method: "POST" }),
  me: () => request<User>("/auth/me", { allowUnauthenticated: true }),
  config: () => request<ClientConfig>("/config"),
  system: () => request<SystemInfo>("/system"),
};
