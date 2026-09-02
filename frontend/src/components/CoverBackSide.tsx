// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useQuery } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { formatAudioSpec } from "@/lib/format";
import { camelot, pitchClass } from "@/lib/musicKey";
import { queries } from "@/lib/queries";
import { useI18n } from "@/contexts/I18nContext";
import type { Track } from "@/lib/types";
import { Overline } from "./ui/label";

/**
 * Темп ниже этой уверенности — не темп, а автокорреляция шума. Показать «97 BPM» там, где
 * анализатор сам не уверен, хуже, чем не показать ничего.
 */
const TEMPO_CONFIDENCE = 0.35;

/** То же для тональности: слабая оценка ключа расходится с ухом чаще, чем совпадает. */
const KEY_STRENGTH = 0.4;

/**
 * Оборот обложки — выходные данные записи. Всё, кроме кодека, посчитал `AudioAnalysisWorker`
 * ради схожести треков, и до сих пор это нигде не показывалось.
 *
 * Запрос уходит только когда обложку перевернули: до этого компонент не смонтирован.
 */
export function CoverBackSide({ track }: { track: Track }) {
  const { locale, t } = useI18n();
  const analysis = useQuery(queries.trackAnalysis(track.id));

  const data = analysis.data;
  const decimal = (value: number, digits = 1) =>
    value.toLocaleString(locale, { minimumFractionDigits: digits, maximumFractionDigits: digits });

  const key = data && data.keyStrength >= KEY_STRENGTH ? pitchClass(data.key) : null;
  const wheel = data && key ? camelot(data.key, data.isMinor) : null;

  const rows: [string, string | null][] = [
    [
      t("analysis.tempo"),
      data && data.tempoBpm && data.tempoConfidence >= TEMPO_CONFIDENCE
        ? t("analysis.bpm", { value: Math.round(data.tempoBpm) })
        : null,
    ],
    [
      t("analysis.key"),
      key &&
        `${key} ${t(data?.isMinor ? "analysis.minor" : "analysis.major")}${wheel ? ` · ${wheel}` : ""}`,
    ],
    [t("analysis.loudness"), data ? `${decimal(data.loudnessDb)} LUFS` : null],
    [t("analysis.dynamicRange"), data ? `${decimal(data.dynamicRangeDb)} dB` : null],
    // Кодек всегда под рукой — оборот не бывает пустым, даже когда анализа нет вовсе.
    [t("analysis.format"), formatAudioSpec(track)],
  ];

  const known = rows.filter(([, value]) => value !== null);

  return (
    <div className="flex size-full flex-col justify-center gap-4 overflow-y-auto bg-card p-6 text-left">
      <Overline>{t("analysis.title")}</Overline>

      <dl className="flex flex-col gap-2.5">
        {known.map(([label, value]) => (
          <Row key={label} label={label} value={value ?? ""} />
        ))}
      </dl>

      {analysis.isPending && <p className="text-xs text-faint">{t("common.loading")}</p>}
      {!analysis.isPending && known.length <= 1 && (
        <p className="text-xs text-faint">{t("analysis.unanalyzed")}</p>
      )}
    </div>
  );
}

function Row({ label, value }: { label: string; value: string }): ReactNode {
  return (
    <div className="flex items-baseline justify-between gap-4 border-b border-border pb-2">
      <dt className="text-sm text-muted-foreground">{label}</dt>
      <dd className="shrink-0 font-semibold tabular-nums">{value}</dd>
    </div>
  );
}
