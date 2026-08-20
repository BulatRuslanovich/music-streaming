// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { query, request } from "@/lib/http";
import type { AdminUser, Paged } from "@/lib/types";
import type { PageParams } from "./contracts";

export const adminApi = {
  adminUsers: (params: PageParams = {}) =>
    request<Paged<AdminUser>>(`/admin/users${query({ ...params })}`),
  createUser: (body: {
    username: string;
    password: string;
    displayName?: string;
    isAdmin: boolean;
  }) => request<AdminUser>("/admin/users", { method: "POST", body }),
  setUserActive: (id: string, isActive: boolean) =>
    request<AdminUser>(`/admin/users/${id}/active`, { method: "PUT", body: { isActive } }),
  setUserRole: (id: string, isAdmin: boolean) =>
    request<AdminUser>(`/admin/users/${id}/role`, { method: "PUT", body: { isAdmin } }),
  resetUserPassword: (id: string, newPassword: string) =>
    request<void>(`/admin/users/${id}/password`, { method: "POST", body: { newPassword } }),
  revokeUserSessions: (id: string) =>
    request<void>(`/admin/users/${id}/sessions/revoke`, { method: "POST" }),
};
