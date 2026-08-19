// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { useReducedMotion } from "motion/react";
import { api } from "@/lib/api";
import { cn } from "@/lib/cn";
import type { LyricLine as Line, Lyrics, Track } from "@/lib/types";
import { usePlayerProgress } from "@/contexts/PlayerContext";
import { useT } from "@/contexts/I18nContext";

export function LyricsPane({ track, onSeek }: { track: Track; onSeek: (seconds: number) => void }) {
  const t = useT();
  const { getPosition } = usePlayerProgress();
  const reduceMotion = useReducedMotion();

  // Загруженное держится вместе с id трека, а не сбрасывается на его смену: панель при смене трека
  // не размонтируется, и без этой привязки она успела бы показать текст предыдущей песни.
  const [loaded, setLoaded] = useState<{ id: string; lyrics: Lyrics | null } | null>(null);
  const [failedId, setFailedId] = useState<string | null>(null);

  // Флаг `hasLyrics` здесь намеренно не спрашивается. Он снимается в момент, когда трек попал в
  // выдачу, а очередь переживает перезагрузку целиком, вместе со своими копиями треков — поэтому
  // текст, появившийся у трека позже, для уже стоящего в очереди навсегда остался бы невидимым.
  // Сам запрос честнее: когда текста нет, эндпоинт отвечает 204, и это приходит как undefined.
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

  const lines = useMemo(() => lyrics?.lines ?? [], [lyrics]);

  const [current, setCurrent] = useState(-1);

  // Пока курсор (или фокус) в тексте, погасшие строки приподнимаются: без этого в них некуда
  // целиться — место в потоке они занимают, а видно их не было бы.
  const [browsing, setBrowsing] = useState(false);

  // Строка должна загораться ровно тогда, когда её начинают петь, поэтому время опрашивается
  // покадрово, а не берётся из `position`: он приходит по timeupdate и отстаёт до четверти секунды,
  // а строки бывают и в полторы секунды длиной. Перерисовка при этом остаётся редкой — состояние
  // меняется только на смене строки, а не на каждом кадре.
  useEffect(() => {
    if (lines.length === 0) {
      setCurrent(-1);
      return;
    }

    let frame = requestAnimationFrame(function tick() {
      setCurrent(activeLineAt(lines, getPosition() * 1000));
      frame = requestAnimationFrame(tick);
    });

    return () => cancelAnimationFrame(frame);
  }, [lines, getPosition]);

  const note = "py-8 text-center text-muted-foreground";

  if (failedId === track.id) return <p className={note}>{t("lyrics.failed")}</p>;
  if (loaded?.id !== track.id) return <p className={note}>{t("common.loading")}</p>;
  if (!lyrics) return <p className={note}>{t("lyrics.none")}</p>;

  if (lines.length === 0) {
    return (
      <p className="text-center leading-[1.7] whitespace-pre-wrap text-muted-foreground">
        {lyrics.plain}
      </p>
    );
  }

  return (
    // Верхний отступ равен той высоте, на которой держится поющаяся строка, нижний — всему
    // остатку экрана: без него последние строки не смогли бы доехать до своего места.
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
        />
      )}

      {lines.map((line, index) => (
        <LyricLine
          key={`${line.at}-${index}`}
          text={line.text}
          active={index === current}
          dim={visible(index - current, browsing)}
          smooth={!reduceMotion}
          onSeek={() => onSeek(line.at / 1000)}
        />
      ))}
    </ol>
  );
}

/** Последняя строка, чей таймкод уже наступил, или -1, пока не спели ни одной. */
function activeLineAt(lines: readonly Line[], at: number) {
  let index = -1;

  for (let i = 0; i < lines.length && lines[i].at <= at; i += 1) index = i;

  return index;
}

// Затухание намеренно несимметричное. Поющаяся строка стоит первой из читаемых: от спетого
// остаётся только призрак предыдущей строки, а впереди видно три — ровно столько, чтобы успевать
// вести взглядом, но не читать всю песню разом. Дальше по обе стороны — ноль, то есть строка
// занимает своё место в потоке, но не отвлекает.
const PASSED = [1, 0.12];
const UPCOMING = [1, 0.5, 0.34, 0.18];

const dim = (distance: number) => (distance < 0 ? PASSED : UPCOMING)[Math.abs(distance)] ?? 0;

// Порог, ниже которого строку не опускают, пока по тексту водят курсором: по ней надо попасть.
const BROWSING_FLOOR = 0.4;

const visible = (distance: number, browsing: boolean) =>
  browsing ? Math.max(dim(distance), BROWSING_FLOOR) : dim(distance);

// Вступление короче этого точками не отмечаем — они бы только мигнули.
const INTRO_MIN = 3000;

// За столько до первой строки точки начинают заполняться слева направо.
const INTRO_COUNTDOWN = 3000;

const clamp01 = (value: number) => Math.min(1, Math.max(0, value));

/**
 * Три точки на месте ещё не начавшегося текста. Пока до вступления далеко, они просто дышат;
 * за несколько секунд до первой строки — загораются по очереди, отсчитывая её приход.
 */
function LyricsIntro({
  startsAt,
  getPosition,
  active,
  dim: opacity,
  animate,
}: {
  startsAt: number;
  getPosition: () => number;
  active: boolean;
  dim: number;
  animate: boolean;
}) {
  const element = useRef<HTMLLIElement | null>(null);
  const dots = useRef<(HTMLSpanElement | null)[]>([]);

  useEffect(() => {
    if (!active) return;

    // Та же посадка, что и у строк: точки стоят ровно там, где через секунду загорится первая.
    element.current?.scrollIntoView({ behavior: animate ? "smooth" : "auto", block: "start" });
  }, [active, animate]);

  // Точки анимируются записью в DOM, а не через состояние: кадров шестьдесят в секунду, и гонять
  // на каждый из них перерисовку списка строк было бы расточительно. Фаза считается от времени
  // трека, поэтому на паузе они замирают вместе с музыкой.
  useEffect(() => {
    if (!animate || !active) return;

    const nodes = dots.current;

    let frame = requestAnimationFrame(function tick() {
      const now = getPosition() * 1000;
      const filled = clamp01((INTRO_COUNTDOWN - (startsAt - now)) / INTRO_COUNTDOWN) * nodes.length;

      nodes.forEach((dot, index) => {
        if (!dot) return;

        const breath = 0.35 + 0.2 * Math.sin(now / 420 - index * 0.9);
        const level = Math.max(breath, clamp01(filled - index));

        dot.style.opacity = `${level}`;
        dot.style.transform = `scale(${0.75 + level * 0.45})`;
      });

      frame = requestAnimationFrame(tick);
    });

    return () => {
      cancelAnimationFrame(frame);

      // Инлайновые стили перебивают классы, так что уступленное место надо освободить — иначе
      // точки застынут в той яркости, на которой их застала первая строка.
      nodes.forEach((dot) => {
        if (!dot) return;

        dot.style.opacity = "";
        dot.style.transform = "";
      });
    };
  }, [active, animate, getPosition, startsAt]);

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

function LyricLine({
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

    // `start`, а не `center`: поющаяся строка должна стоять первой, а место под ней — уходить
    // тому, что вот-вот прозвучит. На нужной высоте её держит scroll-mt.
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
      {/* Пустыми строками в LRC размечают проигрыши: прыгать по ним некуда, а кнопку без названия
          скринридер прочитал бы как пустую — поэтому они остаются просто отбивкой. */}
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
}
