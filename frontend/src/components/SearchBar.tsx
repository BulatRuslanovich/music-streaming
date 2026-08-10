"use client";

import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useEffect, useState } from "react";
import { SearchIcon } from "./Icons";

/**
 * The library-wide search field, pinned above the content on every page.
 *
 * The URL is the single source of truth: typing here navigates to `/search?q=…` and the search
 * page renders whatever the query says, so there is only one input in the app and no state to keep
 * in sync between the two. The first keystroke pushes a history entry — Back returns to the page
 * the search started from — and refinements replace it, so Back does not walk through every
 * intermediate query.
 */
export function SearchBar() {
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();

  const onSearchPage = pathname === "/search";
  const queryInUrl = onSearchPage ? (searchParams.get("q") ?? "") : "";

  const [input, setInput] = useState(queryInUrl);
  const [lastSeenInUrl, setLastSeenInUrl] = useState(queryInUrl);

  // When the URL changes underneath the field — Back, a shared link, or navigating away from the
  // results — the field follows it. Adjusting during render rather than in an effect is React's
  // own recommendation for state that tracks something external: it re-renders before anything is
  // painted, so the stale value is never shown.
  if (queryInUrl !== lastSeenInUrl) {
    setLastSeenInUrl(queryInUrl);
    setInput(queryInUrl);
  }

  // Debounced so typing does not push a navigation per keystroke.
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
