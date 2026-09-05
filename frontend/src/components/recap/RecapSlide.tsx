// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import type { ReactNode } from "react";
import { motion, useReducedMotion } from "motion/react";
import { cn } from "@/lib/cn";
import { EASE } from "@/lib/motion";
import type { RecapSlide as Slide } from "@/lib/recapStory";
import { useFormat } from "@/lib/useFormat";
import { useI18n, useT } from "@/contexts/I18nContext";
import { monthLabel, type MonthlyRecap } from "@/lib/recap";
import { ArtistCover, TrackCover } from "@/components/Cover";
import { Overline } from "@/components/ui/label";

/**
 * Один экран истории.
 *
 * Реплики появляются по очереди, а не разом: пауза между надписью и числом — это и есть
 * подача. Всё движение отменяется при `prefers-reduced-motion` — тогда слайд просто есть.
 */
export function RecapSlide({ slide, data }: { slide: Slide; data: MonthlyRecap }) {
  const t = useT();
  const { locale } = useI18n();
  const format = useFormat();
  const reduceMotion = useReducedMotion();

  const month = monthLabel(data.month, locale);

  switch (slide.kind) {
    case "intro":
      return (
        <Lines reduceMotion={reduceMotion}>
          <Eyebrow>{t("recap.title")}</Eyebrow>
          <Headline>{month}</Headline>
          <Note>{t("recap.slide.introNote")}</Note>
        </Lines>
      );

    case "time":
      return (
        <Lines reduceMotion={reduceMotion}>
          <Eyebrow>{t("recap.slide.timeLabel")}</Eyebrow>
          <Headline>{format.totalDuration(data.listenedSeconds)}</Headline>
          <Note>
            {slide.changePercent === null
              ? t("stats.playCount", { count: data.plays })
              : t(slide.changePercent >= 0 ? "recap.slide.timeUp" : "recap.slide.timeDown", {
                  percent: Math.abs(slide.changePercent),
                })}
          </Note>
        </Lines>
      );

    case "topTrack":
      return (
        <Lines reduceMotion={reduceMotion}>
          <Eyebrow>{t("recap.slide.topTrackLabel")}</Eyebrow>
          <Art className="rounded-lg">
            <TrackCover track={slide.entry.track} className="size-full rounded-none" />
          </Art>
          <Headline className="text-title">{slide.entry.track.title}</Headline>
          <Note>
            {slide.entry.track.artistName} · {t("stats.playCount", { count: slide.entry.plays })}
          </Note>
        </Lines>
      );

    case "topTracks":
      return (
        <Lines reduceMotion={reduceMotion}>
          <Eyebrow>{t("recap.topTracks")}</Eyebrow>
          <ol className="flex flex-col gap-3">
            {slide.entries.map((entry, index) => (
              <li key={entry.track.id} className="flex items-baseline gap-4">
                <span className="w-6 text-right text-title font-bold text-primary tabular-nums">
                  {index + 1}
                </span>
                <span className="min-w-0">
                  <span className="block truncate text-title font-semibold">
                    {entry.track.title}
                  </span>
                  <span className="block truncate text-sm opacity-70">
                    {entry.track.artistName}
                  </span>
                </span>
              </li>
            ))}
          </ol>
        </Lines>
      );

    case "topArtist":
      return (
        <Lines reduceMotion={reduceMotion}>
          <Eyebrow>{t("recap.slide.topArtistLabel")}</Eyebrow>
          <Art className="rounded-full">
            <ArtistCover
              artist={{
                id: slide.entry.id,
                name: slide.entry.name,
                hasImage: slide.entry.hasImage,
              }}
              className="size-full"
            />
          </Art>
          <Headline>{slide.entry.name}</Headline>
          <Note>{format.totalDuration(slide.entry.listenedSeconds)}</Note>
        </Lines>
      );

    case "discoveries":
      return (
        <Lines reduceMotion={reduceMotion}>
          <Eyebrow>{t("recap.discoveries")}</Eyebrow>
          <Headline className="text-title">
            {slide.entries.map((entry) => entry.name).join(" · ")}
          </Headline>
          <Note>{t("recap.discoveryHint")}</Note>
        </Lines>
      );

    case "genre":
      return (
        <Lines reduceMotion={reduceMotion}>
          <Eyebrow>{t("recap.slide.genreLabel")}</Eyebrow>
          <Headline>{slide.genre}</Headline>
          {/* Прошлый жанр печатается, только если он действительно был и действительно другой —
              решение принято в recapSlides, здесь остаётся простая проверка на null. */}
          <Note>
            {slide.previous
              ? t("recap.genreShift", { from: slide.previous, to: slide.genre })
              : t("recap.slide.genreSteady")}
          </Note>
        </Lines>
      );

    case "finale":
      return (
        <Lines reduceMotion={reduceMotion}>
          <Eyebrow>{t("recap.slide.finaleLabel")}</Eyebrow>
          <Headline>{month}</Headline>
          <Note>
            {format.totalDuration(data.listenedSeconds)} ·{" "}
            {t("count.tracks", { count: data.uniqueTracks })} ·{" "}
            {t("count.artists", { count: data.uniqueArtists })}
          </Note>
        </Lines>
      );
  }
}

function Lines({ reduceMotion, children }: { reduceMotion: boolean | null; children: ReactNode }) {
  return (
    <motion.div
      className="flex flex-col items-start gap-5 text-balance"
      initial={reduceMotion ? false : "hidden"}
      animate="shown"
      variants={{ shown: { transition: { staggerChildren: 0.12, delayChildren: 0.1 } } }}
    >
      {children}
    </motion.div>
  );
}

const line = {
  hidden: { opacity: 0, y: 18 },
  shown: { opacity: 1, y: 0, transition: { duration: 0.45, ease: EASE } },
};

function Art({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <motion.div
      variants={line}
      className={cn("size-56 overflow-hidden shadow-hero max-md:size-40", className)}
    >
      {children}
    </motion.div>
  );
}

function Eyebrow({ children }: { children: ReactNode }) {
  return (
    <motion.div variants={line}>
      <Overline className="text-primary">{children}</Overline>
    </motion.div>
  );
}

function Headline({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <motion.h2
      variants={line}
      className={cn("max-w-[16ch] text-display font-bold tabular-nums", className)}
    >
      {children}
    </motion.h2>
  );
}

function Note({ children }: { children: ReactNode }) {
  return (
    <motion.p variants={line} className="max-w-[36ch] text-lg opacity-80 max-md:text-base">
      {children}
    </motion.p>
  );
}
