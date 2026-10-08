// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useMutation } from "@tanstack/react-query";
import * as RadioGroup from "@radix-ui/react-radio-group";
import { useRouter, useSearchParams } from "next/navigation";
import { Suspense, useCallback, useState } from "react";
import { useForm } from "react-hook-form";
import { api } from "@/lib/api";
import { limits, passwordChangeSchema, type PasswordChangeValues } from "@/lib/schemas";
import { LOCALES, LOCALE_NAMES } from "@/lib/i18n";
import { setTheme, THEME_CHOICES, useThemeChoice } from "@/lib/theme";
import { CROSSFADE_CHOICES } from "@/lib/playback/crossfade";
import {
  EQ_BANDS,
  EQ_LIMIT_DB,
  EQ_PRESETS,
  type EqualizerPreset,
  useEqualizer,
} from "@/lib/playback/equalizer";
import { cn } from "@/lib/cn";
import { Copyright } from "@/components/Copyright";
import { PageHeader } from "@/components/PageHeader";
import { Button } from "@/components/ui/button";
import { TextField } from "@/components/ui/form";
import { Switch } from "@/components/ui/switch";
import { useSettings } from "@/lib/useSettings";
import { useI18n, useT } from "@/contexts/I18nContext";
import { usePlayer } from "@/contexts/PlayerContext";
import { useToast } from "@/lib/useToast";
import type { AudioQuality } from "@/lib/types";
import { ThemeSwatch } from "./ThemeSwatch";
import { RecommendationStats } from "./RecommendationStats";

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

type SettingsSection = "playback" | "appearance" | "recommendations" | "account";

const SECTIONS: SettingsSection[] = ["playback", "appearance", "recommendations", "account"];

const DEFAULT_SECTION: SettingsSection = "playback";

function isSection(value: string | null): value is SettingsSection {
  return value !== null && (SECTIONS as string[]).includes(value);
}

function SettingsSections() {
  const t = useT();
  const router = useRouter();
  const params = useSearchParams();

  const raw = params.get("tab");
  const section = isSection(raw) ? raw : DEFAULT_SECTION;

  const setSection = useCallback(
    (next: SettingsSection) => {
      router.replace(next === DEFAULT_SECTION ? "/settings" : `/settings?tab=${next}`);
    },
    [router],
  );

  return (
    <div className="grid w-full items-start gap-5 lg:grid-cols-[15rem_minmax(0,1fr)]">
      <div
        className={cn(
          "flex gap-1 rounded-lg bg-card p-2 max-lg:overflow-x-auto",
          "[scrollbar-width:none] [&::-webkit-scrollbar]:hidden",
          "lg:sticky lg:top-0 lg:flex-col",
          "max-md:-mx-4 max-md:rounded-none max-md:px-4",
        )}
      >
        {SECTIONS.map((key) => (
          <button
            key={key}
            type="button"
            onClick={() => setSection(key)}
            aria-current={section === key ? "page" : undefined}
            className="rounded-md px-3 py-2 text-left text-sm font-medium whitespace-nowrap text-muted-foreground transition-colors hover:bg-card hover:text-foreground aria-[current=page]:bg-accent aria-[current=page]:font-semibold aria-[current=page]:text-foreground"
          >
            {t(`settings.${key}`)}
          </button>
        ))}
      </div>

      <div className="min-w-0">
        {section === "playback" && <Playback />}
        {section === "appearance" && <Appearance />}
        {section === "recommendations" && <RecommendationStats />}
        {section === "account" && <Account />}
      </div>
    </div>
  );
}

function Panel({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="flex flex-col gap-4 rounded-lg bg-card p-4">
      <h2 className="text-section font-semibold">{title}</h2>
      {children}
    </section>
  );
}

function Choice<T extends string>({
  legend,
  hint,
  value,
  onChange,
  options,
}: {
  legend: string;
  hint: string;
  value: T;
  onChange: (value: T) => void;
  options: { value: T; label: string; hint?: string; preview?: React.ReactNode }[];
}) {
  return (
    <fieldset className="flex flex-col gap-2 border-0 p-0">
      <legend className="font-semibold">{legend}</legend>
      <p className="text-sm text-muted-foreground">{hint}</p>

      <RadioGroup.Root
        className="mt-1 grid grid-cols-[repeat(auto-fit,minmax(9rem,1fr))] gap-2"
        value={value}
        onValueChange={(next) => onChange(next as T)}
      >
        {options.map((option) => (
          <RadioGroup.Item
            key={option.value}
            value={option.value}
            className={cn(
              "flex cursor-pointer flex-col items-start gap-0.5 rounded-md border border-border p-3 text-left transition-colors outline-none",
              "hover:bg-raised focus-visible:ring-2 focus-visible:ring-ring/40",
              "data-[state=checked]:border-primary data-[state=checked]:bg-primary-soft",
            )}
          >
            {option.preview}
            <span className="font-medium">{option.label}</span>
            {option.hint && <span className="text-xs text-muted-foreground">{option.hint}</span>}
          </RadioGroup.Item>
        ))}
      </RadioGroup.Root>
    </fieldset>
  );
}

function Appearance() {
  const t = useT();
  const { locale, setLocale } = useI18n();
  const theme = useThemeChoice();

  return (
    <Panel title={t("settings.appearance")}>
      <Choice
        legend={t("settings.theme")}
        hint={t("settings.themeHint")}
        value={theme}
        onChange={setTheme}
        options={THEME_CHOICES.map((value) => ({
          value,
          label: t(`settings.theme.${value}`),
          preview: <ThemeSwatch choice={value} />,
        }))}
      />

      <Choice
        legend={t("settings.language")}
        hint={t("settings.languageHint")}
        value={locale}
        onChange={setLocale}
        options={LOCALES.map((value) => ({ value, label: LOCALE_NAMES[value] }))}
      />
    </Panel>
  );
}

const QUALITIES: { quality: AudioQuality; bitrateKbps: number | null }[] = [
  { quality: "Low", bitrateKbps: 64 },
  { quality: "Normal", bitrateKbps: 128 },
  { quality: "Original", bitrateKbps: null },
];

function Playback() {
  const t = useT();
  const settings = useSettings();
  const { crossfade, setCrossfade } = usePlayer();

  return (
    <Panel title={t("settings.playback")}>
      <Choice
        legend={t("settings.quality")}
        hint={t("settings.qualityHint")}
        value={settings.quality}
        onChange={(quality) => settings.update({ quality })}
        options={QUALITIES.map(({ quality, bitrateKbps }) => ({
          value: quality,
          label: t(`settings.quality.${quality}`),
          hint: bitrateKbps
            ? t("settings.qualityBitrate", { bitrate: bitrateKbps })
            : t("settings.qualityOriginal"),
        }))}
      />

      <Choice
        legend={t("settings.crossfade")}
        hint={t("settings.crossfadeHint")}
        value={String(crossfade)}
        onChange={(seconds) => setCrossfade(Number(seconds))}
        options={CROSSFADE_CHOICES.map((seconds) => ({
          value: String(seconds),
          label: seconds ? t("settings.crossfadeSeconds", { seconds }) : t("settings.crossfadeOff"),
        }))}
      />

      <Equalizer />

      <div className="mt-1 flex flex-col gap-1 border-t border-border pt-4 text-sm text-faint">
        <p>{t("settings.timeZone", { zone: settings.timeZone })}</p>
        <p className="max-md:hidden">{t("settings.shortcutsHint")}</p>
      </div>
    </Panel>
  );
}

function Equalizer() {
  const { locale, t } = useI18n();
  const eq = useEqualizer();

  if (!eq.supported) return null;

  const frequency = (hz: number) =>
    hz < 1000
      ? t("settings.equalizerHz", { value: hz })
      : t("settings.equalizerKhz", { value: (hz / 1000).toLocaleString(locale) });
  const decibels = (gain: number) =>
    t("settings.equalizerDb", { value: gain > 0 ? `+${gain}` : String(gain) });

  return (
    <div className="flex flex-col gap-4">
      <label className="flex cursor-pointer items-start gap-3">
        <Switch checked={eq.enabled} onCheckedChange={eq.setEnabled} className="mt-0.5" />
        <span className="flex flex-col gap-0.5">
          <span className="font-medium">{t("settings.equalizer")}</span>
          <span className="text-sm text-muted-foreground">{t("settings.equalizerHint")}</span>
        </span>
      </label>

      {eq.enabled && (
        <>
          <Choice
            legend={t("settings.equalizerPreset")}
            hint={t("settings.equalizerPresetHint")}
            value={eq.preset ?? "custom"}
            onChange={(preset) => eq.applyPreset(preset as EqualizerPreset)}
            options={(Object.keys(EQ_PRESETS) as EqualizerPreset[]).map((preset) => ({
              value: preset,
              label: t(`settings.equalizerPreset.${preset}`),
            }))}
          />

          <EqualizerCurve
            gains={eq.gains}
            onChange={eq.setGain}
            frequency={frequency}
            decibels={decibels}
            bandLabel={(hz) => t("settings.equalizerBand", { band: frequency(hz) })}
          />
        </>
      )}
    </div>
  );
}

const EQ_MIN_DB = -EQ_LIMIT_DB;

const EQ_TICKS = [EQ_LIMIT_DB, EQ_LIMIT_DB / 2, 0, EQ_MIN_DB / 2, EQ_MIN_DB];

function EqualizerCurve({
  gains,
  onChange,
  frequency,
  decibels,
  bandLabel,
}: {
  gains: readonly number[];
  onChange: (band: number, gain: number) => void;
  frequency: (hz: number) => string;
  decibels: (gain: number) => string;
  bandLabel: (hz: number) => string;
}) {
  const [focused, setFocused] = useState<number | null>(null);

  const gainAt = (event: React.PointerEvent<HTMLElement>) => {
    const rect = event.currentTarget.getBoundingClientRect();
    const ratio = (event.clientY - rect.top) / rect.height;
    return Math.round(EQ_LIMIT_DB - ratio * EQ_LIMIT_DB * 2);
  };

  const x = (band: number) => ((band + 0.5) / EQ_BANDS.length) * 100;
  const y = (gain: number) => ((EQ_LIMIT_DB - gain) / (EQ_LIMIT_DB * 2)) * 100;

  const points = [
    [0, y(gains[0])],
    ...gains.map((gain, band) => [x(band), y(gain)]),
    [100, y(gains[gains.length - 1])],
  ];
  const curve = points
    .slice(1)
    .map(([px, py], index) => {
      const before = points[Math.max(0, index - 1)];
      const from = points[index];
      const after = points[Math.min(points.length - 1, index + 2)];
      const c1 = [from[0] + (px - before[0]) / 6, from[1] + (py - before[1]) / 6];
      const c2 = [px - (after[0] - from[0]) / 6, py - (after[1] - from[1]) / 6];
      return `C ${c1.join(" ")} ${c2.join(" ")} ${px} ${py}`;
    })
    .join(" ");
  const line = `M ${points[0].join(" ")} ${curve}`;

  return (
    <div className="grid grid-cols-[2rem_minmax(0,1fr)] gap-x-2 text-2xs text-faint tabular-nums">
      <span />
      <div className="grid" style={{ gridTemplateColumns: `repeat(${EQ_BANDS.length}, 1fr)` }}>
        {gains.map((gain, band) => (
          <span key={band} className="text-center text-muted-foreground">
            {decibels(gain)}
          </span>
        ))}
      </div>

      <div className="relative">
        {EQ_TICKS.map((tick) => (
          <span
            key={tick}
            className="absolute right-0 -translate-y-1/2"
            style={{ top: `${y(tick)}%` }}
          >
            {tick > 0 ? `+${tick}` : tick}
          </span>
        ))}
      </div>

      <div className="relative mt-2 h-52">
        <svg
          viewBox="0 0 100 100"
          preserveAspectRatio="none"
          aria-hidden="true"
          className="absolute inset-0 size-full overflow-visible"
        >
          <defs>
            <linearGradient id="eq-fill" x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" stopColor="var(--primary)" stopOpacity="0.35" />
              <stop offset="100%" stopColor="var(--primary)" stopOpacity="0.02" />
            </linearGradient>
          </defs>
          {EQ_TICKS.map((tick) => (
            <line
              key={tick}
              x1="0"
              x2="100"
              y1={y(tick)}
              y2={y(tick)}
              stroke="var(--border)"
              strokeDasharray={tick === 0 ? undefined : "2 2"}
              vectorEffect="non-scaling-stroke"
            />
          ))}
          {EQ_BANDS.map((hz, band) => (
            <line
              key={hz}
              x1={x(band)}
              x2={x(band)}
              y1="0"
              y2="100"
              stroke="var(--border)"
              vectorEffect="non-scaling-stroke"
            />
          ))}
          <path d={`${line} L 100 100 L 0 100 Z`} fill="url(#eq-fill)" />
          <path
            d={line}
            fill="none"
            stroke="var(--primary)"
            strokeWidth="2"
            vectorEffect="non-scaling-stroke"
          />
        </svg>

        {EQ_BANDS.map((hz, band) => (
          <div
            key={hz}
            role="slider"
            tabIndex={0}
            aria-orientation="vertical"
            aria-label={bandLabel(hz)}
            aria-valuemin={EQ_MIN_DB}
            aria-valuemax={EQ_LIMIT_DB}
            aria-valuenow={gains[band]}
            aria-valuetext={decibels(gains[band])}
            onFocus={() => setFocused(band)}
            onBlur={() => setFocused(null)}
            onPointerDown={(event) => {
              const onHandle = (event.target as HTMLElement).dataset.handle !== undefined;
              if (event.pointerType !== "mouse" && !onHandle) return;

              event.currentTarget.setPointerCapture(event.pointerId);
              onChange(band, gainAt(event));
            }}
            onPointerMove={(event) => {
              if (event.currentTarget.hasPointerCapture(event.pointerId)) {
                onChange(band, gainAt(event));
              }
            }}
            onKeyDown={(event) => {
              const step =
                event.key === "ArrowUp" || event.key === "ArrowRight"
                  ? 1
                  : event.key === "ArrowDown" || event.key === "ArrowLeft"
                    ? -1
                    : 0;
              if (step === 0) return;

              event.preventDefault();
              onChange(band, gains[band] + step);
            }}
            className="absolute inset-y-0 cursor-ns-resize touch-pan-y outline-none"
            style={{
              left: `${(band / EQ_BANDS.length) * 100}%`,
              width: `${100 / EQ_BANDS.length}%`,
            }}
          >
            <span
              data-handle=""
              className="absolute left-1/2 grid size-11 -translate-1/2 touch-none place-items-center"
              style={{ top: `${y(gains[band])}%` }}
            >
              <span
                className={cn(
                  "pointer-events-none size-4 rounded-full border-2 border-background bg-primary shadow-art",
                  focused === band && "ring-2 ring-ring ring-offset-2 ring-offset-card",
                )}
              />
            </span>
          </div>
        ))}
      </div>

      <span />
      <div
        className="mt-2 grid text-muted-foreground"
        style={{ gridTemplateColumns: `repeat(${EQ_BANDS.length}, 1fr)` }}
      >
        {EQ_BANDS.map((hz) => (
          <span key={hz} className="text-center">
            {frequency(hz)}
          </span>
        ))}
      </div>
    </div>
  );
}

function Account() {
  const t = useT();
  const { notify } = useToast();

  const form = useForm<PasswordChangeValues>({
    resolver: zodResolver(passwordChangeSchema),
    defaultValues: { current: "", next: "", repeat: "" },
  });

  const changePassword = useMutation({
    mutationFn: ({ current, next }: PasswordChangeValues) => api.changePassword(current, next),
    onSuccess: () => {
      form.reset();
      notify(t("settings.passwordChanged"), "success");
    },
  });

  const submit = form.handleSubmit((values) => changePassword.mutate(values));

  const errors = form.formState.errors;

  return (
    <Panel title={t("settings.account")}>
      <form
        onSubmit={(event) => void submit(event)}
        className="flex max-w-sm flex-col gap-3"
        noValidate
      >
        <TextField
          label={t("settings.currentPassword")}
          type="password"
          autoComplete="current-password"
          registration={form.register("current")}
          error={errors.current && t("form.required")}
        />

        <TextField
          label={t("settings.newPassword")}
          type="password"
          autoComplete="new-password"
          registration={form.register("next")}
          error={errors.next && t("form.passwordShort", { count: limits.password.min })}
        />

        <TextField
          label={t("settings.repeatPassword")}
          type="password"
          autoComplete="new-password"
          registration={form.register("repeat")}
          error={errors.repeat && t("settings.passwordMismatch")}
        />

        <Button
          variant="primary"
          type="submit"
          className="mt-1 self-start"
          disabled={changePassword.isPending}
        >
          {t("settings.changePassword")}
        </Button>
      </form>
    </Panel>
  );
}
