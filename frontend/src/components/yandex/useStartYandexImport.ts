// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "@/lib/api";
import { useToast } from "@/lib/useToast";
import { useT } from "@/contexts/I18nContext";
import type { YandexImportKind } from "@/lib/types";

export function useStartYandexImport() {
  const t = useT();
  const client = useQueryClient();
  const { notify } = useToast();

  return useMutation({
    mutationFn: ({ kind, id }: { kind: YandexImportKind; id?: string }) =>
      api.startYandexImport(kind, id),
    onSuccess: () => {
      notify(t("yandex.started"), "success");
      void client.invalidateQueries({ queryKey: ["yandexImports"] });
    },
  });
}
