// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import dynamic from "next/dynamic";
import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { queries } from "@/lib/queries";
import { useInvalidate } from "@/lib/useInvalidate";
import { PlaylistCard } from "@/components/MediaCard";
import { CardGrid, PageHeader } from "@/components/PageHeader";
import { Query } from "@/components/Query";
import { Button } from "@/components/ui/button";
import { ToggleGroup, ToggleGroupButton } from "@/components/ui/toggle-group";
import { ListMusicIcon, PlusIcon } from "lucide-react";
import { useT } from "@/contexts/I18nContext";

const PlaylistDialog = dynamic(() =>
  import("@/components/PlaylistDialog").then((m) => m.PlaylistDialog),
);

type Tab = "mine" | "public";

export function PlaylistsPage() {
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
        empty={
          tab === "public"
            ? {
                icon: <ListMusicIcon size={24} />,
                title: t("playlists.publicEmptyTitle"),
                description: t("playlists.publicEmptyDescription"),
              }
            : {
                icon: <ListMusicIcon size={24} />,
                title: t("playlists.emptyTitle"),
                description: t("playlists.emptyDescription"),
                action: newButton,
              }
        }
      >
        {(list) => (
          <CardGrid>
            {list.map((playlist) => (
              <PlaylistCard key={playlist.id} playlist={playlist} showOwner={tab === "public"} />
            ))}
          </CardGrid>
        )}
      </Query>

      {creating && (
        <PlaylistDialog
          onClose={() => setCreating(false)}
          onSaved={() => {
            invalidate("playlists");
            setTab("mine");
          }}
        />
      )}
    </>
  );
}
