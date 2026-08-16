"use client";

import type { TranslationKey } from "@/lib/i18n";
import type { RecommendationSection } from "@/lib/types";
import { useT } from "@/contexts/I18nContext";
import { AlbumCard, ArtistCard, TrackCards } from "./MediaCard";
import { Shelf } from "./PageHeader";

const SHELF_TITLES: Record<string, TranslationKey> = {
  continueListening: "rec.shelf.continueListening",
  forYou: "rec.shelf.forYou",
  similarTo: "rec.shelf.similarTo",
  becauseYouListened: "rec.shelf.becauseYouListened",
  discover: "rec.shelf.discover",
  genreMix: "rec.shelf.genreMix",
  newReleases: "rec.shelf.newReleases",
  popular: "rec.shelf.popular",
  artistsForYou: "rec.shelf.artistsForYou",
  albumsForYou: "rec.shelf.albumsForYou",
};

const NEEDS_SUBJECT = new Set(["similarTo", "becauseYouListened", "genreMix"]);

const SHELF_LINKS: Record<string, string> = {
  continueListening: "/recently-played",
  newReleases: "/tracks?sort=Recent",
  artistsForYou: "/artists",
  albumsForYou: "/albums",
};

export function RecommendationShelves({ sections }: { sections: RecommendationSection[] }) {
  const t = useT();

  return (
    <>
      {sections.map((section) => {
        const subject = section.reason?.subject ?? undefined;
        const titleKey = SHELF_TITLES[section.baseKey];

        const usable = titleKey && (!NEEDS_SUBJECT.has(section.baseKey) || subject);
        const title = usable
          ? t(titleKey, subject ? { subject } : undefined)
          : t("rec.shelf.forYou");

        return (
          <Shelf key={section.key} title={title} href={SHELF_LINKS[section.baseKey]}>
            <SectionItems section={section} />
          </Shelf>
        );
      })}
    </>
  );
}

function SectionItems({ section }: { section: RecommendationSection }) {
  if (section.artists?.length) {
    return (
      <>
        {section.artists.map((artist) => (
          <ArtistCard key={artist.id} artist={artist} />
        ))}
      </>
    );
  }

  if (section.albums?.length) {
    return (
      <>
        {section.albums.map((album) => (
          <AlbumCard key={album.id} album={album} />
        ))}
      </>
    );
  }

  const tracks = section.tracks?.map((item) => item.track) ?? [];

  return (
    <TrackCards
      tracks={tracks}
      context={tracks}
      origin={{ source: "recommendation", sourceId: section.reason?.subjectId ?? undefined }}
    />
  );
}
