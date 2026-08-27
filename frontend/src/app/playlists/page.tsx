// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import dynamic from "next/dynamic";
import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { queries } from "@/lib/queries";
import { useInvalidate } from "@/lib/useInvalidate";
import { CoverMosaic } from "@/components/collection/CoverMosaic";
import { Section } from "@/components/collection/Section";
import { QuickRow, Tile } from "@/components/collection/Tile";
import { PlaylistCard } from "@/components/MediaCard";
import { CardGrid, PageHeader } from "@/components/PageHeader";
import { Query } from "@/components/Query";
import { Button } from "@/components/ui/button";
import { ToggleGroup, ToggleGroupButton } from "@/components/ui/tabs";
import { HeartIcon, HistoryIcon, PlaylistIcon, PlusIcon } from "@/components/Icons";
import { useT } from "@/contexts/I18nContext";

const CreatePlaylistDialog = dynamic(() =>
  import("@/components/CreatePlaylistDialog").then((m) => m.CreatePlaylistDialog),
);

type Tab = "mine" | "public";

export default function PlaylistsPage() {
  const t = useT();
  const invalidate = useInvalidate();

  const [tab, setTab] = useState<Tab>("mine");
  const [creating, setCreating] = useState(false);

  const overview = useQuery(queries.libraryOverview());

  const mine = useQuery({ ...queries.playlists(), enabled: tab === "mine" });
  const shared = useQuery({ ...queries.publicPlaylists(), enabled: tab === "public" });
  const playlists = tab === "public" ? shared : mine;

  const newButton = (
    <Button variant="primary" onClick={() => setCreating(true)}>
      <PlusIcon size={16} /> {t("playlists.new")}
    </Button>
  );

  return (
    <>
      <PageHeader
        title={t("nav.playlists")}
        subtitle={
          playlists.data ? t("count.playlists", { count: playlists.data.length }) : undefined
        }
        actions={newButton}
      />

      <Section title={t("playlists.quickPicks")}>
        <QuickRow>
          <Tile
            href="/favorites"
            label={t("nav.favorites")}
            sublabel={t("count.tracks", { count: overview.data?.stats.favoriteCount ?? 0 })}
            art={
              <span className="grid size-full place-items-center bg-primary-soft text-primary">
                <HeartIcon size={22} />
              </span>
            }
          />
          <Tile
            href="/recently-played"
            label={t("nav.recentlyPlayed")}
            sublabel={t("library.wholeLibrary")}
            art={
              <span className="grid size-full place-items-center bg-raised text-muted-foreground">
                <HistoryIcon size={22} />
              </span>
            }
          />
          {overview.data && overview.data.recentTracks.length > 0 && (
            <Tile
              href="/tracks"
              label={t("library.allTracks")}
              sublabel={t("count.tracks", { count: overview.data.stats.trackCount })}
              art={<CoverMosaic tracks={overview.data.recentTracks} />}
            />
          )}
        </QuickRow>
      </Section>

      <ToggleGroup aria-label={t("playlists.tabs")}>
        {(["mine", "public"] as const).map((value) => (
          <ToggleGroupButton key={value} active={tab === value} onClick={() => setTab(value)}>
            {value === "mine" ? t("playlists.mine") : t("playlists.public")}
          </ToggleGroupButton>
        ))}
      </ToggleGroup>

      <Query
        result={playlists}
        empty={
          tab === "mine"
            ? {
                icon: <PlaylistIcon size={22} />,
                title: t("playlists.emptyTitle"),
                description: t("playlists.emptyDescription"),
                action: newButton,
              }
            : {
                icon: <PlaylistIcon size={22} />,
                title: t("playlists.publicEmptyTitle"),
                description: t("playlists.publicEmptyDescription"),
              }
        }
      >
        {(list) => (
          <Section title={tab === "mine" ? t("playlists.mine") : t("playlists.public")}>
            <CardGrid>
              {list.map((playlist) => (
                <PlaylistCard key={playlist.id} playlist={playlist} showOwner={tab === "public"} />
              ))}
            </CardGrid>
          </Section>
        )}
      </Query>

      {creating && (
        <CreatePlaylistDialog
          onClose={() => setCreating(false)}
          onCreated={() => {
            invalidate("playlists");
            setTab("mine");
          }}
        />
      )}
    </>
  );
}
