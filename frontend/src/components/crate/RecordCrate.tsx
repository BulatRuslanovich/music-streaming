// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import Link from "next/link";
import { useQueryClient } from "@tanstack/react-query";
import { useEffect, useRef, useState, type CSSProperties } from "react";
import { cn } from "@/lib/cn";
import { queries } from "@/lib/queries";
import { useToast } from "@/lib/useToast";
import type { Album } from "@/lib/types";
import { usePlayerActions } from "@/contexts/PlayerContext";
import { useT } from "@/contexts/I18nContext";
import { AlbumCover } from "@/components/Cover";
import { Record as Vinyl } from "@/components/Record";
import { Button } from "@/components/ui/button";
import { ChevronDownIcon, ChevronUpIcon, PlayIcon } from "lucide-react";

// Сколько конвертов видно за передним и сколько уже перелистанных ещё падают вперёд.
const BEHIND = 12;
const FLIPPED = 2;

// Колесо и свайп листают по одному конверту на столько пикселей хода.
const WHEEL_STEP = 70;
const SWIPE_STEP = 42;

// Подгрузка следующей страницы, когда до конца ящика осталось столько конвертов.
const PREFETCH_AHEAD = 10;

export function letterOf(title: string): string {
  const first = [...title.trim()][0]?.toLocaleUpperCase() ?? "";
  return /\p{L}/u.test(first) ? first : "#";
}

// Альбомы как пластинки в ящике: стопка конвертов, наклонённых назад; перелистнутые падают
// вперёд, передний можно вытащить — из него выезжает пластинка.
export function RecordCrate({
  albums,
  hasMore,
  onNeedMore,
}: {
  albums: Album[];
  hasMore: boolean;
  onNeedMore: () => void;
}) {
  const t = useT();
  const client = useQueryClient();
  const { playQueue } = usePlayerActions();
  const { notifyError } = useToast();

  const [focus, setFocus] = useState(0);
  const [pulled, setPulled] = useState(false);
  const wheel = useRef(0);
  const swipe = useRef<number | null>(null);
  const boxRef = useRef<HTMLDivElement>(null);

  const last = albums.length - 1;
  const current = albums[Math.min(focus, last)];

  const go = (delta: number) => {
    setPulled(false);
    setFocus((index) => Math.max(0, Math.min(last, index + delta)));
  };

  useEffect(() => {
    if (hasMore && focus >= albums.length - PREFETCH_AHEAD) onNeedMore();
  }, [focus, albums.length, hasMore, onNeedMore]);

  useEffect(() => {
    const box = boxRef.current;
    if (!box) return;

    const onWheel = (event: WheelEvent) => {
      event.preventDefault();
      wheel.current += event.deltaY;

      const steps = Math.trunc(wheel.current / WHEEL_STEP);
      if (steps === 0) return;

      wheel.current -= steps * WHEEL_STEP;
      setPulled(false);
      setFocus((index) => Math.max(0, Math.min(albums.length - 1, index + steps)));
    };

    box.addEventListener("wheel", onWheel, { passive: false });
    return () => box.removeEventListener("wheel", onWheel);
  }, [albums.length]);

  const play = async (album: Album) => {
    try {
      const detail = await client.fetchQuery(queries.album(album.id));
      if (detail.tracks.length > 0) playQueue(detail.tracks, 0);
    } catch (error) {
      notifyError(error);
    }
  };

  if (!current) return null;

  const letter = letterOf(current.title);

  return (
    <section className="flex flex-col items-center gap-6" aria-roledescription={t("crate.label")}>
      <div
        ref={boxRef}
        className="crate"
        tabIndex={0}
        aria-label={t("crate.hint")}
        onKeyDown={(event) => {
          const moves: Record<string, () => void> = {
            ArrowDown: () => go(1),
            ArrowRight: () => go(1),
            ArrowUp: () => go(-1),
            ArrowLeft: () => go(-1),
            PageDown: () => go(10),
            PageUp: () => go(-10),
            Home: () => go(-focus),
            End: () => go(last - focus),
            Enter: () => setPulled((open) => !open),
            " ": () => setPulled((open) => !open),
            Escape: () => setPulled(false),
          };

          const move = moves[event.key];
          if (!move) return;
          event.preventDefault();
          move();
        }}
        onPointerDown={(event) => {
          swipe.current = event.clientY;
        }}
        onPointerMove={(event) => {
          if (swipe.current === null || event.pointerType === "mouse") return;

          const steps = Math.trunc((swipe.current - event.clientY) / SWIPE_STEP);
          if (steps === 0) return;

          swipe.current -= steps * SWIPE_STEP;
          go(steps);
        }}
        onPointerUp={() => {
          swipe.current = null;
        }}
        onPointerCancel={() => {
          swipe.current = null;
        }}
      >
        <div className="crate-stack">
          {albums.map((album, index) => {
            const depth = index - focus;
            if (depth < -FLIPPED || depth > BEHIND) return null;

            const side = depth < 0 ? "flipped" : depth === 0 ? "front" : "behind";

            return (
              <button
                key={album.id}
                type="button"
                tabIndex={-1}
                className="crate-sleeve"
                data-side={side}
                data-pulled={side === "front" && pulled}
                style={{ "--depth": depth, zIndex: BEHIND + 1 - Math.abs(depth) } as CSSProperties}
                onClick={() => (depth === 0 ? setPulled((open) => !open) : go(depth))}
                aria-label={album.title}
              >
                {side === "front" ? (
                  <Vinyl
                    track={{
                      id: album.id,
                      title: album.title,
                      albumId: album.id,
                      albumTitle: album.title,
                      hasCover: album.hasCover,
                    }}
                    out={pulled}
                    spinning={false}
                    axis="y"
                    sizes="min(22rem, 70vw)"
                    className="crate-record"
                  />
                ) : (
                  <AlbumCover album={album} variant={Math.abs(depth) < 3 ? "full" : "thumb"} />
                )}
              </button>
            );
          })}
        </div>

        <div className="crate-front" aria-hidden="true">
          <span className="crate-letter font-display">{letter}</span>
        </div>
      </div>

      <div className="flex w-full max-w-[28rem] items-center gap-3" aria-live="polite">
        <Button
          variant="ghost"
          size="icon"
          onClick={() => go(-1)}
          disabled={focus === 0}
          aria-label={t("crate.previous")}
        >
          <ChevronUpIcon size={18} />
        </Button>

        <div className="flex min-w-0 flex-1 flex-col items-center text-center">
          <Link
            href={`/albums/${current.id}`}
            className="w-full truncate font-semibold hover:underline"
          >
            {current.title}
          </Link>
          <span className="w-full truncate text-sm text-muted-foreground">
            {[current.artistName, current.year].filter(Boolean).join(" · ")}
          </span>
          <span className="text-xs text-faint tabular-nums">
            {t("crate.position", { index: focus + 1, total: albums.length })}
          </span>
        </div>

        <Button
          variant="ghost"
          size="icon"
          onClick={() => go(1)}
          disabled={focus >= last}
          aria-label={t("crate.next")}
        >
          <ChevronDownIcon size={18} />
        </Button>
      </div>

      <Button
        variant="primary"
        className={cn("transition-opacity", !pulled && "opacity-80")}
        onClick={() => void play(current)}
      >
        <PlayIcon size={16} /> {t("action.playNamed", { name: current.title })}
      </Button>
    </section>
  );
}
