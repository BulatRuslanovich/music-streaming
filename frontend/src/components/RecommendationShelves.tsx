"use client";

import type { TranslationKey } from "@/lib/i18n";
import type { RecommendationSection } from "@/lib/types";
import { useT } from "@/contexts/I18nContext";
import { AlbumCard, ArtistCard, ShelfSection, TrackCards } from "./ui";

/**
 * Headings for the shelves the server can produce.
 *
 * The server sends a shelf kind and a subject, never a finished sentence — it has no idea which
 * language the page is being read in. Mapping happens here, and an unknown kind from a newer
 * server falls back to a generic heading rather than showing a raw key.
 */
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

/** Headings that read as a sentence about something, and are meaningless without their subject. */
const NEEDS_SUBJECT = new Set(["similarTo", "becauseYouListened", "genreMix"]);

/** Where "see all" leads, for the shelves that have a natural full page already. */
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

        // A subject-less heading for a shelf that needs one would read as a broken sentence.
        const usable = titleKey && (!NEEDS_SUBJECT.has(section.baseKey) || subject);
        const title = usable ? t(titleKey, subject ? { subject } : undefined) : t("rec.shelf.forYou");

        return (
          <ShelfSection key={section.key} title={title} href={SHELF_LINKS[section.baseKey]}>
            <SectionItems section={section} />
          </ShelfSection>
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

  // Tagged as recommendation-sourced so that what happens next — played to the end, or skipped
  // after four seconds — is attributed back to the shelf that suggested it.
  return (
    <TrackCards
      tracks={tracks}
      context={tracks}
      origin={{ source: "recommendation", sourceId: section.reason?.subjectId ?? undefined }}
    />
  );
}
