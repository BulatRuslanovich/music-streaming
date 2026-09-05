// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { PageHeader } from "@/components/PageHeader";
import { Query } from "@/components/Query";
import { TrackCover } from "@/components/Cover";
import { Button } from "@/components/ui/button";
import { useI18n } from "@/contexts/I18nContext";
import { useToast } from "@/contexts/ToastContext";
import { usePlayerActions } from "@/contexts/PlayerContext";
import { api } from "@/lib/api";
import { queries } from "@/lib/queries";
import { downloadRecapCard, listeningChange, monthLabel, type MonthlyRecap } from "@/lib/recap";
import { useInvalidate } from "@/lib/useInvalidate";

export default function RecapPage() {
  const { t } = useI18n();
  const [month, setMonth] = useState<string>();
  const result = useQuery(queries.monthlyRecap(month));
  return (
    <>
      <PageHeader
        title={t("recap.title")}
        subtitle={t("recap.subtitle")}
        actions={
          <label className="flex items-center gap-3 text-sm">
            {t("recap.month")}
            <input
              type="month"
              min="2000-01"
              value={month ?? result.data?.month ?? ""}
              className="rounded-md border bg-background p-2"
              onChange={(event) => setMonth(event.target.value || undefined)}
            />
          </label>
        }
      />
      <Query
        result={result}
        skeleton="tile"
        skeletonCount={1}
        isEmpty={(data) => data.listenedSeconds === 0}
        empty={{ title: t("recap.empty"), description: t("recap.emptyHint") }}
      >
        {(data) => <RecapStory key={data.month} data={data} />}
      </Query>
    </>
  );
}

function RecapStory({ data }: { data: MonthlyRecap }) {
  const { t, locale } = useI18n();
  const { notify } = useToast();
  const player = usePlayerActions();
  const router = useRouter();
  const invalidate = useInvalidate();
  const [step, setStep] = useState(0);
  const [busy, setBusy] = useState(false);
  const title = monthLabel(data.month, locale);
  const minutes = Math.round(data.listenedSeconds / 60).toLocaleString(locale);
  const change = listeningChange(data.listenedSeconds, data.previousListenedSeconds);
  const artist = data.topArtists[0];
  const song = data.topTracks[0]?.track;
  const facts = [
    t("recap.minutes", { count: minutes }),
    t("recap.artists", { count: data.uniqueArtists }),
    ...(artist ? [t("recap.artistNamed", { name: artist.name })] : []),
    ...(song ? [t("recap.trackNamed", { name: song.title })] : []),
    ...(data.topGenre ? [t("recap.genreNamed", { name: data.topGenre })] : []),
  ];
  const slides = [
    {
      label: t("recap.yourMonth"),
      headline: t("recap.minutes", { count: minutes }),
      body: <p>{t("recap.breadth", { tracks: data.uniqueTracks, artists: data.uniqueArtists })}</p>,
    },
    ...(artist
      ? [
          {
            label: t("recap.topArtist"),
            headline: artist.name,
            body: (
              <p>
                {t("recap.minutes", {
                  count: Math.round(artist.listenedSeconds / 60).toLocaleString(locale),
                })}
              </p>
            ),
          },
        ]
      : []),
    {
      label: t("recap.topTracks"),
      headline: song?.title ?? "",
      body: (
        <ol className="space-y-3">
          {data.topTracks.slice(0, 5).map(({ track }, index) => (
            <li key={track.id}>
              <button
                className="flex w-full items-center gap-3 text-left hover:underline"
                onClick={() =>
                  player.playQueue(
                    data.topTracks.map((entry) => entry.track),
                    index,
                  )
                }
              >
                <span className="w-5 opacity-50">{index + 1}</span>
                <TrackCover track={track} size={40} />
                <span className="truncate">{track.title}</span>
              </button>
            </li>
          ))}
        </ol>
      ),
    },
    ...(data.discoveries.length
      ? [
          {
            label: t("recap.discoveries"),
            headline: data.discoveries[0].name,
            body: (
              <>
                <p className="mb-4">{t("recap.discoveryHint")}</p>
                <p>{data.discoveries.map((entry) => entry.name).join(" · ")}</p>
              </>
            ),
          },
        ]
      : []),
    ...(change !== null
      ? [
          {
            label: t("recap.comparison"),
            headline: `${change > 0 ? "+" : ""}${change}%`,
            body: (
              <>
                <p>{t("recap.changeHint")}</p>
                {data.topGenre &&
                  data.previousTopGenre &&
                  data.topGenre !== data.previousTopGenre && (
                    <p className="mt-4">
                      {data.previousTopGenre} → {data.topGenre}
                    </p>
                  )}
              </>
            ),
          },
        ]
      : []),
    {
      label: t("recap.keep"),
      headline: title,
      body: (
        <div className="space-y-3">
          {facts.map((fact) => (
            <p key={fact}>{fact}</p>
          ))}
        </div>
      ),
    },
  ];
  const slide = slides[Math.min(step, slides.length - 1)];
  async function save() {
    setBusy(true);
    try {
      const playlist = await api.saveRecapPlaylist(
        data.month,
        t("recap.playlistName", { month: title }),
      );
      invalidate("playlists");
      router.push(`/playlists/${playlist.id}`);
    } catch {
      notify(t("recap.failed"), "error");
    } finally {
      setBusy(false);
    }
  }
  return (
    <section className="mx-auto w-full max-w-3xl space-y-5">
      {!data.isComplete && <p className="text-sm text-muted-foreground">{t("recap.inProgress")}</p>}
      <div className="overflow-hidden rounded-2xl bg-gradient-to-br from-indigo-950 via-slate-900 to-zinc-950 p-6 text-white shadow-xl md:p-10">
        <div className="mb-10 flex gap-2" aria-label={t("recap.pages")}>
          {slides.map((item, index) => (
            <button
              key={item.label}
              onClick={() => setStep(index)}
              aria-label={item.label}
              aria-current={index === step ? "step" : undefined}
              className={`h-2 flex-1 rounded-full ${index <= step ? "bg-violet-300" : "bg-white/20"}`}
            />
          ))}
        </div>
        <div className="min-h-80 space-y-6" aria-live="polite">
          <p className="text-sm tracking-widest text-violet-300 uppercase">{slide.label}</p>
          <h2 className="text-4xl font-bold break-words md:text-6xl">{slide.headline}</h2>
          <div className="text-lg text-slate-200">{slide.body}</div>
        </div>
        <div className="mt-8 flex items-center justify-between">
          <Button variant="ghost" disabled={step === 0} onClick={() => setStep(step - 1)}>
            {t("recap.back")}
          </Button>
          <span className="text-xs tracking-widest text-violet-300">CAIMACK · {data.month}</span>
          <Button
            variant="ghost"
            disabled={step === slides.length - 1}
            onClick={() => setStep(step + 1)}
          >
            {t("recap.next")}
          </Button>
        </div>
      </div>
      <div className="flex flex-wrap gap-3">
        <Button onClick={() => player.playQueue(data.topTracks.map((entry) => entry.track))}>
          {t("recap.listen")}
        </Button>
        <Button variant="outline" disabled={busy} onClick={() => void save()}>
          {t("recap.savePlaylist")}
        </Button>
        <Button
          variant="outline"
          onClick={() =>
            void downloadRecapCard(title, facts, `caimack-${data.month}.png`).catch(() =>
              notify(t("recap.failed"), "error"),
            )
          }
        >
          {t("recap.saveImage")}
        </Button>
      </div>
    </section>
  );
}
