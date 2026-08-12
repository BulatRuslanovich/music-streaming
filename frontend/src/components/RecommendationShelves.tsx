"use client";

import type { TranslationKey } from "@/lib/i18n";
import type { RecommendationSection } from "@/lib/types";
import { useT } from "@/contexts/I18nContext";
import { AlbumCard, ArtistCard, ShelfSection, TrackCards } from "./ui";

/**
 * Заголовки для полок, которые умеет отдавать сервер.
 *
 * Сервер присылает вид полки и предмет, но не готовую фразу — он не знает, на каком языке читают
 * страницу. Сопоставление происходит здесь, а незнакомый вид от более нового сервера откатывается
 * к общему заголовку вместо показа сырого ключа.
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

/** Заголовки, читающиеся как фраза о чём-то и бессмысленные без своего предмета. */
const NEEDS_SUBJECT = new Set(["similarTo", "becauseYouListened", "genreMix"]);

/** Куда ведёт «показать все» у полок, для которых уже есть естественная отдельная страница. */
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

        // Заголовок без предмета там, где предмет нужен, читался бы как оборванная фраза.
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

  // Помечено как пришедшее из рекомендаций, чтобы дальнейшее — дослушано до конца или пропущено
  // через четыре секунды — было отнесено обратно к предложившей полке.
  return (
    <TrackCards
      tracks={tracks}
      context={tracks}
      origin={{ source: "recommendation", sourceId: section.reason?.subjectId ?? undefined }}
    />
  );
}
