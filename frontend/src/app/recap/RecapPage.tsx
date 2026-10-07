// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useMutation, useQuery } from "@tanstack/react-query";
import { CalendarRangeIcon } from "lucide-react";
import type { Route } from "next";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import type { CSSProperties } from "react";
import { cn } from "@/lib/cn";
import { formatArtists } from "@/lib/format";
import { artistImageUrl, coverUrl } from "@/lib/media";
import { moodLabel } from "@/lib/moods";
import { queries } from "@/lib/queries";
import {
  busiestDay,
  dominantDaypart,
  intensity,
  leadingBlanks,
  monthKey,
  parseMonthKey,
} from "@/lib/recap";
import type { Recap, RecapMonth, Track } from "@/lib/types";
import { useFormat } from "@/lib/useFormat";
import { useMonthLabel } from "@/lib/useMonthLabel";
import { usePlayback } from "@/lib/playback/usePlayback";
import { usePlayerActions } from "@/contexts/PlayerContext";
import { useI18n, useT } from "@/contexts/I18nContext";
import { ArtBackdrop } from "@/components/ArtBackdrop";
import { ArtistCover, TrackCover } from "@/components/Cover";
import { ArtistCard } from "@/components/MediaCard";
import { MoodIcon } from "@/components/MoodIcon";
import { PageHeader, Section } from "@/components/PageHeader";
import { PlayBadge } from "@/components/PlayBadge";
import { Query } from "@/components/Query";
import { hiddenScrollbar } from "@/components/Shelf";
import { Button } from "@/components/ui/button";
import { ToggleGroup, ToggleGroupButton } from "@/components/ui/toggle-group";

export function RecapPage() {
  const t = useT();
  const months = useQuery(queries.recapMonths());

  return (
    <Query
      result={months}
      isEmpty={(data) => data.length === 0}
      empty={{
        title: t("recap.emptyTitle"),
        description: t("recap.emptyDescription"),
        icon: <CalendarRangeIcon />,
      }}
    >
      {(data) => <Months months={data} />}
    </Query>
  );
}

function Months({ months }: { months: RecapMonth[] }) {
  const t = useT();
  const router = useRouter();
  const label = useMonthLabel();

  const asked = parseMonthKey(useSearchParams().get("month"));
  const chosen =
    months.find((item) => item.year === asked?.year && item.month === asked.month) ?? months[0];

  const recap = useQuery(queries.recap(chosen.year, chosen.month));

  const pick = (item: RecapMonth) =>
    router.replace(`/recap?month=${monthKey(item.year, item.month)}` as Route, { scroll: false });

  return (
    <>
      {months.length > 1 && (
        <ToggleGroup
          aria-label={t("recap.months")}
          className={cn(
            "max-md:-mx-4 max-md:flex-nowrap max-md:overflow-x-auto max-md:px-4",
            hiddenScrollbar,
          )}
        >
          {months.map((item) => (
            <ToggleGroupButton
              key={monthKey(item.year, item.month)}
              active={item === chosen}
              onClick={() => pick(item)}
            >
              {label(item.year, item.month)}
            </ToggleGroupButton>
          ))}
        </ToggleGroup>
      )}

      <Query result={recap}>{(data) => <Summary recap={data} />}</Query>
    </>
  );
}

function Summary({ recap }: { recap: Recap }) {
  return (
    <>
      <Headline recap={recap} />
      <div className="grid grid-cols-2 gap-x-10 gap-y-12 max-lg:grid-cols-1">
        <TopArtists recap={recap} />
        <TopTracks recap={recap} />
        <Calendar recap={recap} />
        <Hours recap={recap} />
        <Mood recap={recap} />
        <SoundOfMonth recap={recap} />
      </div>
      <Discoveries recap={recap} />
      <Genres recap={recap} />
    </>
  );
}

function Headline({ recap }: { recap: Recap }) {
  const t = useT();
  const format = useFormat();
  const label = useMonthLabel();

  const lead = recap.topArtists[0]?.artist;
  const backdrop =
    artistImageUrl({ artistId: lead?.id, hasImage: lead?.hasImage }) ??
    (recap.topTracks[0]
      ? coverUrl({
          albumId: recap.topTracks[0].track.albumId,
          trackId: recap.topTracks[0].track.id,
          hasCover: recap.topTracks[0].track.hasCover,
        })
      : null);

  const delta =
    recap.previousListenedSeconds == null
      ? null
      : recap.listenedSeconds - recap.previousListenedSeconds;

  return (
    <header className="flex flex-col gap-4 pt-2">
      <ArtBackdrop src={backdrop} mode="header" />

      <PageHeader title={label(recap.year, recap.month, true)} />

      <p className="flex flex-wrap items-baseline gap-x-3 font-display">
        <span className="text-[clamp(2.75rem,7vw,4.5rem)] leading-none font-semibold tracking-tight tabular-nums">
          {format.totalDuration(recap.listenedSeconds)}
        </span>
        <span className="text-xl text-muted-foreground">{t("recap.ofMusic")}</span>
      </p>

      <p className="flex flex-wrap gap-x-5 gap-y-1 text-sm font-medium text-foreground/85">
        <span>{t("recap.plays", { count: recap.plays })}</span>
        <span>{t("count.tracks", { count: recap.distinctTracks })}</span>
        <span>{t("count.artists", { count: recap.distinctArtists })}</span>
      </p>

      {delta !== null && Math.abs(delta) >= 60 && (
        <p className="text-sm">
          {t(delta > 0 ? "recap.moreThanPrevious" : "recap.lessThanPrevious", {
            duration: format.totalDuration(Math.abs(delta)),
          })}
        </p>
      )}

      {!recap.complete && <p className="text-sm text-muted-foreground">{t("recap.inProgress")}</p>}
    </header>
  );
}

function TopArtists({ recap }: { recap: Recap }) {
  const t = useT();
  const format = useFormat();

  if (recap.topArtists.length === 0) return null;

  return (
    <Section title={t("recap.topArtists")}>
      <ol className="flex flex-col gap-1">
        {recap.topArtists.map(({ artist, listenedSeconds }, index) => (
          <li key={artist.id}>
            <Link
              href={`/artists/${artist.id}`}
              className="grid grid-cols-[1.5rem_3rem_minmax(0,1fr)_auto] items-center gap-3 rounded-md px-2 py-1.5 transition-colors duration-150 ease-brand hover:bg-card hover:no-underline"
            >
              <span className="text-right text-sm text-faint tabular-nums">{index + 1}</span>
              <span className="size-12 overflow-hidden rounded-full">
                <ArtistCover artist={artist} />
              </span>
              <span className="truncate font-medium">{artist.name}</span>
              <span className="text-sm text-muted-foreground tabular-nums">
                {format.totalDuration(listenedSeconds)}
              </span>
            </Link>
          </li>
        ))}
      </ol>
    </Section>
  );
}

function TopTracks({ recap }: { recap: Recap }) {
  const t = useT();
  const tracks = recap.topTracks.map((item) => item.track);

  if (tracks.length === 0) return null;

  return (
    <Section title={t("recap.topTracks")}>
      <ol className="flex flex-col gap-1">
        {recap.topTracks.map(({ track, plays }, index) => (
          <li key={track.id}>
            <TrackRow
              track={track}
              context={tracks}
              rank={index + 1}
              aside={t("recap.times", { count: plays })}
            />
          </li>
        ))}
      </ol>
    </Section>
  );
}

function TrackRow({
  track,
  context,
  rank,
  aside,
}: {
  track: Track;
  context: Track[];
  rank?: number;
  aside?: string;
}) {
  const t = useT();
  const { currentTrackId, playTrack, soundingNow } = usePlayback();
  const current = currentTrackId === track.id;
  const playing = soundingNow(track.id);

  return (
    <button
      type="button"
      onClick={() => playTrack(track, context)}
      aria-label={`${playing ? t("action.pause") : t("action.play")}: ${track.title}`}
      className={cn(
        "grid w-full items-center gap-3 rounded-md px-2 py-1.5 text-left transition-colors duration-150 ease-brand hover:bg-card",
        rank === undefined
          ? "grid-cols-[3rem_minmax(0,1fr)_auto]"
          : "grid-cols-[1.5rem_3rem_minmax(0,1fr)_auto]",
      )}
    >
      {rank !== undefined && (
        <span className="text-right text-sm text-faint tabular-nums">{rank}</span>
      )}
      <span className="relative size-12 overflow-hidden rounded-xs">
        <TrackCover track={track} />
        <PlayBadge size={8} playing={playing} visible={current} className="absolute top-1 left-1" />
      </span>
      <span className="min-w-0">
        <span className={cn("block truncate font-medium", current && "text-primary")}>
          {track.title}
        </span>
        <span className="block truncate text-sm text-muted-foreground">{formatArtists(track)}</span>
      </span>
      {aside && <span className="text-sm text-muted-foreground tabular-nums">{aside}</span>}
    </button>
  );
}

// Календарь месяца: клетка дня тем ярче, чем дольше в этот день звучала музыка.
function Calendar({ recap }: { recap: Recap }) {
  const t = useT();
  const { locale } = useI18n();
  const format = useFormat();

  const max = Math.max(...recap.daySeconds);
  const peak = busiestDay(recap.daySeconds);
  const blanks = leadingBlanks(recap.year, recap.month);

  // 5 января 2026 — понедельник: от него берутся подписи дней недели.
  const weekdays = Array.from({ length: 7 }, (_, index) =>
    new Date(2026, 0, 5 + index).toLocaleDateString(locale, { weekday: "short" }),
  );
  const dateOf = (day: number) =>
    new Date(recap.year, recap.month - 1, day).toLocaleDateString(locale, {
      day: "numeric",
      month: "long",
    });

  return (
    <Section title={t("recap.byDay")}>
      <div className="grid max-w-md grid-cols-7 gap-1.5">
        {weekdays.map((weekday) => (
          <span key={weekday} className="pb-1 text-center text-xs text-faint">
            {weekday}
          </span>
        ))}
        {Array.from({ length: blanks }, (_, index) => (
          <span key={`blank-${index}`} />
        ))}
        {recap.daySeconds.map((seconds, index) => {
          const level = intensity(seconds, max);
          const day = index + 1;

          return (
            <span
              key={day}
              title={`${dateOf(day)}: ${format.totalDuration(seconds)}`}
              className={cn(
                "grid aspect-square place-items-center rounded-sm bg-card text-xs tabular-nums",
                level > 0.6 ? "text-primary-foreground" : "text-muted-foreground",
                peak?.day === day && "ring-2 ring-primary ring-offset-2 ring-offset-background",
              )}
              style={
                level > 0
                  ? ({
                      backgroundColor: `color-mix(in oklab, var(--color-primary) ${Math.round(15 + level * 85)}%, var(--color-card))`,
                    } as CSSProperties)
                  : undefined
              }
            >
              {day}
            </span>
          );
        })}
      </div>
      {peak && (
        <p className="text-sm text-muted-foreground">
          {t("recap.busiestDay", {
            date: dateOf(peak.day),
            duration: format.totalDuration(peak.seconds),
          })}
        </p>
      )}
    </Section>
  );
}

// Сутки полосой из 24 столбиков: видно, когда музыка звучит, а когда тишина.
function Hours({ recap }: { recap: Recap }) {
  const t = useT();
  const format = useFormat();

  const max = Math.max(...recap.hourSeconds);
  const daypart = dominantDaypart(recap.hourSeconds);

  return (
    <Section title={t("recap.byHour")} note={daypart ? t(`recap.daypart.${daypart}`) : undefined}>
      <div className="flex flex-col gap-2">
        <div className="flex h-40 items-end gap-1" role="img" aria-label={t("recap.byHour")}>
          {recap.hourSeconds.map((seconds, hour) => (
            <span
              key={hour}
              title={`${String(hour).padStart(2, "0")}:00 — ${format.totalDuration(seconds)}`}
              className={cn(
                "flex-1 rounded-t-xs",
                seconds > 0 ? "bg-primary" : "bg-card",
                seconds === max && max > 0 ? "opacity-100" : "opacity-70",
              )}
              style={{ height: `${Math.max(4, max > 0 ? (seconds / max) * 100 : 0)}%` }}
            />
          ))}
        </div>
        <div className="grid grid-cols-4 text-xs text-faint tabular-nums">
          {["00", "06", "12", "18"].map((hour) => (
            <span key={hour}>{hour}</span>
          ))}
        </div>
      </div>
    </Section>
  );
}

function Mood({ recap }: { recap: Recap }) {
  const t = useT();
  const player = usePlayerActions();
  const start = useMutation({ mutationFn: (mood: string) => player.startRadio(null, mood) });

  const lead = recap.moods[0];
  if (!lead) return null;

  return (
    <Section title={t("recap.mood")}>
      <p className="flex items-center gap-3 font-display text-2xl">
        <MoodIcon mood={lead.key} className="size-7 text-primary" />
        {t("recap.moodLead", { mood: moodLabel(lead.key, t) })}
      </p>

      <div className="flex h-3 overflow-hidden rounded-full bg-card">
        {recap.moods.map((mood, index) => (
          <span
            key={mood.key}
            className="h-full bg-primary"
            style={{
              width: `${mood.share * 100}%`,
              opacity: Math.max(0.2, 1 - index * 0.18),
            }}
          />
        ))}
      </div>

      <ul className="flex flex-wrap gap-x-5 gap-y-1 text-sm text-muted-foreground">
        {recap.moods.map((mood) => (
          <li key={mood.key}>
            {moodLabel(mood.key, t)}{" "}
            <span className="tabular-nums">{Math.round(mood.share * 100)}%</span>
          </li>
        ))}
      </ul>

      <div>
        <Button onClick={() => start.mutate(lead.key)} disabled={start.isPending}>
          <MoodIcon mood={lead.key} size={16} />
          {t("recap.moodRadio", { mood: moodLabel(lead.key, t) })}
        </Button>
      </div>
    </Section>
  );
}

function SoundOfMonth({ recap }: { recap: Recap }) {
  const t = useT();
  const track = recap.soundOfMonth;

  if (!track) return null;

  return (
    <Section title={t("recap.sound")} note={t("recap.soundNote")}>
      <TrackRow track={track} context={[track]} />
    </Section>
  );
}

function Discoveries({ recap }: { recap: Recap }) {
  const t = useT();

  if (recap.newArtists == null) return null;

  return (
    <Section
      title={t("recap.discoveries")}
      note={
        recap.newArtists > 0
          ? t("recap.newArtists", { count: recap.newArtists })
          : t("recap.noDiscoveries")
      }
    >
      {recap.newArtistPicks.length > 0 && (
        <div className="grid grid-cols-[repeat(auto-fill,minmax(9.5rem,1fr))] gap-4">
          {recap.newArtistPicks.map((artist) => (
            <ArtistCard key={artist.id} artist={artist} bare />
          ))}
        </div>
      )}
    </Section>
  );
}

function Genres({ recap }: { recap: Recap }) {
  const t = useT();

  if (recap.topGenres.length === 0) return null;

  return (
    <Section title={t("recap.genres")}>
      <ul className="flex max-w-2xl flex-col gap-3">
        {recap.topGenres.map((genre) => (
          <li
            key={genre.id}
            className="grid grid-cols-[minmax(0,12rem)_minmax(0,1fr)_3rem] items-center gap-4 text-sm"
          >
            <Link href={`/genres?id=${genre.id}`} className="truncate font-medium">
              {genre.name}
            </Link>
            <span className="h-2 overflow-hidden rounded-full bg-card">
              <span
                className="block h-full rounded-full bg-primary"
                style={{ width: `${genre.share * 100}%` }}
              />
            </span>
            <span className="text-right text-muted-foreground tabular-nums">
              {Math.round(genre.share * 100)}%
            </span>
          </li>
        ))}
      </ul>
    </Section>
  );
}
