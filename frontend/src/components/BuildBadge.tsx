"use client";

import { useEffect, useState } from "react";
import { useI18n } from "@/contexts/I18nContext";
import { api } from "@/lib/api";
import { frontendBuild } from "@/lib/buildInfo";
import type { Locale } from "@/lib/i18n";
import type { SystemInfo } from "@/lib/types";

export function BuildBadge() {
  const { locale, t } = useI18n();
  const [backend, setBackend] = useState<SystemInfo | null>(null);

  useEffect(() => {
    let active = true;

    api
      .system()
      .then((info) => {
        if (active) setBackend(info);
      })
      .catch(() => undefined);

    return () => {
      active = false;
    };
  }, []);

  const dash = t("build.unknown");
  const mismatched =
    backend?.commit !== undefined &&
    frontendBuild.commit !== undefined &&
    backend.commit !== frontendBuild.commit;

  const label = `${frontendBuild.version} · ${frontendBuild.commit ?? dash}`;

  const tooltip = [
    describe(t("build.frontend"), frontendBuild, locale, dash),
    backend ? describe(t("build.backend"), backend, locale, dash) : null,
  ]
    .filter((line) => line !== null)
    .join("\n");

  return (
    <p className={`build-badge ${mismatched ? "is-mismatched" : ""}`} title={tooltip}>
      {label}
      {mismatched && <span className="build-badge-api"> · api {backend.commit}</span>}
    </p>
  );
}

function describe(title: string, info: SystemInfo, locale: Locale, dash: string): string {
  const builtAt = info.builtAt ? new Date(info.builtAt) : null;
  const stamp =
    builtAt && !Number.isNaN(builtAt.getTime())
      ? builtAt.toLocaleString(locale, { dateStyle: "medium", timeStyle: "short" })
      : dash;

  return `${title}: ${info.version} · ${info.commit ?? dash} · ${stamp}`;
}
