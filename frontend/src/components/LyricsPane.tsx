// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import dynamic from "next/dynamic";
import { Fragment, memo, useEffect, useMemo, useRef, useState } from "react";
import { useReducedMotion } from "motion/react";
import { api } from "@/lib/api";
import { cn } from "@/lib/cn";
import { activeLineAt } from "@/lib/lyrics";
import type { Lyrics, Track } from "@/lib/types";
import { usePlayerProgress, usePlayerState } from "@/contexts/PlayerContext";
import { useAuth } from "@/contexts/AuthContext";
import { useT } from "@/contexts/I18nContext";
import { EditIcon, LyricsIcon } from "./Icons";
import { EmptyState } from "./EmptyState";
import { Button } from "./ui/button";

const EditLyricsDialog = dynamic(() =>
  import("./EditLyricsDialog").then((m) => m.EditLyricsDialog),
);

export function LyricsPane({
  track,
  onSeek,
  onLyricsKnown,
}: {
  track: Track;
  onSeek: (seconds: number) => void;
  onLyricsKnown: (hasLyrics: boolean) => void;
}) {
  const t = useT();
  const { position, getPosition } = usePlayerProgress();
  const { isPlaying } = usePlayerState();
  const { isAdmin } = useAuth();
  const reduceMotion = useReducedMotion();
  const [editing, setEditing] = useState(false);

  const [loaded, setLoaded] = useState<{ id: string; lyrics: Lyrics | null } | null>(null);
  const [failedId, setFailedId] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    const id = track.id;

    api
      .lyrics(id)
      .then((found) => {
        if (active) setLoaded({ id, lyrics: found ?? null });
      })
      .catch(() => {
        if (active) setFailedId(id);
      });

    return () => {
      active = false;
    };
  }, [track.id]);

  const lyrics = loaded?.id === track.id ? loaded.lyrics : null;

  useEffect(() => {
    if (loaded?.id !== track.id) return;

    const has = loaded.lyrics !== null;
    if (has !== track.hasLyrics) onLyricsKnown(has);
  }, [loaded, track.id, track.hasLyrics, onLyricsKnown]);

  const lines = useMemo(() => lyrics?.lines ?? [], [lyrics]);

  const current = activeLineAt(lines, position * 1000);

  const [browsing, setBrowsing] = useState(false);

  const note = "py-8 text-center text-muted-foreground";
  const ready = loaded?.id === track.id;

  return (
    <>
      {isAdmin && ready && (
        <div className="sticky top-0 z-1 flex justify-end">
          <Button
            variant="ghost"
            size="icon"
            className="size-10"
            onClick={() => setEditing(true)}
            aria-label={t("lyrics.edit")}
            title={t("lyrics.edit")}
          >
            <EditIcon size={18} />
          </Button>
        </div>
      )}

      {editing && (
        <EditLyricsDialog
          track={track}
          lyrics={lyrics}
          onClose={() => setEditing(false)}
          onSaved={(saved) => setLoaded({ id: track.id, lyrics: saved })}
        />
      )}

      {body()}
    </>
  );

  function body() {
    if (failedId === track.id) {
      return <EmptyState bare icon={<LyricsIcon size={22} />} title={t("lyrics.failed")} />;
    }
    if (!ready) return <p className={note}>{t("common.loading")}</p>;
    if (!lyrics) {
      return <EmptyState bare icon={<LyricsIcon size={22} />} title={t("lyrics.none")} />;
    }

    if (lines.length === 0) {
      return (
        <p className="text-center leading-[1.7] whitespace-pre-wrap text-muted-foreground">
          {lyrics.plain}
        </p>
      );
    }

    return (
      <ol
        className="flex flex-col gap-10 pt-[22vh] pb-[78vh] text-center md:gap-14"
        onPointerEnter={() => setBrowsing(true)}
        onPointerLeave={() => setBrowsing(false)}
        onFocus={() => setBrowsing(true)}
        onBlur={() => setBrowsing(false)}
      >
        {lines[0].at >= INTRO_MIN && (
          <LyricsIntro
            startsAt={lines[0].at}
            getPosition={getPosition}
            active={current === -1}
            dim={visible(-1 - current, browsing)}
            animate={!reduceMotion}
            playing={isPlaying}
          />
        )}

        {lines.map((line, index) => {
          const next = lines[index + 1];
          const gap = next ? next.at - line.at : 0;

          return (
            <Fragment key={`${line.at}-${index}`}>
              <LyricLine
                text={line.text}
                active={index === current}
                dim={visible(index - current, browsing)}
                smooth={!reduceMotion}
                onSeek={() => onSeek(line.at / 1000)}
              />

              {gap >= GAP_MIN && (
                <LyricsIntro
                  startsAt={next.at}
                  getPosition={getPosition}
                  active={index === current}
                  dim={visible(index - current, browsing)}
                  animate={!reduceMotion}
                  playing={isPlaying}
                  showFrom={GAP_SHOW_FROM}
                  scroll={false}
                />
              )}
            </Fragment>
          );
        })}
      </ol>
    );
  }
}

const PASSED = [1, 0.12];
const UPCOMING = [1, 0.5, 0.34, 0.18];

const dim = (distance: number) => (distance < 0 ? PASSED : UPCOMING)[Math.abs(distance)] ?? 0;

const BROWSING_FLOOR = 0.4;

const visible = (distance: number, browsing: boolean) =>
  browsing ? Math.max(dim(distance), BROWSING_FLOOR) : dim(distance);

const INTRO_MIN = 3000;

const INTRO_COUNTDOWN = 3000;

const GAP_MIN = 10_000;

const GAP_SHOW_FROM = 5000;

const clamp01 = (value: number) => Math.min(1, Math.max(0, value));

function LyricsIntro({
  startsAt,
  getPosition,
  active,
  dim: opacity,
  animate,
  playing,
  showFrom = Number.POSITIVE_INFINITY,
  scroll = true,
}: {
  startsAt: number;
  getPosition: () => number;
  active: boolean;
  dim: number;
  animate: boolean;
  playing: boolean;
  showFrom?: number;
  scroll?: boolean;
}) {
  const element = useRef<HTMLLIElement | null>(null);
  const dots = useRef<(HTMLSpanElement | null)[]>([]);

  useEffect(() => {
    if (!active || !scroll) return;

    element.current?.scrollIntoView({ behavior: animate ? "smooth" : "auto", block: "start" });
  }, [active, animate, scroll]);

  useEffect(() => {
    if (!animate || !active || !playing) return;

    const nodes = dots.current;

    let frame = requestAnimationFrame(function tick() {
      const now = getPosition() * 1000;
      const left = startsAt - now;
      const filled = clamp01((INTRO_COUNTDOWN - left) / INTRO_COUNTDOWN) * nodes.length;
      const shown = left <= showFrom;

      nodes.forEach((dot, index) => {
        if (!dot) return;

        const breath = 0.35 + 0.2 * Math.sin(now / 420 - index * 0.9);
        const level = shown ? Math.max(breath, clamp01(filled - index)) : 0;

        dot.style.opacity = `${level}`;
        dot.style.transform = `scale(${0.75 + level * 0.45})`;
      });

      frame = requestAnimationFrame(tick);
    });

    return () => {
      cancelAnimationFrame(frame);

      nodes.forEach((dot) => {
        if (!dot) return;

        dot.style.opacity = "";
        dot.style.transform = "";
      });
    };
  }, [active, animate, getPosition, playing, startsAt, showFrom]);

  return (
    <li
      ref={element}
      aria-hidden
      style={{ opacity }}
      className="flex scroll-mt-[22vh] justify-center gap-3 py-4 transition-opacity duration-300 ease-brand motion-reduce:transition-none"
    >
      {[0, 1, 2].map((index) => (
        <span
          key={index}
          ref={(node) => {
            dots.current[index] = node;
          }}
          className="size-3 rounded-full bg-foreground opacity-40 sm:size-4"
        />
      ))}
    </li>
  );
}

/**
 * Мемоизирована: панель подписана на прогресс и перерисовывается четыре раза в секунду,
 * а на песне в шестьдесят строк это две с половиной сотни рендеров в секунду ради смены
 * одного `active`. Пропсы здесь примитивные, поэтому сравнение по умолчанию и годится.
 */
const LyricLine = memo(function LyricLine({
  text,
  active,
  dim,
  smooth,
  onSeek,
}: {
  text: string;
  active: boolean;
  dim: number;
  smooth: boolean;
  onSeek: () => void;
}) {
  const t = useT();
  const element = useRef<HTMLLIElement | null>(null);

  useEffect(() => {
    if (!active) return;

    element.current?.scrollIntoView({
      behavior: smooth ? "smooth" : "auto",
      block: "start",
    });
  }, [active, smooth]);

  const styling = cn(
    "block w-full text-2xl leading-[1.15] font-bold tracking-tight text-balance text-foreground transition-transform duration-300 ease-brand sm:text-4xl md:text-5xl motion-reduce:transition-none",
    active && "scale-[1.02] motion-reduce:scale-100",
  );

  return (
    <li
      ref={element}
      aria-current={active}
      style={{ opacity: dim }}
      className="scroll-mt-[22vh] transition-opacity duration-300 ease-brand motion-reduce:transition-none"
    >
      {text ? (
        <button
          type="button"
          onClick={onSeek}
          aria-label={t("lyrics.seekTo", { line: text })}
          className={cn(
            styling,
            "rounded-2xl px-3 py-1 hover:bg-foreground/10 focus-visible:bg-foreground/10 focus-visible:outline-none",
          )}
        >
          {text}
        </button>
      ) : (
        <span className={styling}>{" "}</span>
      )}
    </li>
  );
});
