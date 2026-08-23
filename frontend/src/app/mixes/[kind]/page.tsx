// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useQuery } from "@tanstack/react-query";
import { notFound, useParams } from "next/navigation";
import { trackCoverUrl } from "@/lib/media";
import { queries } from "@/lib/queries";
import { useFormat } from "@/lib/useFormat";
import { useCoverColor } from "@/lib/useCoverColor";
import type { HomeMixSlug } from "@/lib/types";
import type { TranslationKey } from "@/lib/i18n";
import { CoverMosaic } from "@/components/collection/CoverMosaic";
import { Section } from "@/components/collection/Section";
import { DetailHeader } from "@/components/DetailHeader";
import { PlayAllButton } from "@/components/PlayAllButton";
import { Query } from "@/components/Query";
import { TrackList } from "@/components/TrackList";
import { useT } from "@/contexts/I18nContext";

const MIXES: Record<HomeMixSlug, { title: TranslationKey; description: TranslationKey }> = {
  daily: { title: "home.dailyMix", description: "mixes.dailyDescription" },
  new: { title: "home.newArrivals", description: "mixes.newDescription" },
  top: { title: "home.topThisWeek", description: "mixes.topDescription" },
};

function isMixSlug(value: string): value is HomeMixSlug {
  return Object.hasOwn(MIXES, value);
}

export default function MixPage() {
  const kind = useParams<{ kind: string }>().kind;

  if (!isMixSlug(kind)) notFound();

  return <Mix kind={kind} />;
}

function Mix({ kind }: { kind: HomeMixSlug }) {
  const t = useT();
  const format = useFormat();

  const mix = useQuery(queries.homeMix(kind));

  const tracks = mix.data?.tracks ?? [];
  const tint = useCoverColor(trackCoverUrl(tracks[0], "thumb"));

  const title = t(MIXES[kind].title);
  const duration = tracks.reduce((total, track) => total + track.durationSeconds, 0);

  return (
    <Query result={mix} skeleton="detail">
      {(data) => (
        <>
          <DetailHeader
            kind={t("mixes.kind")}
            title={title}
            tint={tint}
            description={t(MIXES[kind].description)}
            art={<CoverMosaic tracks={data.tracks} />}
            facts={
              data.tracks.length > 0 ? (
                <>
                  {t("count.tracks", { count: data.tracks.length })}
                  {duration > 0 && <span> · {format.totalDuration(duration)}</span>}
                </>
              ) : undefined
            }
            actions={
              data.tracks.length > 0 ? (
                <PlayAllButton tracks={data.tracks} name={title} />
              ) : undefined
            }
          />

          <Section title={t("albums.tracks")}>
            <TrackList
              tracks={data.tracks}
              showArtist
              showAlbum
              emptyMessage={t("mixes.empty")}
              origin={{ source: "home", sourceId: kind }}
            />
          </Section>
        </>
      )}
    </Query>
  );
}
