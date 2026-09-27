// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { Suspense, useCallback } from "react";
import { queries } from "@/lib/queries";
import { parseDirection, parsePeriod, parseUploadSort } from "@/lib/stats/adminStatistics";
import { usePage } from "@/lib/usePage";
import { useFormat } from "@/lib/useFormat";
import { PeriodTabs, useUrlFilters } from "@/components/admin/AdminFilters";
import { UploadIcon } from "@/components/Icons";
import { PageHeader } from "@/components/PageHeader";
import { Pagination, PageToolbar, SortSelect } from "@/components/PageToolbar";
import { Query } from "@/components/Query";
import { Button } from "@/components/ui/button";
import { Cell, HeaderCell, Row, Table } from "@/components/ui/table";
import { useT } from "@/contexts/I18nContext";
import type { TranslationKey } from "@/lib/i18n";
import type { AdminUpload, AdminUploadSort, StatisticsPeriod } from "@/lib/types";

const PAGE_SIZE = 50;

const columns = "grid-cols-[minmax(0,1.8fr)_1fr_0.8fr_0.9fr_0.6fr]";

const sortOptions: Record<AdminUploadSort, TranslationKey> = {
  CreatedAt: "field.created",
  FileSize: "admin.stats.sort.FileSize",
  Plays: "admin.stats.sort.Plays",
};

export default function AdminUploadsPage() {
  const t = useT();

  return (
    <Suspense fallback={<PageHeader title={t("admin.stats.uploadsTitle")} />}>
      <AdminUploadsView />
    </Suspense>
  );
}

function AdminUploadsView() {
  const t = useT();
  const { params, set } = useUrlFilters("/admin/statistics/uploads");

  const period = parsePeriod(params.get("period"), "All");
  const sort = parseUploadSort(params.get("sort"), "CreatedAt");
  const direction = parseDirection(params.get("direction"), "Desc");
  const userId = params.get("userId") ?? undefined;
  const search = params.get("q") ?? "";

  const [page, setPage] = usePage([period, sort, direction, userId, search]);

  const uploads = useQuery(
    queries.adminUploads({
      period,
      sort,
      direction,
      userId,
      q: search || undefined,
      page,
      pageSize: PAGE_SIZE,
    }),
  );

  const setPeriod = useCallback(
    (next: StatisticsPeriod) => set({ period: next === "All" ? undefined : next }),
    [set],
  );

  return (
    <>
      <PageHeader
        title={t("admin.stats.uploadsTitle")}
        subtitle={t("admin.stats.uploadsSubtitle")}
      />

      <PeriodTabs period={period} onChange={setPeriod} />

      <PageToolbar
        search={search}
        onSearch={(value) => set({ q: value })}
        placeholder={t("admin.stats.searchUploads")}
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
        result={uploads}
        skeleton="row"
        empty={{ icon: <UploadIcon size={24} />, title: t("admin.stats.uploadsEmpty") }}
      >
        {(data) => (
          <>
            <Table aria-label={t("admin.stats.uploadsTitle")}>
              <Row head className={columns}>
                <HeaderCell>{t("field.title")}</HeaderCell>
                <HeaderCell>{t("admin.stats.addedBy")}</HeaderCell>
                <HeaderCell>{t("field.created")}</HeaderCell>
                <HeaderCell>{t("admin.stats.audio")}</HeaderCell>
                <HeaderCell>{t("admin.stats.plays")}</HeaderCell>
              </Row>

              {data.items.map((upload) => (
                <UploadRow key={upload.trackId} upload={upload} />
              ))}
            </Table>

            <Pagination result={data} onChange={setPage} />
          </>
        )}
      </Query>
    </>
  );
}

function UploadRow({ upload }: { upload: AdminUpload }) {
  const t = useT();
  const format = useFormat();

  return (
    <Row className={columns}>
      <Cell className="truncate">
        {upload.title}
        <span className="text-muted-foreground"> · {upload.artistName}</span>
        <span className="block truncate text-2xs text-faint">{upload.originalFileName}</span>
      </Cell>

      <Cell className="truncate">
        {upload.addedByUsername ? (
          <Link
            href={`/admin/statistics/users/${upload.addedByUserId}`}
            className="hover:text-primary hover:underline"
          >
            {upload.addedByUsername}
          </Link>
        ) : (
          <span className="text-muted-foreground">{t("admin.stats.uploaderUnknown")}</span>
        )}
      </Cell>

      <Cell className="text-muted-foreground">{format.relativeDate(upload.createdAt)}</Cell>

      <Cell className="truncate text-muted-foreground tabular-nums">
        {format.bytes(upload.fileSize)}
        <span className="block text-2xs text-faint">
          {[upload.codec, upload.bitrateKbps && `${upload.bitrateKbps} kbps`]
            .filter(Boolean)
            .join(" · ")}
        </span>
      </Cell>

      <Cell className="tabular-nums">
        {upload.plays}
        <span className="block text-2xs text-faint">{upload.uniqueListeners}</span>
      </Cell>
    </Row>
  );
}
