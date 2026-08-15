"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { useReducedMotion } from "motion/react";
import { api } from "@/lib/api";
import type { Lyrics, Track } from "@/lib/types";
import { usePlayerProgress } from "@/contexts/PlayerContext";
import { useT } from "@/contexts/I18nContext";

/**
 * Текст песни рядом с обложкой.
 *
 * Загружается только когда его открыли и только для треков, у которых он есть: признак приезжает
 * вместе с треком, поэтому лишнего запроса на каждую песню не случается. Ошибка загрузки гасится
 * здесь же — текст не та вещь, ради которой стоит прерывать музыку.
 */
export function LyricsPane({ track }: { track: Track }) {
  const t = useT();
  const { position } = usePlayerProgress();
  const reduceMotion = useReducedMotion();

  const [lyrics, setLyrics] = useState<Lyrics | null>(null);

  // Трек без текста известен заранее — по признаку, который приехал вместе с ним, — поэтому
  // такому не нужен ни запрос, ни состояние загрузки.
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

  // Подсвечена последняя строка, чьё время уже наступило.
  const current = useMemo(() => {
    if (lines.length === 0) return -1;

    const at = position * 1000;
    let index = -1;

    for (let i = 0; i < lines.length && lines[i].at <= at; i += 1) index = i;

    return index;
  }, [lines, position]);

  if (state === "loading") return <p className="muted lyrics-note">{t("common.loading")}</p>;
  if (state === "failed") return <p className="muted lyrics-note">{t("lyrics.failed")}</p>;
  if (!lyrics) return <p className="muted lyrics-note">{t("lyrics.none")}</p>;

  if (lines.length === 0) {
    return <p className="lyrics-plain">{lyrics.plain}</p>;
  }

  return (
    <ol className="lyrics-lines">
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
    <li ref={element} className={`lyrics-line ${active ? "is-current" : ""}`} aria-current={active}>
      {/* Пустая строка — это проигрыш между куплетами, и место она занимать должна. */}
      {text || " "}
    </li>
  );
}
