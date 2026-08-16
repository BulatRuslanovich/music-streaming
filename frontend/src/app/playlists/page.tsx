"use client";

import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { queries } from "@/lib/queries";
import { useInvalidate } from "@/lib/useInvalidate";
import { CreatePlaylistDialog } from "@/components/CreatePlaylistDialog";
import { PlaylistCard } from "@/components/MediaCard";
import { CardGrid, PageHeader } from "@/components/PageHeader";
import { Query } from "@/components/Query";
import { Button } from "@/components/ui/button";
import { ToggleGroup, ToggleGroupButton } from "@/components/ui/tabs";
import { PlusIcon } from "@/components/Icons";
import { useT } from "@/contexts/I18nContext";

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
        empty={
          tab === "mine"
            ? {
                title: t("playlists.emptyTitle"),
                description: t("playlists.emptyDescription"),
                action: newButton,
              }
            : {
                title: t("playlists.publicEmptyTitle"),
                description: t("playlists.publicEmptyDescription"),
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
