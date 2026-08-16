"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { useState } from "react";
import { queries } from "@/lib/queries";
import { useInvalidate } from "@/lib/useInvalidate";
import { usePage } from "@/lib/usePage";
import { ArtistMenu } from "@/components/ArtistMenu";
import { Cover } from "@/components/Cover";
import { PageHeader } from "@/components/PageHeader";
import { Pagination, PageToolbar } from "@/components/PageToolbar";
import { Query } from "@/components/Query";
import { Row, Table } from "@/components/ui/table";
import { useT } from "@/contexts/I18nContext";

const PAGE_SIZE = 50;

export default function AdminArtistsPage() {
  const t = useT();
  const invalidate = useInvalidate();

  const [search, setSearch] = useState("");
  const [menuFor, setMenuFor] = useState<string | null>(null);
  const [page, setPage] = usePage([search]);

  const artists = useQuery(queries.artists({ page, pageSize: PAGE_SIZE, q: search || undefined }));

  return (
    <>
      <PageHeader
        title={t("nav.artists")}
        subtitle={artists.data ? t("count.artists", { count: artists.data.total }) : undefined}
      />

      <PageToolbar search={search} onSearch={setSearch} placeholder={t("filter.artists")} />

      <Query
        result={artists}
        skeleton="row"
        empty={{ title: search ? t("filter.nothingMatched") : t("artists.empty") }}
      >
        {(data) => (
          <>
            <Table>
              {data.items.map((artist) => (
                <Row
                  key={artist.id}
                  className="grid-cols-[2.5rem_minmax(0,1.6fr)_minmax(0,1fr)_auto] max-md:grid-cols-[2.5rem_minmax(0,1fr)_auto] max-md:[grid-template-areas:'art_name_menu''art_meta_menu'] max-md:gap-x-3 max-md:gap-y-0.5"
                >
                  <Cover
                    artistId={artist.id}
                    hasCover={artist.hasImage}
                    name={artist.name}
                    size={40}
                    rounded
                    className="max-md:[grid-area:art]"
                  />

                  <Link
                    href={`/artists/${artist.id}`}
                    className="truncate font-semibold hover:text-primary max-md:[grid-area:name]"
                  >
                    {artist.name}
                  </Link>

                  <span className="truncate text-muted-foreground max-md:[grid-area:meta]">
                    {t("count.tracks", { count: artist.trackCount })}
                    {artist.albumCount > 0
                      ? ` · ${t("count.albums", { count: artist.albumCount })}`
                      : ""}
                  </span>

                  <span className="max-md:[grid-area:menu]">
                    <ArtistMenu
                      artist={{ id: artist.id, name: artist.name, hasImage: artist.hasImage }}
                      open={menuFor === artist.id}
                      onOpenChange={(open) => setMenuFor(open ? artist.id : null)}
                      onChanged={() => invalidate("library")}
                    />
                  </span>
                </Row>
              ))}
            </Table>

            <Pagination result={data} onChange={setPage} />
          </>
        )}
      </Query>
    </>
  );
}
