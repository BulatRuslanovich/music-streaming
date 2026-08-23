// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { queries } from "@/lib/queries";
import { useAuth } from "@/contexts/AuthContext";
import { HomeFeed } from "@/components/home/HomeFeed";
import { PageHeader } from "@/components/PageHeader";
import { Query } from "@/components/Query";
import { EmptyState } from "@/components/EmptyState";
import { Button } from "@/components/ui/button";
import { SearchIcon } from "@/components/Icons";
import { useT } from "@/contexts/I18nContext";

export default function HomePage() {
  const t = useT();

  const { user } = useAuth();
  const feed = useQuery(queries.homeFeed());

  const libraryIsEmpty = feed.data?.stats.trackCount === 0;
  const accountName = user?.displayName || user?.username;

  return (
    <>
      <PageHeader
        title={accountName ? t("home.welcomeNamed", { name: accountName }) : t("home.welcome")}
        // Размер фонотеки не помогает слушать музыку и спорит за внимание с миксом дня.
        subtitle={libraryIsEmpty ? t("home.libraryEmpty") : undefined}
        actions={
          <Button variant="secondary" asChild>
            <Link href="/search">
              <SearchIcon size={18} />
              {t("nav.search")}
            </Link>
          </Button>
        }
      />

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
