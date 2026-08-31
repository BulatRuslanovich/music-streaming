// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { queries } from "@/lib/queries";
import { HomeFeed } from "@/components/home/HomeFeed";
import { Query } from "@/components/Query";
import { EmptyState } from "@/components/EmptyState";
import { Button } from "@/components/ui/button";
import { useT } from "@/contexts/I18nContext";

export function HomePage() {
  const t = useT();

  const feed = useQuery(queries.homeFeed());

  return (
    <>
      {/* Шапки нет намеренно: главная открывается миксом дня, и приветствие над ним только отодвигало
          содержимое вниз. Пустая библиотека и так объясняется через EmptyState ниже. */}
      <Query result={feed} skeletonCount={6}>
        {(data) =>
          data.blocks.length === 0 ? (
            <EmptyState
              title={t("home.emptyTitle")}
              description={t("home.emptyDescription")}
              action={
                <Button variant="primary" asChild>
                  <Link href="/upload">{t("home.uploadMusic")}</Link>
                </Button>
              }
            />
          ) : (
            <HomeFeed blocks={data.blocks} />
          )
        }
      </Query>
    </>
  );
}
