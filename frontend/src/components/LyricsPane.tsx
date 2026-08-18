"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { useReducedMotion } from "motion/react";
import { api } from "@/lib/api";
import { cn } from "@/lib/cn";
import type { Lyrics, Track } from "@/lib/types";
import { usePlayerProgress } from "@/contexts/PlayerContext";
import { useT } from "@/contexts/I18nContext";

export function LyricsPane({ track }: { track: Track }) {
  const t = useT();
  const { position } = usePlayerProgress();
  const reduceMotion = useReducedMotion();

  const [lyrics, setLyrics] = useState<Lyrics | null>(null);

  const [state, setState] = useState<"loading" | "ready" | "failed">(
    track.hasLyrics ? "loading" : "ready",
  );

  useEffect(() => {
    if (!track.hasLyrics) return;

    let active = true;

    api
      .lyrics(track.id)
      .then((found) => {
        if (!active) return;

        setLyrics(found);
        setState("ready");
      })
      .catch(() => {
        if (active) setState("failed");
      });

    return () => {
      active = false;
    };
  }, [track.id, track.hasLyrics]);

  const lines = useMemo(() => lyrics?.lines ?? [], [lyrics]);

  const current = useMemo(() => {
    if (lines.length === 0) return -1;

    const at = position * 1000;
    let index = -1;

    for (let i = 0; i < lines.length && lines[i].at <= at; i += 1) index = i;

    return index;
  }, [lines, position]);

  const note = "py-8 text-center text-muted-foreground";

  if (state === "loading") return <p className={note}>{t("common.loading")}</p>;
  if (state === "failed") return <p className={note}>{t("lyrics.failed")}</p>;
  if (!lyrics) return <p className={note}>{t("lyrics.none")}</p>;

  if (lines.length === 0) {
    return (
      <p className="text-center leading-[1.7] whitespace-pre-wrap text-muted-foreground">
        {lyrics.plain}
      </p>
    );
  }

  return (
    <ol className="flex flex-col gap-2 py-[40%] text-center">
      {lines.map((line, index) => (
        <LyricLine
          key={`${line.at}-${index}`}
          text={line.text}
          active={index === current}
          smooth={!reduceMotion}
        />
      ))}
    </ol>
  );
}

function LyricLine({ text, active, smooth }: { text: string; active: boolean; smooth: boolean }) {
  const element = useRef<HTMLLIElement | null>(null);

  useEffect(() => {
    if (!active) return;

    element.current?.scrollIntoView({
      behavior: smooth ? "smooth" : "auto",
      block: "center",
    });
  }, [active, smooth]);

  return (
    <li
      ref={element}
      aria-current={active}
      className={cn(
        "text-lg leading-snug font-semibold transition-[color,transform] duration-150 ease-brand motion-reduce:transition-none",
        active ? "scale-[1.04] text-foreground motion-reduce:scale-100" : "text-faint",
      )}
    >
      {text || " "}
    </li>
  );
}
