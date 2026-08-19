"use client";

import { useEffect } from "react";

const APP_NAME = "Caimack";

let pageTitle: string | null = null;
let nowPlaying: string | null = null;

function apply() {
  if (typeof document === "undefined") return;

  const lead = nowPlaying ?? pageTitle;
  document.title = lead ? `${lead} · ${APP_NAME}` : APP_NAME;
}

export function setNowPlaying(title: string | null) {
  nowPlaying = title;
  apply();
}

export function usePageTitle(title: string | null | undefined) {
  useEffect(() => {
    if (!title) return;

    pageTitle = title;
    apply();

    return () => {
      if (pageTitle === title) {
        pageTitle = null;
        apply();
      }
    };
  }, [title]);
}
