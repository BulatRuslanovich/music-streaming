// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import dynamic from "next/dynamic";
import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { queries } from "@/lib/queries";
import { useInvalidate } from "@/lib/useInvalidate";
import { LibraryCards } from "@/components/collection/LibraryCards";
import { Section } from "@/components/collection/Section";
import { EmptyState } from "@/components/EmptyState";
import { PlaylistCard } from "@/components/MediaCard";
import { CardGrid, PageHeader } from "@/components/PageHeader";
import { Query } from "@/components/Query";
import { Button } from "@/components/ui/button";
import { ToggleGroup, ToggleGroupButton } from "@/components/ui/tabs";
import { PlaylistIcon, PlusIcon } from "@/components/Icons";
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

      <ToggleGroup aria-label={t("playlists.tabs")}>
        {(["mine", "public"] as const).map((value) => (
          <ToggleGroupButton key={value} active={tab === value} onClick={() => setTab(value)}>
            {value === "mine" ? t("playlists.mine") : t("playlists.public")}
          </ToggleGroupButton>
        ))}
      </ToggleGroup>

      <Query
        result={playlists}
        // На своей вкладке пусто не бывает: три карточки фонотеки есть всегда, поэтому
        // приглашение завести плейлист живёт под сеткой, а не вместо неё.
        empty={
          tab === "public"
            ? {
                icon: <PlaylistIcon size={22} />,
                title: t("playlists.publicEmptyTitle"),
                description: t("playlists.publicEmptyDescription"),
              }
            : undefined
        }
      >
        {(list) => (
          <Section title={tab === "mine" ? t("playlists.mine") : t("playlists.public")}>
            <CardGrid>
              {tab === "mine" && <LibraryCards />}
              {list.map((playlist) => (
                <PlaylistCard key={playlist.id} playlist={playlist} showOwner={tab === "public"} />
              ))}
            </CardGrid>

            {tab === "mine" && list.length === 0 && (
              <EmptyState
                icon={<PlaylistIcon size={22} />}
                title={t("playlists.emptyTitle")}
                description={t("playlists.emptyDescription")}
                action={newButton}
              />
            )}
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
