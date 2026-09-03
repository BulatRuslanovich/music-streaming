// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { query, request } from "@/lib/http";
import type {
  AdminCatalogHealth,
  AdminListener,
  AdminListenerDetail,
  AdminListenerSort,
  AdminOverview,
  AdminUpload,
  AdminUploadSort,
  IngestionSource,
  Paged,
  SortDirection,
  StatisticsPeriod,
} from "@/lib/types";
import type { PageParams } from "./contracts";

export interface AdminListenerParams extends PageParams {
  period?: StatisticsPeriod;
  q?: string;
  sort?: AdminListenerSort;
  direction?: SortDirection;
}

export interface AdminUploadParams extends PageParams {
  period?: StatisticsPeriod;
  userId?: string;
  source?: IngestionSource;
  q?: string;
  sort?: AdminUploadSort;
  direction?: SortDirection;
}

export const adminStatisticsApi = {
  adminOverview: (period: StatisticsPeriod) =>
    request<AdminOverview>(`/admin/statistics/overview${query({ period })}`),

  adminCatalogHealth: () => request<AdminCatalogHealth>("/admin/statistics/catalog"),

  adminListeners: (params: AdminListenerParams = {}) =>
    request<Paged<AdminListener>>(`/admin/statistics/users${query({ ...params })}`),

  adminListener: (id: string, period: StatisticsPeriod) =>
    request<AdminListenerDetail>(`/admin/statistics/users/${id}${query({ period })}`),

  adminUploads: (params: AdminUploadParams = {}) =>
    request<Paged<AdminUpload>>(`/admin/statistics/uploads${query({ ...params })}`),
};
