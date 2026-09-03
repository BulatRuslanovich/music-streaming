// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { Suspense, useCallback } from "react";
import { queries } from "@/lib/queries";
import { parseDirection, parseListenerSort, parsePeriod, percent } from "@/lib/adminStatistics";
import { usePage } from "@/lib/usePage";
import { useFormat } from "@/lib/useFormat";
import { PeriodTabs, useUrlFilters } from "@/components/admin/AdminFilters";
import { ArtistIcon } from "@/components/Icons";
import { PageHeader } from "@/components/PageHeader";
import { Pagination, PageToolbar, SortSelect } from "@/components/PageToolbar";
import { Query } from "@/components/Query";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Cell, HeaderCell, Row, Table } from "@/components/ui/table";
import { useT } from "@/contexts/I18nContext";
import type { TranslationKey } from "@/lib/i18n";
import type { AdminListener, AdminListenerSort, StatisticsPeriod } from "@/lib/types";

const PAGE_SIZE = 50;

const columns = "grid-cols-[minmax(0,1.6fr)_1fr_1fr_0.7fr_0.7fr_0.9fr_0.7fr]";

const sortOptions: Record<AdminListenerSort, TranslationKey> = {
  ListenedSeconds: "admin.stats.sort.ListenedSeconds",
  Plays: "admin.stats.sort.Plays",
  UploadedTracks: "admin.stats.sort.UploadedTracks",
  UploadedBytes: "admin.stats.sort.UploadedBytes",
  SkipRate: "admin.stats.sort.SkipRate",
  LastActiveAt: "admin.stats.sort.LastActiveAt",
  CreatedAt: "admin.stats.sort.CreatedAt",
  Username: "admin.stats.sort.Username",
};

export default function AdminListenersPage() {
  const t = useT();

  return (
    <Suspense fallback={<PageHeader title={t("admin.stats.listenersTitle")} />}>
      <AdminListenersView />
    </Suspense>
  );
}

function AdminListenersView() {
  const t = useT();
  const { params, set } = useUrlFilters("/admin/statistics/users");

  const period = parsePeriod(params.get("period"), "Month");
  const sort = parseListenerSort(params.get("sort"), "ListenedSeconds");
  const direction = parseDirection(params.get("direction"), "Desc");
  const search = params.get("q") ?? "";

  // Номер страницы живёт в компоненте, как во всех списках проекта, и сбрасывается сам, когда
  // меняется любой фильтр.
  const [page, setPage] = usePage([period, sort, direction, search]);

  const listeners = useQuery(
    queries.adminListeners({
      period,
      sort,
      direction,
      q: search || undefined,
      page,
      pageSize: PAGE_SIZE,
    }),
  );

  const setPeriod = useCallback(
    (next: StatisticsPeriod) => set({ period: next === "Month" ? undefined : next }),
    [set],
  );

  return (
    <>
      <PageHeader
        title={t("admin.stats.listenersTitle")}
        subtitle={t("admin.stats.listenersSubtitle")}
      />

      <PeriodTabs period={period} onChange={setPeriod} />

      <PageToolbar
        search={search}
        onSearch={(value) => set({ q: value })}
        placeholder={t("admin.stats.searchListeners")}
        sort={
          <div className="flex items-center gap-2">
            <SortSelect
              value={sort}
              onChange={(value) => set({ sort: value })}
              options={sortOptions}
            />
            <Button
              variant="outline"
              size="sm"
              onClick={() => set({ direction: direction === "Desc" ? "Asc" : "Desc" })}
            >
              {direction === "Desc" ? "↓" : "↑"}
            </Button>
          </div>
        }
      />

      <Query
        result={listeners}
        skeleton="row"
        empty={{ icon: <ArtistIcon size={24} />, title: t("admin.stats.listenersEmpty") }}
      >
        {(data) => (
          <>
            <Table aria-label={t("admin.stats.listenersTitle")}>
              <Row head className={columns}>
                <HeaderCell>{t("field.username")}</HeaderCell>
                <HeaderCell>{t("admin.stats.lastActive")}</HeaderCell>
                <HeaderCell>{t("admin.stats.listenedTime")}</HeaderCell>
                <HeaderCell>{t("admin.stats.plays")}</HeaderCell>
                <HeaderCell>{t("admin.stats.uploadedTracks")}</HeaderCell>
                <HeaderCell>{t("admin.stats.uploadedBytes")}</HeaderCell>
                <HeaderCell>{t("admin.stats.skipRate")}</HeaderCell>
              </Row>

              {data.items.map((listener) => (
                <ListenerRow key={listener.id} listener={listener} />
              ))}
            </Table>

            <Pagination result={data} onChange={setPage} />
          </>
        )}
      </Query>
    </>
  );
}

function ListenerRow({ listener }: { listener: AdminListener }) {
  const t = useT();
  const format = useFormat();

  // Кликается вся строка, но ссылка одна и живёт в ячейке с именем: она растягивается
  // псевдоэлементом на всю строку. Обёртка <a> вокруг role="row" дала бы тот же вид, но
  // сломала бы разметку таблицы для скринридера и склеила бы все ячейки в одно имя ссылки.
  return (
    <Row className={`${columns} relative`}>
      <Cell className="truncate">
        <Link
          href={`/admin/statistics/users/${listener.id}`}
          className="font-medium after:absolute after:inset-0 hover:underline"
        >
          {listener.username}
        </Link>
        <span className="text-muted-foreground"> · {listener.displayName}</span>
        {listener.isAdmin && (
          <Badge className="ml-2" variant="outline">
            {t("admin.roleAdmin")}
          </Badge>
        )}
        {!listener.isActive && (
          <Badge className="ml-2" variant="neutral">
            {t("admin.inactive")}
          </Badge>
        )}
      </Cell>
      <Cell className="truncate text-muted-foreground">
        {listener.lastActiveAt
          ? format.relativeDate(listener.lastActiveAt)
          : t("admin.stats.neverActive")}
      </Cell>
      <Cell className="tabular-nums">{format.totalDuration(listener.listenedSeconds)}</Cell>
      <Cell className="tabular-nums">{listener.plays}</Cell>
      <Cell className="tabular-nums">{listener.uploadedTracks}</Cell>
      <Cell className="tabular-nums">{format.bytes(listener.uploadedBytes)}</Cell>
      <Cell className="tabular-nums">{percent(listener.skipRate)}%</Cell>
    </Row>
  );
}
