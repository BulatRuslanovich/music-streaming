"use client";

import { cn } from "@/lib/cn";
import { formatArtists } from "@/lib/format";
import type { HomeBlock } from "@/lib/types";
import { usePlayer, type PlaybackOrigin } from "@/contexts/PlayerContext";
import { useT } from "@/contexts/I18nContext";
import { Cover } from "../Cover";
import { PauseIcon, PlayIcon, PlaylistIcon } from "../Icons";
import { Tile } from "./Tile";

export function QuickTiles({ block, origin }: { block: HomeBlock; origin: PlaybackOrigin }) {
  const t = useT();
  const player = usePlayer();

  const tracks = block.tracks ?? [];
  const playlists = block.playlists ?? [];

  return (
    <>
      {tracks.map((track) => {
        const isCurrent = player.currentTrack?.id === track.id;

        return (
          <Tile
            key={track.id}
            current={isCurrent}
            label={track.title}
            sublabel={formatArtists(track)}
            onClick={() => {
              if (isCurrent) {
                player.toggle();
                return;
              }
              player.playTrack(track, tracks, origin);
            }}
            art={
              <Cover
                albumId={track.albumId}
                trackId={track.id}
                hasCover={track.hasCover}
                name={track.albumTitle ?? track.title}
                className="size-full rounded-none"
              />
            }
            action={
              <span
                aria-hidden="true"
                className={cn(
                  "grid size-8 shrink-0 place-items-center rounded-full bg-primary text-primary-foreground shadow-art",
                  "translate-x-1 opacity-0 transition-[opacity,transform] duration-150 ease-brand",
                  "group-hover:translate-x-0 group-hover:opacity-100",
                  isCurrent && "translate-x-0 opacity-100",
                )}
              >
                {isCurrent && player.isPlaying ? <PauseIcon size={16} /> : <PlayIcon size={16} />}
              </span>
            }
          />
        );
      })}

      {playlists.map((playlist) => (
        <Tile
          key={playlist.id}
          href={`/playlists/${playlist.id}`}
          label={playlist.name}
          sublabel={t("count.tracks", { count: playlist.trackCount })}
          art={
            <Cover
              playlistId={playlist.id}
              hasCover={playlist.hasCover}
              coverTrackId={playlist.coverTrackId}
              name={playlist.name}
              fallback={<PlaylistIcon size={22} />}
              className="size-full rounded-none"
            />
          }
        />
      ))}
    </>
  );
}
