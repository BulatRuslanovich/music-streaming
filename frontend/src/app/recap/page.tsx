// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import * as DialogPrimitive from "@radix-ui/react-dialog";
import { useMutation, useQuery } from "@tanstack/react-query";
import { ChevronLeftIcon, ChevronRightIcon, PauseIcon, PlayIcon, XIcon } from "lucide-react";
import { useRouter } from "next/navigation";
import { useCallback, useEffect, useRef, useState, type ReactNode } from "react";
import { cn } from "@/lib/cn";
import { formatArtists } from "@/lib/format";
import { artistImageUrl, trackCoverUrl } from "@/lib/media";
import { moodLabel } from "@/lib/moods";
import { queries } from "@/lib/queries";
import {
  DAYPART_HOURS,
  busiestDay,
  capitalize,
  daypartOf,
  dominantDaypart,
  intensity,
  leadingBlanks,
  monthName,
  type Daypart,
} from "@/lib/recap";
import type { Artist, Recap, Track } from "@/lib/types";
import { useFormat } from "@/lib/useFormat";
import { usePlayback } from "@/lib/playback/usePlayback";
import { usePlayerActions } from "@/contexts/PlayerContext";
import { useI18n, useT } from "@/contexts/I18nContext";
import { ArtBackdrop } from "@/components/ArtBackdrop";
import { ArtistCover, TrackCover } from "@/components/Cover";
import { Loading } from "@/components/Loading";
import { MoodIcon } from "@/components/MoodIcon";
import { Button } from "@/components/ui/button";

const SLIDE_MS = 7000;

const segment = "block h-full rounded-full bg-foreground";

// Короткое касание листает, удержание дольше этого — пауза, как в сторис.
const HOLD_MS = 250;

interface Slide {
  key: string;
  art: string | null;
  body: ReactNode;
}

// Вне первой недели месяца итогов нет, и страница возвращает на главную.
export default function RecapPage() {
  const router = useRouter();
  const recap = useQuery(queries.recap());

  const missing = recap.isError || recap.data === null;

  useEffect(() => {
    if (missing) router.replace("/");
  }, [missing, router]);

  return (
    <div className="fixed inset-0 z-95 overflow-hidden bg-background text-foreground">
      {recap.data ? (
        <Story recap={recap.data} onClose={() => router.push("/")} />
      ) : (
        <div className="grid h-full place-items-center">
          <Loading />
        </div>
      )}
    </div>
  );
}

function Story({ recap, onClose }: { recap: Recap; onClose: () => void }) {
  const t = useT();
  const [index, setIndex] = useState(0);
  const slides = useSlides(recap, onClose, () => setIndex(0));

  const [paused, setPaused] = useState(false);
  const [held, setHeld] = useState(false);
  const hidden = useDocumentHidden();
  const downAt = useRef(0);

  const last = slides.length - 1;
  const slide = slides[Math.min(index, last)];
  const running = !paused && !held && !hidden;

  const go = useCallback(
    (step: number) => setIndex((current) => Math.min(Math.max(current + step, 0), last)),
    [last],
  );

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key === "ArrowRight") go(1);
      else if (event.key === "ArrowLeft") go(-1);
      else if (event.key === " ") {
        event.preventDefault();
        setPaused((value) => !value);
      }
    };

    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [go]);

  const zone = (step: number) => ({
    onPointerDown: () => {
      downAt.current = Date.now();
      setHeld(true);
    },
    onPointerUp: () => setHeld(false),
    onPointerLeave: () => setHeld(false),
    onPointerCancel: () => setHeld(false),
    onClick: () => {
      if (Date.now() - downAt.current < HOLD_MS) go(step);
    },
  });

  return (
    <DialogPrimitive.Root open onOpenChange={(next) => !next && onClose()}>
      <DialogPrimitive.Content asChild aria-describedby={undefined}>
        <section className="h-full outline-none">
          <DialogPrimitive.Title className="sr-only">{t("recap.title")}</DialogPrimitive.Title>
          <ArtBackdrop src={slide.art} mode="stage" />

          <div className="relative mx-auto flex h-full max-w-xl flex-col gap-4 px-5 pt-[max(1rem,env(safe-area-inset-top))] pb-[max(1.5rem,env(safe-area-inset-bottom))]">
            <div className="flex gap-1" aria-hidden="true">
              {slides.map((item, position) => (
                <span
                  key={item.key}
                  className="h-1 flex-1 overflow-hidden rounded-full bg-foreground/20"
                >
                  {position < index && <span className={segment} />}
                  {position === index && (
                    <span
                      key={index}
                      className={cn(segment, "recap-progress")}
                      style={{
                        animationDuration: `${SLIDE_MS}ms`,
                        animationPlayState: running ? "running" : "paused",
                      }}
                      onAnimationEnd={() => go(1)}
                    />
                  )}
                </span>
              ))}
            </div>

            <div className="flex items-center justify-between">
              <span className="text-sm font-medium text-muted-foreground">{t("recap.title")}</span>
              <div className="flex items-center gap-1">
                <Button
                  variant="ghost"
                  size="icon"
                  onClick={() => setPaused((value) => !value)}
                  aria-label={paused ? t("recap.resume") : t("recap.pause")}
                >
                  {paused ? <PlayIcon size={18} /> : <PauseIcon size={18} />}
                </Button>
                <Button variant="ghost" size="icon" onClick={onClose} aria-label={t("recap.close")}>
                  <XIcon size={20} />
                </Button>
              </div>
            </div>

            <div className="relative min-h-0 flex-1">
              <button
                type="button"
                aria-label={t("recap.previous")}
                className="absolute inset-y-0 left-0 w-1/3 cursor-w-resize outline-none"
                {...zone(-1)}
              />
              <button
                type="button"
                aria-label={t("recap.next")}
                className="absolute inset-y-0 right-0 w-2/3 cursor-e-resize outline-none"
                {...zone(1)}
              />

              <div
                key={slide.key}
                className={cn(
                  "pointer-events-none relative flex h-full flex-col justify-center gap-6 overflow-y-auto",
                  "animate-in duration-500 fade-in-0 slide-in-from-bottom-4 motion-reduce:animate-none",
                  "[&_a]:pointer-events-auto [&_button]:pointer-events-auto",
                )}
              >
                {slide.body}
              </div>
            </div>
          </div>

          {index > 0 && (
            <SideArrow side="left" label={t("recap.previous")} onClick={() => go(-1)} />
          )}
          {index < last && <SideArrow side="right" label={t("recap.next")} onClick={() => go(1)} />}
        </section>
      </DialogPrimitive.Content>
    </DialogPrimitive.Root>
  );
}

function SideArrow({
  side,
  label,
  onClick,
}: {
  side: "left" | "right";
  label: string;
  onClick: () => void;
}) {
  return (
    <Button
      variant="secondary"
      size="icon-lg"
      onClick={onClick}
      aria-label={label}
      className={cn(
        "absolute top-1/2 -translate-y-1/2 max-md:hidden",
        side === "left"
          ? "left-[max(1.5rem,calc(50%-22rem))]"
          : "right-[max(1.5rem,calc(50%-22rem))]",
      )}
    >
      {side === "left" ? <ChevronLeftIcon /> : <ChevronRightIcon />}
    </Button>
  );
}

function useDocumentHidden(): boolean {
  const [hidden, setHidden] = useState(false);

  useEffect(() => {
    const update = () => setHidden(document.hidden);
    document.addEventListener("visibilitychange", update);
    return () => document.removeEventListener("visibilitychange", update);
  }, []);

  return hidden;
}

// Слайды собираются из того, что есть: пустой раздел просто выпадает из истории.
function useSlides(recap: Recap, onClose: () => void, onRestart: () => void): Slide[] {
  const { locale } = useI18n();
  const month = monthName(recap.year, recap.month, locale);

  const artist = recap.topArtists[0]?.artist;
  const track = recap.topTracks[0]?.track;
  // Фон всё равно размыт до неузнаваемости — хватает миниатюр.
  const artistArt = artist
    ? artistImageUrl({ artistId: artist.id, hasImage: artist.hasImage, variant: "thumb" })
    : null;
  const trackArt = (item?: Track | null) => (item ? trackCoverUrl(item, "thumb") : null);
  const fallbackArt = artistArt ?? trackArt(track);
  const peak = busiestDay(recap.daySeconds);
  const hours = dominantDaypart(recap.hourSeconds);

  const slides: Slide[] = [
    {
      key: "intro",
      art: trackArt(track) ?? fallbackArt,
      body: <Intro recap={recap} month={month} />,
    },
    { key: "time", art: fallbackArt, body: <Time recap={recap} /> },
  ];

  if (artist) slides.push({ key: "artist", art: artistArt, body: <TopArtist recap={recap} /> });
  if (track)
    slides.push({
      key: "track",
      art: trackArt(track) ?? fallbackArt,
      body: <TopTrack recap={recap} />,
    });
  if (peak)
    slides.push({ key: "day", art: fallbackArt, body: <BusiestDay recap={recap} peak={peak} /> });
  if (hours)
    slides.push({
      key: "hours",
      art: fallbackArt,
      body: <Hours hourSeconds={recap.hourSeconds} {...hours} />,
    });
  if (recap.moods.length > 0)
    slides.push({ key: "mood", art: fallbackArt, body: <Mood recap={recap} /> });
  if (recap.soundOfMonth) {
    slides.push({
      key: "sound",
      art: trackArt(recap.soundOfMonth) ?? fallbackArt,
      body: <Sound track={recap.soundOfMonth} />,
    });
  }
  if (recap.newArtists != null)
    slides.push({
      key: "discoveries",
      art: fallbackArt,
      body: <Discoveries count={recap.newArtists} picks={recap.newArtistPicks} />,
    });

  slides.push({
    key: "summary",
    art: fallbackArt,
    body: <Summary recap={recap} month={month} onClose={onClose} onRestart={onRestart} />,
  });

  return slides;
}

function Eyebrow({ children }: { children: ReactNode }) {
  return <p className="text-sm font-semibold tracking-wide text-primary uppercase">{children}</p>;
}

const huge =
  "font-display text-[clamp(3rem,13vw,5.5rem)] leading-[0.95] font-semibold tracking-tight text-balance pb-[0.1em]";
const big =
  "font-display text-[clamp(2rem,8vw,3.25rem)] leading-tight font-semibold tracking-tight text-balance";

function Intro({ recap, month }: { recap: Recap; month: string }) {
  const t = useT();
  const covers = recap.topTracks
    .map((item) => item.track)
    .filter((item) => item.hasCover)
    .slice(0, 4);

  return (
    <>
      {covers.length > 0 && (
        <div className="flex -space-x-10">
          {covers.map((item, position) => (
            <span
              key={item.id}
              className="size-28 overflow-hidden rounded-xs shadow-art max-sm:size-24"
              style={{ transform: `rotate(${(position - (covers.length - 1) / 2) * 6}deg)` }}
            >
              <TrackCover track={item} />
            </span>
          ))}
        </div>
      )}
      <h1 className={huge}>{t("recap.intro.title", { month })}</h1>
      <p className="text-xl text-muted-foreground">{t("recap.intro.note")}</p>
    </>
  );
}

function Time({ recap }: { recap: Recap }) {
  const t = useT();
  const format = useFormat();
  const delta =
    recap.previousListenedSeconds == null
      ? null
      : recap.listenedSeconds - recap.previousListenedSeconds;

  return (
    <>
      <Eyebrow>{t("recap.time.eyebrow")}</Eyebrow>
      <p className={cn(huge, "tabular-nums")}>{format.totalDuration(recap.listenedSeconds)}</p>
      <p className="text-2xl text-muted-foreground">{t("recap.time.ofMusic")}</p>
      <ul className="flex flex-col gap-1 text-lg">
        <li>{t("recap.plays", { count: recap.plays })}</li>
        <li>{t("count.tracks", { count: recap.distinctTracks })}</li>
        <li>{t("count.artists", { count: recap.distinctArtists })}</li>
      </ul>
      {delta !== null && Math.abs(delta) >= 60 && (
        <p className="text-muted-foreground">
          {t(delta > 0 ? "recap.time.more" : "recap.time.less", {
            duration: format.totalDuration(Math.abs(delta)),
          })}
        </p>
      )}
    </>
  );
}

function TopArtist({ recap }: { recap: Recap }) {
  const t = useT();
  const format = useFormat();
  const [lead, ...rest] = recap.topArtists;

  return (
    <>
      <Eyebrow>{t("recap.artist.eyebrow")}</Eyebrow>
      <span className="size-[min(16rem,55vw)] overflow-hidden rounded-full shadow-art">
        <ArtistCover artist={lead.artist} variant="full" />
      </span>
      <h2 className={big}>{lead.artist.name}</h2>
      <p className="text-lg text-muted-foreground">
        {t("recap.artist.together", { duration: format.totalDuration(lead.listenedSeconds) })},{" "}
        {t("recap.plays", { count: lead.plays })}
      </p>
      <Runners title={t("recap.alsoTop")} names={rest.map((item) => item.artist.name)} />
    </>
  );
}

function TopTrack({ recap }: { recap: Recap }) {
  const t = useT();
  const [lead, ...rest] = recap.topTracks;
  const tracks = recap.topTracks.map((item) => item.track);

  return (
    <>
      <Eyebrow>{t("recap.track.eyebrow")}</Eyebrow>
      <PlayableCover track={lead.track} context={tracks} />
      <div>
        <h2 className={big}>{lead.track.title}</h2>
        <p className="text-lg text-muted-foreground">
          {formatArtists(lead.track)} · {t("recap.times", { count: lead.plays })}
        </p>
      </div>
      <Runners title={t("recap.alsoTop")} names={rest.map((item) => item.track.title)} />
    </>
  );
}

function Runners({ title, names }: { title: string; names: string[] }) {
  if (names.length === 0) return null;

  return (
    <div className="flex flex-col gap-1">
      <p className="text-sm text-muted-foreground">{title}</p>
      <ol className="flex flex-col gap-0.5 text-lg">
        {names.map((name, position) => (
          <li key={`${position}-${name}`} className="truncate">
            <span className="mr-3 text-faint tabular-nums">{position + 2}</span>
            {name}
          </li>
        ))}
      </ol>
    </div>
  );
}

function PlayableCover({ track, context }: { track: Track; context: Track[] }) {
  const t = useT();
  const { playTrack, soundingNow } = usePlayback();
  const playing = soundingNow(track.id);

  return (
    <button
      type="button"
      onClick={() => playTrack(track, context)}
      aria-label={`${playing ? t("action.pause") : t("action.play")}: ${track.title}`}
      className="group relative size-[min(16rem,55vw)] overflow-hidden rounded-xs shadow-art"
    >
      <TrackCover track={track} variant="full" />
      <span className="absolute right-3 bottom-3 grid size-14 place-items-center rounded-full bg-action text-action-foreground shadow-pop transition-transform duration-150 ease-brand group-hover:scale-105">
        {playing ? (
          <PauseIcon size={24} />
        ) : (
          <PlayIcon size={24} className="translate-x-px fill-current" />
        )}
      </span>
    </button>
  );
}

function BusiestDay({ recap, peak }: { recap: Recap; peak: { day: number; seconds: number } }) {
  const t = useT();
  const { locale } = useI18n();
  const format = useFormat();
  const max = peak.seconds;
  const blanks = leadingBlanks(recap.year, recap.month);

  const date = new Date(recap.year, recap.month - 1, peak.day);
  const weekday = date.toLocaleDateString(locale, { weekday: "long" });

  return (
    <>
      <Eyebrow>{t("recap.day.eyebrow")}</Eyebrow>
      <h2 className={big}>{date.toLocaleDateString(locale, { day: "numeric", month: "long" })}</h2>
      <p className="text-lg text-muted-foreground">
        {capitalize(weekday)} ·{" "}
        {t("recap.day.note", { duration: format.totalDuration(peak.seconds) })}
      </p>
      <div className="grid max-w-sm grid-cols-7 gap-1.5" aria-hidden="true">
        {Array.from({ length: blanks }, (_, position) => (
          <span key={`blank-${position}`} />
        ))}
        {recap.daySeconds.map((seconds, position) => {
          const level = intensity(seconds, max);

          return (
            <span
              key={position}
              className={cn(
                "grid aspect-square place-items-center rounded-sm bg-foreground/8 text-2xs tabular-nums",
                level > 0.6 ? "text-primary-foreground" : "text-muted-foreground",
                position + 1 === peak.day && "ring-2 ring-foreground",
              )}
              style={
                level > 0
                  ? {
                      backgroundColor: `color-mix(in oklab, var(--color-primary) ${Math.round(20 + level * 80)}%, transparent)`,
                    }
                  : undefined
              }
            >
              {position + 1}
            </span>
          );
        })}
      </div>
    </>
  );
}

function Hours({
  hourSeconds,
  daypart,
  share,
}: {
  hourSeconds: number[];
  daypart: Daypart;
  share: number;
}) {
  const t = useT();
  const [from, to] = DAYPART_HOURS[daypart];
  const max = Math.max(...hourSeconds);

  return (
    <>
      <Eyebrow>{t("recap.hours.eyebrow")}</Eyebrow>
      <h2 className={big}>{t(`recap.hours.${daypart}`)}</h2>
      <p className="text-lg text-muted-foreground">
        {t("recap.hours.note", {
          share: Math.round(share * 100),
          from: String(from).padStart(2, "0"),
          to: String(to).padStart(2, "0"),
        })}
      </p>
      <div className="flex flex-col gap-2" aria-hidden="true">
        <div className="flex h-40 items-end gap-1">
          {hourSeconds.map((seconds, hour) => (
            <span
              key={hour}
              className={cn(
                "flex-1 rounded-t-xs",
                daypartOf(hour) === daypart ? "bg-primary" : "bg-foreground/25",
              )}
              style={{ height: `${Math.max(3, max > 0 ? (seconds / max) * 100 : 0)}%` }}
            />
          ))}
        </div>
        <div className="grid grid-cols-4 text-xs text-faint tabular-nums">
          {["00", "06", "12", "18"].map((hour) => (
            <span key={hour}>{hour}</span>
          ))}
        </div>
      </div>
    </>
  );
}

function Mood({ recap }: { recap: Recap }) {
  const t = useT();
  const player = usePlayerActions();
  const start = useMutation({ mutationFn: (mood: string) => player.startRadio(null, mood) });
  const lead = recap.moods[0];

  return (
    <>
      <Eyebrow>{t("recap.mood.eyebrow")}</Eyebrow>
      <MoodIcon mood={lead.key} className="size-16 text-primary" strokeWidth={1.75} />
      <h2 className={huge}>{moodLabel(lead.key, t)}</h2>
      <ul className="flex flex-col gap-2">
        {recap.moods.slice(0, 4).map((mood) => (
          <li
            key={mood.key}
            className="grid grid-cols-[7rem_minmax(0,1fr)_3rem] items-center gap-3"
          >
            <span className="truncate">{moodLabel(mood.key, t)}</span>
            <span className="h-2 overflow-hidden rounded-full bg-foreground/15">
              <span
                className="block h-full rounded-full bg-primary"
                style={{ width: `${mood.share * 100}%` }}
              />
            </span>
            <span className="text-right text-muted-foreground tabular-nums">
              {Math.round(mood.share * 100)}%
            </span>
          </li>
        ))}
      </ul>
      <div>
        <Button variant="primary" onClick={() => start.mutate(lead.key)} disabled={start.isPending}>
          <MoodIcon mood={lead.key} size={16} />
          {t("recap.mood.radio", { mood: moodLabel(lead.key, t) })}
        </Button>
      </div>
    </>
  );
}

function Sound({ track }: { track: Track }) {
  const t = useT();

  return (
    <>
      <Eyebrow>{t("recap.sound.eyebrow")}</Eyebrow>
      <PlayableCover track={track} context={[track]} />
      <div>
        <h2 className={big}>{track.title}</h2>
        <p className="text-lg text-muted-foreground">{formatArtists(track)}</p>
      </div>
      <p className="text-muted-foreground">{t("recap.sound.note")}</p>
    </>
  );
}

function Discoveries({ count, picks }: { count: number; picks: Artist[] }) {
  const t = useT();

  return (
    <>
      <Eyebrow>{t("recap.discoveries.eyebrow")}</Eyebrow>
      {count === 0 ? (
        <>
          <h2 className={big}>{t("recap.discoveries.none")}</h2>
          <p className="text-lg text-muted-foreground">{t("recap.discoveries.noneNote")}</p>
        </>
      ) : (
        <>
          <p className={cn(huge, "tabular-nums")}>{count}</p>
          <p className="text-2xl text-muted-foreground">{t("recap.newArtists", { count })}</p>
        </>
      )}
      {picks.length > 0 && (
        <ul className="flex flex-wrap gap-5">
          {picks.map((artist) => (
            <li key={artist.id} className="flex w-24 flex-col items-center gap-2 text-center">
              <span className="size-24 overflow-hidden rounded-full">
                <ArtistCover artist={artist} />
              </span>
              <span className="line-clamp-2 text-sm">{artist.name}</span>
            </li>
          ))}
        </ul>
      )}
    </>
  );
}

function Summary({
  recap,
  month,
  onClose,
  onRestart,
}: {
  recap: Recap;
  month: string;
  onClose: () => void;
  onRestart: () => void;
}) {
  const t = useT();
  const format = useFormat();
  const mood = recap.moods[0];

  const facts = [
    [t("recap.summary.artist"), recap.topArtists[0]?.artist.name],
    [t("recap.summary.track"), recap.topTracks[0]?.track.title],
    [t("recap.summary.mood"), mood ? moodLabel(mood.key, t) : null],
    [t("recap.summary.genre"), recap.topGenre],
  ].filter((fact): fact is [string, string] => Boolean(fact[1]));

  return (
    <>
      <Eyebrow>{t("recap.summary.eyebrow", { month: capitalize(month) })}</Eyebrow>
      <dl className="grid grid-cols-2 gap-x-6 gap-y-5">
        <div className="col-span-2">
          <dt className="text-sm text-muted-foreground">{t("recap.summary.time")}</dt>
          <dd className="font-display text-5xl font-semibold tracking-tight tabular-nums">
            {format.totalDuration(recap.listenedSeconds)}
          </dd>
        </div>
        {facts.map(([label, value]) => (
          <div key={label} className="min-w-0">
            <dt className="text-sm text-muted-foreground">{label}</dt>
            <dd className="line-clamp-2 font-display text-2xl font-semibold tracking-tight">
              {value}
            </dd>
          </div>
        ))}
      </dl>
      <div className="flex flex-wrap gap-2.5">
        <Button variant="primary" onClick={onClose}>
          {t("recap.close")}
        </Button>
        <Button onClick={onRestart}>{t("recap.again")}</Button>
      </div>
    </>
  );
}
