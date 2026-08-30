// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { type DragEndEvent } from "@dnd-kit/core";
import { arrayMove, useSortable } from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import { useReducedMotion } from "motion/react";
import Link from "next/link";
import { memo, useCallback, useEffect, useMemo, useRef, useState } from "react";
import { cn } from "@/lib/cn";
import { formatAudioSpec, formatDuration, isLossless } from "@/lib/format";
import { useFormat } from "@/lib/useFormat";
import { useInvalidate } from "@/lib/useInvalidate";
import { usePlaylistsOnce } from "@/lib/usePlaylistsOnce";
import { useToggleFavorite } from "@/lib/useToggleFavorite";
import type { Playlist, Track } from "@/lib/types";
import { useNowPlaying, usePlayerActions, type PlaybackOrigin } from "@/contexts/PlayerContext";
import { useT } from "@/contexts/I18nContext";
import { useToast } from "@/contexts/ToastContext";
import { ArtistLinks } from "./ArtistLinks";
import { TrackCover } from "./Cover";
import { EmptyState } from "./EmptyState";
import { TrackMenu } from "./TrackMenu";
import { VerticalSortable } from "./VerticalSortable";
import { Button, PressButton } from "./ui/button";
import { Checkbox } from "./ui/checkbox";
import { Overline } from "./ui/label";
import { GripIcon, HeartIcon, NoteIcon, PauseIcon, PlayIcon } from "./Icons";

export interface TrackSelection {
  selected: ReadonlySet<string>;
  onToggle: (trackId: string, index: number, extend: boolean) => void;
  onToggleAll: () => void;
}

interface TrackListProps {
  tracks: Track[];
  showCover?: boolean;
  showAlbum?: boolean;
  showArtist?: boolean;
  /** Бейдж качества в строке. Страница альбома гасит его, когда формат по альбому единый. */
  showAudioSpec?: boolean;
  useTrackNumbers?: boolean;
  playedAt?: Record<string, string>;
  onChanged?: () => void;
  playlistId?: string;
  onReorder?: (trackIds: string[]) => void;
  emptyMessage?: string;
  origin?: PlaybackOrigin;
  selection?: TrackSelection;
}

const ROW_COLUMNS = {
  "album+date": "grid-cols-[2.75rem_minmax(0,3fr)_minmax(0,2fr)_7rem_4.75rem_5.5rem]",
  album: "grid-cols-[2.75rem_minmax(0,3fr)_minmax(0,2fr)_4.75rem_5.5rem]",
  date: "grid-cols-[2.75rem_minmax(0,3fr)_7rem_4.75rem_5.5rem]",
  plain: "grid-cols-[2.75rem_minmax(0,1fr)_4.75rem_5.5rem]",
} as const;

const rowBase =
  "grid items-center gap-3 rounded-md px-2.5 py-2 max-md:grid-cols-[2.125rem_minmax(0,1fr)_auto_auto] max-md:gap-2 max-md:px-1 max-[380px]:grid-cols-[2.125rem_minmax(0,1fr)_auto]";

function rowGridFor(showAlbum: boolean, showDate: boolean): string {
  const key =
    showAlbum && showDate ? "album+date" : showAlbum ? "album" : showDate ? "date" : "plain";
  return cn(rowBase, ROW_COLUMNS[key]);
}

export function TrackList({
  tracks,
  showCover = true,
  showAlbum = true,
  showArtist = true,
  showAudioSpec = true,
  useTrackNumbers = false,
  playedAt,
  onChanged,
  playlistId,
  onReorder,
  emptyMessage,
  origin,
  selection,
}: TrackListProps) {
  // INFO: узкая подписка вместо usePlayerState — списку нужно только «этот ли трек играет»,
  // а полное состояние меняется ещё и на каждый patchTrack и перерисовывало бы все строки.
  const { currentTrackId, isPlaying } = useNowPlaying();
  const actions = usePlayerActions();
  const { notify } = useToast();
  const invalidate = useInvalidate();
  const t = useT();

  const [menuFor, setMenuFor] = useState<string | null>(null);
  const [focused, setFocused] = useState(0);
  const [currentVisible, setCurrentVisible] = useState(true);
  const reduceMotion = useReducedMotion();
  const bodyRef = useRef<HTMLDivElement>(null);
  const [favorites, setFavorites] = useState<Record<string, boolean>>({});

  const [renderedTracks, setRenderedTracks] = useState(tracks);
  if (tracks !== renderedTracks) {
    setRenderedTracks(tracks);
    setFavorites({});
  }

  const loadPlaylists = usePlaylistsOnce();

  const changed = useCallback(() => {
    invalidate("library", "playlists");
    onChanged?.();
  }, [invalidate, onChanged]);

  const isFavorite = useCallback(
    (track: Track) => favorites[track.id] ?? track.isFavorite,
    [favorites],
  );

  const toggleFavorite = useToggleFavorite();

  // Все колбэки строк принимают трек или индекс, а не замыкаются на них: иначе memo на
  // TrackRow сбрасывался бы на каждом рендере списка и не давал бы ничего.
  const likeTrack = useCallback(
    (track: Track, current: boolean) => {
      void toggleFavorite({ id: track.id, isFavorite: current }, (next) =>
        setFavorites((all) => ({ ...all, [track.id]: next })),
      );
    },
    [toggleFavorite],
  );

  const queueTrack = useCallback(
    (track: Track) => {
      actions.addToQueue(track);
      notify(t("menu.addedToQueue", { title: track.title }), "success");
    },
    [actions, notify, t],
  );

  const openMenuFor = useCallback(
    (trackId: string, open: boolean) => setMenuFor(open ? trackId : null),
    [],
  );

  // Вызывающие передают origin литералом, то есть новым объектом на каждый рендер.
  // Раскладываем его на два примитива, иначе play не удержать стабильным.
  const originSource = origin?.source;
  const originId = origin?.sourceId;
  const playbackOrigin = useMemo<PlaybackOrigin | undefined>(
    () => (originSource || originId ? { source: originSource, sourceId: originId } : undefined),
    [originSource, originId],
  );

  // Сознательно не через `usePlayback`: тот ищет трек в контексте по id, а в плейлисте
  // один и тот же трек может стоять несколько раз (ключи строк потому и включают индекс).
  // Здесь нужна именно та строка, по которой кликнули, а не первая с таким же id.
  const play = useCallback(
    (index: number) => {
      const track = tracks[index];
      if (!track) return;

      if (currentTrackId === track.id) {
        actions.toggle();
        return;
      }
      actions.playQueue(tracks, index, playbackOrigin);
    },
    [actions, currentTrackId, tracks, playbackOrigin],
  );

  const focusRow = (index: number) => {
    const clamped = Math.max(0, Math.min(index, tracks.length - 1));
    setFocused(clamped);
    bodyRef.current?.querySelector<HTMLElement>(`[data-row="${clamped}"]`)?.focus();
    return clamped;
  };

  const onRowsKeyDown = (event: React.KeyboardEvent) => {
    const from = Number((event.target as HTMLElement).dataset.row);
    if (Number.isNaN(from)) return;

    if (event.key === "ArrowDown" || event.key === "ArrowUp") {
      event.preventDefault();
      const to = focusRow(from + (event.key === "ArrowDown" ? 1 : -1));

      if (event.shiftKey && selection && to !== from) {
        selection.onToggle(tracks[to].id, to, true);
      }
      return;
    }

    if (event.key === "Enter") {
      event.preventDefault();
      play(from);
    }
  };

  const sortable = Boolean(playlistId && onReorder);

  // Меню трека берёт этот список, чтобы восстановить порядок после «убрать из плейлиста».
  // Пересобирать его на каждый рендер нельзя — сбросит memo у всех строк разом.
  const playlistTrackIds = useMemo(
    () => (playlistId ? tracks.map((item) => item.id) : undefined),
    [playlistId, tracks],
  );

  const selectable = selection !== undefined;
  const onToggleSelected = selection?.onToggle;

  const playingIndex = currentTrackId
    ? tracks.findIndex((track) => track.id === currentTrackId)
    : -1;

  useEffect(() => {
    const row = bodyRef.current?.querySelector(`[data-row="${playingIndex}"]`);
    if (playingIndex < 0 || !row) return;

    const observer = new IntersectionObserver(([entry]) => setCurrentVisible(entry.isIntersecting));
    observer.observe(row);

    return () => observer.disconnect();
  }, [playingIndex, tracks]);

  const onDragEnd = (event: DragEndEvent) => {
    const { active, over } = event;
    if (!over || active.id === over.id || !onReorder) return;

    const from = tracks.findIndex((track) => track.id === active.id);
    const to = tracks.findIndex((track) => track.id === over.id);
    if (from < 0 || to < 0) return;

    onReorder(arrayMove(tracks, from, to).map((track) => track.id));
  };

  if (tracks.length === 0) {
    return <EmptyState icon={<NoteIcon size={24} />} title={emptyMessage ?? t("tracks.empty")} />;
  }

  const grid = rowGridFor(showAlbum, playedAt !== undefined);

  const rows = tracks.map((track, index) => (
    <TrackRow
      key={playlistId ? `${track.id}-${index}` : track.id}
      track={track}
      index={index}
      focused={index === Math.min(focused, tracks.length - 1)}
      grid={grid}
      sortable={sortable}
      showCover={showCover}
      showAlbum={showAlbum}
      showArtist={showArtist}
      showAudioSpec={showAudioSpec}
      useTrackNumbers={useTrackNumbers}
      playedAt={playedAt?.[track.id]}
      showPlayedAt={playedAt !== undefined}
      playlistId={playlistId}
      playlistTrackIds={playlistTrackIds}
      isCurrent={currentTrackId === track.id}
      isPlaying={currentTrackId === track.id && isPlaying}
      isFavorite={isFavorite(track)}
      selectable={selectable}
      isSelected={selection?.selected.has(track.id) ?? false}
      onToggleSelected={onToggleSelected}
      menuOpen={menuFor === track.id}
      onMenuOpenChange={openMenuFor}
      onPlay={play}
      onToggleFavorite={likeTrack}
      onChanged={changed}
      loadPlaylists={loadPlaylists}
      onQueue={queueTrack}
    />
  ));

  const body = (
    <div
      ref={bodyRef}
      className="flex flex-col"
      role="table"
      aria-label={t("tracks.tableLabel")}
      onKeyDown={onRowsKeyDown}
    >
      <div className={cn(grid, "rounded-none border-b border-border pb-2")} role="row">
        {selection ? (
          <span role="columnheader" className="flex items-center">
            <Checkbox
              checked={
                selection.selected.size === 0
                  ? false
                  : tracks.every((track) => selection.selected.has(track.id))
                    ? true
                    : "indeterminate"
              }
              onClick={selection.onToggleAll}
              aria-label={t("tracks.selectAllOnPage")}
            />
          </span>
        ) : (
          <Overline role="columnheader" className="max-md:invisible">
            #
          </Overline>
        )}
        <Overline role="columnheader" className="truncate">
          {t("column.title")}
        </Overline>
        {showAlbum && (
          <Overline role="columnheader" className="truncate max-md:hidden">
            {t("column.album")}
          </Overline>
        )}
        {playedAt && (
          <Overline role="columnheader" className="truncate max-md:hidden">
            {t("column.played")}
          </Overline>
        )}
        <span role="columnheader" aria-label={t("column.actions")} />
        <Overline role="columnheader" className="text-right max-[380px]:hidden">
          {t("column.duration")}
        </Overline>
      </div>

      {rows}
    </div>
  );

  return (
    <>
      {sortable ? (
        <VerticalSortable items={tracks.map((track) => track.id)} onDragEnd={onDragEnd}>
          {body}
        </VerticalSortable>
      ) : (
        body
      )}

      {playingIndex >= 0 && !currentVisible && (
        <Button
          variant="secondary"
          className="fixed bottom-[calc(var(--player-height)+1.5rem)] left-1/2 z-40 -translate-x-1/2 shadow-pop max-md:bottom-[calc(var(--player-height)+var(--mobile-nav-height)+env(safe-area-inset-bottom)+1rem)]"
          onClick={() =>
            bodyRef.current
              ?.querySelector(`[data-row="${playingIndex}"]`)
              ?.scrollIntoView({ block: "center", behavior: reduceMotion ? "auto" : "smooth" })
          }
        >
          {t("tracks.jumpToCurrent")}
        </Button>
      )}
    </>
  );
}

interface TrackRowProps {
  track: Track;
  index: number;
  grid: string;
  sortable: boolean;
  showCover: boolean;
  showAlbum: boolean;
  showArtist: boolean;
  showAudioSpec: boolean;
  useTrackNumbers: boolean;
  /** Момент прослушивания именно этой строки: объект целиком сбрасывал бы memo. */
  playedAt?: string;
  /** Есть ли колонка «прослушано» — она задаёт число ячеек и не зависит от конкретной строки. */
  showPlayedAt: boolean;
  playlistId?: string;
  playlistTrackIds?: string[];
  focused: boolean;
  isCurrent: boolean;
  isPlaying: boolean;
  isFavorite: boolean;
  selectable: boolean;
  isSelected: boolean;
  onToggleSelected?: TrackSelection["onToggle"];
  menuOpen: boolean;
  onMenuOpenChange: (trackId: string, open: boolean) => void;
  onPlay: (index: number) => void;
  onToggleFavorite: (track: Track, isFavorite: boolean) => void;
  onChanged: () => void;
  loadPlaylists: () => Promise<Playlist[]>;
  onQueue: (track: Track) => void;
}

/**
 * Мемоизирована намеренно: строк на странице до сотни, а перерисовывать их все ради
 * смены играющего трека или лайка одной из них незачем. Ради этого все колбэки приходят
 * сверху стабильными и принимают трек или индекс, а не замыкаются на них.
 */
const TrackRow = memo(function TrackRow({
  track,
  index,
  grid,
  sortable,
  showCover,
  showAlbum,
  showArtist,
  showAudioSpec,
  useTrackNumbers,
  playedAt,
  showPlayedAt,
  playlistId,
  playlistTrackIds,
  focused,
  isCurrent,
  isPlaying,
  isFavorite,
  selectable,
  isSelected,
  onToggleSelected,
  menuOpen,
  onMenuOpenChange,
  onPlay,
  onToggleFavorite,
  onChanged,
  loadPlaylists,
  onQueue,
}: TrackRowProps) {
  const t = useT();
  const format = useFormat();

  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({
    id: track.id,
    disabled: !sortable,
  });

  const showSpec = showAudioSpec && isLossless(track.codec);

  return (
    <div
      ref={sortable ? setNodeRef : undefined}
      role="row"
      data-row={index}
      tabIndex={focused ? 0 : -1}
      onDoubleClick={() => onPlay(index)}
      style={sortable ? { transform: CSS.Transform.toString(transform), transition } : undefined}
      className={cn(
        grid,
        "group relative transition-colors outline-none",
        "hover:bg-card focus-within:bg-card focus-visible:bg-card focus-visible:inset-ring focus-visible:inset-ring-ring",
        isCurrent && "bg-card",
        isDragging && "z-10 opacity-90 shadow-pop",
      )}
    >
      <span className="flex items-center gap-1 text-sm text-faint tabular-nums" role="cell">
        {selectable ? (
          <Checkbox
            checked={isSelected}
            onClick={(event) => {
              event.stopPropagation();
              onToggleSelected?.(track.id, index, event.shiftKey);
            }}
            aria-label={t("tracks.selectNamed", { title: track.title })}
          />
        ) : (
          <>
            {sortable && (
              <button
                type="button"
                {...attributes}
                {...listeners}
                aria-label={t("tracks.reorderNamed", { title: track.title })}
                className="cursor-grab text-faint opacity-0 transition-opacity group-hover:opacity-100 focus-visible:opacity-100 active:cursor-grabbing [@media(hover:none)]:opacity-100"
              >
                <GripIcon size={14} />
              </button>
            )}

            <span className="group-hover:hidden [@media(hover:none)]:hidden">
              {useTrackNumbers ? (track.trackNumber ?? index + 1) : index + 1}
            </span>

            <PressButton
              variant="ghost"
              size="icon-sm"
              className="hidden text-foreground group-hover:grid max-md:size-8 [@media(hover:none)]:grid"
              onClick={() => onPlay(index)}
              aria-label={
                isPlaying
                  ? t("tracks.pauseNamed", { title: track.title })
                  : t("tracks.playNamed", { title: track.title })
              }
            >
              {isPlaying ? <PauseIcon size={14} /> : <PlayIcon size={14} />}
            </PressButton>
          </>
        )}
      </span>

      <span className="flex min-w-0 items-center gap-3" role="cell">
        {showCover && <TrackCover track={track} size={40} />}
        <span className="flex min-w-0 flex-col">
          <span className={cn("truncate font-semibold", isCurrent && "text-primary")}>
            {track.title}
          </span>
          {(showArtist || showSpec) && (
            <span className="flex min-w-0 items-center gap-2">
              {showArtist && (
                <ArtistLinks track={track} className="truncate text-sm text-muted-foreground" />
              )}

              {showSpec && (
                <span className="shrink-0 text-2xs font-medium text-faint max-md:hidden">
                  {formatAudioSpec(track)}
                </span>
              )}
            </span>
          )}
        </span>
      </span>

      {showAlbum && (
        <span className="truncate text-sm text-muted-foreground max-md:hidden" role="cell">
          {track.albumId ? (
            <Link href={`/albums/${track.albumId}`}>{track.albumTitle}</Link>
          ) : (
            <span>—</span>
          )}
        </span>
      )}

      {showPlayedAt && (
        <span className="truncate text-sm text-muted-foreground max-md:hidden" role="cell">
          {playedAt ? format.relativeDate(playedAt) : ""}
        </span>
      )}

      <span
        role="cell"
        className={cn(
          "flex items-center justify-end gap-0.5 opacity-0 transition-opacity",
          "group-hover:opacity-100 group-focus-within:opacity-100 [@media(hover:none)]:opacity-100",
          isCurrent && "opacity-100",
        )}
      >
        <Button
          variant="ghost"
          size="icon"
          className={cn(isFavorite && "text-primary opacity-100")}
          onClick={() => onToggleFavorite(track, isFavorite)}
          aria-label={isFavorite ? t("tracks.removeFromFavorites") : t("tracks.addToFavorites")}
          aria-pressed={isFavorite}
        >
          <HeartIcon size={16} filled={isFavorite} />
        </Button>

        <TrackMenu
          track={track}
          open={menuOpen}
          onOpenChange={(open) => onMenuOpenChange(track.id, open)}
          playlistId={playlistId}
          playlistTrackIds={playlistTrackIds}
          onChanged={onChanged}
          loadPlaylists={loadPlaylists}
          onQueue={() => onQueue(track)}
          isFavorite={isFavorite}
          onToggleFavorite={() => onToggleFavorite(track, isFavorite)}
        />
      </span>

      <span
        className="text-right text-sm text-muted-foreground tabular-nums max-[380px]:hidden"
        role="cell"
      >
        {formatDuration(track.durationSeconds)}
      </span>
    </div>
  );
});
