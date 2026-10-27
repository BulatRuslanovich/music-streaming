// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { genreName } from "@/lib/format";
import { useQuery } from "@tanstack/react-query";
import { useMemo, useRef, useState } from "react";
import { api } from "@/lib/api";
import { cn } from "@/lib/cn";
import { MAX_SELECTION_PLAY, closestToCentre, median, soundPath } from "@/lib/libraryMap";
import { moodHue, moodLabel } from "@/lib/moods";
import { queries } from "@/lib/queries";
import { useToast } from "@/lib/useToast";
import type { MapPoint } from "@/lib/types";
import { usePlayerActions, usePlayerState } from "@/contexts/PlayerContext";
import { useT } from "@/contexts/I18nContext";
import { PageHeader } from "@/components/PageHeader";
import { Query } from "@/components/Query";
import { Button } from "@/components/ui/button";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  LibraryMapCanvas,
  type MapController,
  type MapLabel,
} from "@/components/map/LibraryMapCanvas";
import {
  CrosshairIcon,
  LassoSelectIcon,
  MapIcon,
  MinusIcon,
  PlayIcon,
  PlusIcon,
  ScanIcon,
  ShuffleIcon,
  XIcon,
} from "lucide-react";

const ALL_GENRES = "*";
const GENRE_CHOICES = 16;

export function LibraryMapPage() {
  const t = useT();
  const map = useQuery(queries.libraryMap());

  return (
    <div className="flex min-h-0 flex-1 flex-col gap-5">
      <PageHeader
        title={t("map.title")}
        subtitle={
          map.data && map.data.points.length > 0 && map.data.points.length < map.data.totalTracks
            ? t("map.coverage", { count: map.data.points.length, total: map.data.totalTracks })
            : undefined
        }
      />

      <Query
        result={map}
        isEmpty={(data) => data.points.length === 0}
        empty={{ icon: <MapIcon size={24} />, title: t("map.preparing") }}
      >
        {(data) => <MapExplorer points={data.points} landmarks={data.landmarks} />}
      </Query>
    </div>
  );
}

function MapExplorer({
  points,
  landmarks,
}: {
  points: MapPoint[];
  landmarks: { mood: string; x: number; y: number }[];
}) {
  const t = useT();
  const { notifyError } = useToast();
  const { currentTrack } = usePlayerState();
  const { playQueue, playTrack, startRadio } = usePlayerActions();
  const controller = useRef<MapController>(null);

  const [genre, setGenre] = useState<string | null>(null);
  const [selecting, setSelecting] = useState(false);
  const [selection, setSelection] = useState<MapPoint[] | null>(null);
  const [busy, setBusy] = useState(false);

  const genres = useMemo(() => {
    const counts = new Map<string, number>();
    for (const point of points) {
      if (point.genre) counts.set(point.genre, (counts.get(point.genre) ?? 0) + 1);
    }

    return [...counts.entries()]
      .sort((a, b) => b[1] - a[1])
      .slice(0, GENRE_CHOICES)
      .map(([name]) => name);
  }, [points]);

  const labels = useMemo<MapLabel[]>(() => {
    const moods = landmarks.map((landmark) => ({
      ...landmark,
      text: moodLabel(landmark.mood, t),
      hue: moodHue(landmark.mood),
    }));
    if (!genre) return moods;

    const members = points.filter((point) => point.genre === genre);
    if (members.length === 0) return moods;

    return [
      ...moods,
      {
        text: genreName(genre),
        x: median(members.map((point) => point.x)),
        y: median(members.map((point) => point.y)),
        strong: true,
      },
    ];
  }, [landmarks, genre, points, t]);

  const selectedIds = useMemo(
    () => (selection ? new Set(selection.map((point) => point.id)) : null),
    [selection],
  );

  const onCurrent = currentTrack && points.some((point) => point.id === currentTrack.id);

  const run = async (work: () => Promise<unknown>) => {
    setBusy(true);
    try {
      await work();
    } catch (error) {
      notifyError(error);
    } finally {
      setBusy(false);
    }
  };

  const playOne = (point: MapPoint) =>
    run(async () => {
      const [track] = await api.lookupTracks([point.id]);
      if (track) playTrack(track);
    });

  const radioFrom = (point: MapPoint) =>
    run(async () => {
      const [track] = await api.lookupTracks([point.id]);
      if (track) await startRadio(track);
    });

  const playSelection = (shuffled: boolean) =>
    run(async () => {
      if (!selection) return;

      const chosen = closestToCentre(selection, MAX_SELECTION_PLAY);
      const ordered = shuffled ? [...chosen].sort(() => Math.random() - 0.5) : soundPath(chosen);
      const tracks = await api.lookupTracks(ordered.map((point) => point.id));

      if (tracks.length > 0) playQueue(tracks, 0);
    });

  return (
    <div className="flex min-h-0 flex-1 flex-col gap-3">
      <div className="flex flex-wrap items-center gap-2">
        <Select
          value={genre ?? ALL_GENRES}
          onValueChange={(value) => setGenre(value === ALL_GENRES ? null : value)}
        >
          <SelectTrigger aria-label={t("map.genre")} className="min-w-[11rem]">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={ALL_GENRES}>{t("map.allGenres")}</SelectItem>
            {genres.map((name) => (
              <SelectItem key={name} value={name}>
                {genreName(name)}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>

        <Button
          variant={selecting ? "secondary" : "outline"}
          size="sm"
          aria-pressed={selecting}
          onClick={() => setSelecting((on) => !on)}
        >
          <LassoSelectIcon size={14} />
          {t("map.select")}
        </Button>

        <div className="ml-auto flex items-center gap-1">
          {onCurrent && (
            <Button
              variant="ghost"
              size="icon"
              onClick={() => controller.current?.locate(currentTrack.id)}
              aria-label={t("map.locate")}
              title={t("map.locate")}
            >
              <CrosshairIcon size={18} />
            </Button>
          )}
          <Button
            variant="ghost"
            size="icon"
            onClick={() => controller.current?.zoomBy(1 / 1.5)}
            aria-label={t("map.zoomOut")}
            title={t("map.zoomOut")}
          >
            <MinusIcon size={18} />
          </Button>
          <Button
            variant="ghost"
            size="icon"
            onClick={() => controller.current?.zoomBy(1.5)}
            aria-label={t("map.zoomIn")}
            title={t("map.zoomIn")}
          >
            <PlusIcon size={18} />
          </Button>
          <Button
            variant="ghost"
            size="icon"
            onClick={() => controller.current?.reset()}
            aria-label={t("map.resetView")}
            title={t("map.resetView")}
          >
            <ScanIcon size={18} />
          </Button>
        </div>
      </div>

      <div className="relative min-h-[22rem] flex-1 max-md:min-h-[60vh]">
        <LibraryMapCanvas
          points={points}
          labels={labels}
          highlightGenre={genre}
          nowPlayingId={currentTrack?.id ?? null}
          selecting={selecting}
          selectedIds={selectedIds}
          onSelect={(chosen) => {
            setSelection(chosen.length > 0 ? chosen : null);
            setSelecting(false);
          }}
          onPlay={(point) => void playOne(point)}
          onRadio={(point) => void radioFrom(point)}
          controllerRef={controller}
        />

        {selection && (
          <div
            className={cn(
              "absolute inset-x-3 bottom-3 z-10 mx-auto flex max-w-[34rem] flex-wrap items-center gap-2 rounded-lg bg-popover p-2 pl-4 text-popover-foreground shadow-pop",
            )}
            aria-live="polite"
          >
            <span className="flex-1 text-sm font-medium">
              {t("map.selected", { count: selection.length })}
            </span>
            <Button
              variant="primary"
              size="sm"
              disabled={busy}
              onClick={() => void playSelection(false)}
            >
              <PlayIcon size={14} /> {t("action.play")}
            </Button>
            <Button
              variant="outline"
              size="sm"
              disabled={busy}
              onClick={() => void playSelection(true)}
            >
              <ShuffleIcon size={14} /> {t("action.shuffle")}
            </Button>
            <Button
              variant="ghost"
              size="icon-sm"
              onClick={() => setSelection(null)}
              aria-label={t("map.clearSelection")}
              title={t("map.clearSelection")}
            >
              <XIcon size={14} />
            </Button>
          </div>
        )}
      </div>

      <PlaysLegend />
    </div>
  );
}

function PlaysLegend() {
  const t = useT();

  return (
    <div className="flex items-center gap-2 text-xs text-muted-foreground" aria-hidden="true">
      <span>{t("map.legendFew")}</span>
      <span className="h-2 w-28 rounded-full bg-[linear-gradient(to_right,color-mix(in_oklab,var(--faint-foreground)_45%,transparent),color-mix(in_oklab,var(--primary)_30%,transparent),var(--primary))]" />
      <span>{t("map.legendMany")}</span>
    </div>
  );
}
