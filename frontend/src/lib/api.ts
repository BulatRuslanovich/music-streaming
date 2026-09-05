// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

import { adminApi } from "./api/admin";
import { adminStatisticsApi } from "./api/adminStatistics";
import { authApi } from "./api/auth";
import { catalogApi } from "./api/catalog";
import { connectApi } from "./api/connect";
import { integrationsApi } from "./api/integrations";
import { libraryApi } from "./api/library";
import { listeningApi } from "./api/listening";
import { uploadApi } from "./api/upload";

export type { PageParams, TrackSort, UploadProgress } from "./api/contracts";

export const api = {
  ...connectApi,
  ...authApi,
  ...catalogApi,
  ...uploadApi,
  ...libraryApi,
  ...listeningApi,
  ...integrationsApi,
  ...adminApi,
  ...adminStatisticsApi,
};
