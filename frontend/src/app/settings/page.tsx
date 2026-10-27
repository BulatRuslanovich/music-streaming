// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useRouter, useSearchParams } from "next/navigation";
import { Suspense, useCallback } from "react";
import { Copyright } from "@/components/Copyright";
import { PageHeader } from "@/components/PageHeader";
import { ToggleGroup, ToggleGroupButton } from "@/components/ui/toggle-group";
import { useAuth } from "@/contexts/AuthContext";
import { useT } from "@/contexts/I18nContext";
import { RecommendationStats } from "./RecommendationStats";
import { Appearance } from "./Appearance";
import { Playback } from "./Playback";
import { Experimental } from "./Experimental";
import { Account } from "./Account";

export default function SettingsPage() {
  const t = useT();

  return (
    <>
      <PageHeader title={t("settings.title")} />
      <Suspense fallback={null}>
        <SettingsSections />
      </Suspense>
      <Copyright className="mt-auto" />
    </>
  );
}

type SettingsSection = "playback" | "appearance" | "recommendations" | "account" | "experimental";

const SECTIONS: SettingsSection[] = ["playback", "appearance", "recommendations", "account"];

const ADMIN_SECTIONS: SettingsSection[] = [...SECTIONS, "experimental"];

const DEFAULT_SECTION: SettingsSection = "playback";

function SettingsSections() {
  const t = useT();
  const router = useRouter();
  const params = useSearchParams();
  const { isAdmin } = useAuth();
  const sections = isAdmin ? ADMIN_SECTIONS : SECTIONS;

  const raw = params.get("tab");
  const section = sections.find((key) => key === raw) ?? DEFAULT_SECTION;

  const setSection = useCallback(
    (next: SettingsSection) => {
      router.replace(next === DEFAULT_SECTION ? "/settings" : `/settings?tab=${next}`);
    },
    [router],
  );

  return (
    <div className="flex w-full max-w-3xl flex-col gap-6">
      <ToggleGroup aria-label={t("settings.title")} className="max-md:-mx-4 max-md:px-4">
        {sections.map((key) => (
          <ToggleGroupButton key={key} active={section === key} onClick={() => setSection(key)}>
            {t(`settings.${key}`)}
          </ToggleGroupButton>
        ))}
      </ToggleGroup>

      <div className="min-w-0">
        {section === "playback" && <Playback />}
        {section === "appearance" && <Appearance />}
        {section === "recommendations" && <RecommendationStats />}
        {section === "account" && <Account />}
        {section === "experimental" && <Experimental />}
      </div>
    </div>
  );
}
