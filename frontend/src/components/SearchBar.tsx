"use client";

import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useEffect, useState } from "react";
import { SearchIcon } from "./Icons";

export function SearchBar() {
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();

  const onSearchPage = pathname === "/search";
  const queryInUrl = onSearchPage ? (searchParams.get("q") ?? "") : "";

  const [input, setInput] = useState(queryInUrl);
  const [lastSeenInUrl, setLastSeenInUrl] = useState(queryInUrl);

  if (queryInUrl !== lastSeenInUrl) {
    setLastSeenInUrl(queryInUrl);
    setInput(queryInUrl);
  }

  useEffect(() => {
    const trimmed = input.trim();
    if (trimmed === queryInUrl) return;

    const timer = window.setTimeout(() => {
      const target = trimmed ? `/search?q=${encodeURIComponent(trimmed)}` : "/search";
      if (onSearchPage) router.replace(target);
      else router.push(target);
    }, 250);

    return () => window.clearTimeout(timer);
  }, [input, queryInUrl, onSearchPage, router]);

  return (
    <div className="search-field">
      <SearchIcon size={18} />
      <label htmlFor="library-search" className="sr-only">
        Search your library
      </label>
      <input
        id="library-search"
        type="search"
        placeholder="Tracks, albums, artists, genres…"
        value={input}
        autoComplete="off"
        onChange={(event) => setInput(event.target.value)}
      />
    </div>
  );
}
