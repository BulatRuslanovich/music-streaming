// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useMutation } from "@tanstack/react-query";
import * as RadioGroup from "@radix-ui/react-radio-group";
import { useRouter, useSearchParams } from "next/navigation";
import { Suspense, useCallback } from "react";
import { useForm } from "react-hook-form";
import { api } from "@/lib/api";
import { limits, passwordChangeSchema, type PasswordChangeValues } from "@/lib/schemas";
import { LOCALES, LOCALE_NAMES } from "@/lib/i18n";
import { setTheme, THEME_CHOICES, useThemeChoice } from "@/lib/theme";
import { CROSSFADE_CHOICES } from "@/lib/playback/crossfade";
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

type SettingsSection = "playback" | "appearance" | "account";

const SECTIONS: SettingsSection[] = ["playback", "appearance", "account"];

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

      <label className="flex cursor-pointer items-start gap-3">
        <Switch
          checked={settings.dataSaver}
          onCheckedChange={(dataSaver) => settings.update({ dataSaver })}
          className="mt-0.5"
        />
        <span className="flex flex-col gap-0.5">
          <span className="font-medium">{t("settings.dataSaver")}</span>
          <span className="text-sm text-muted-foreground">{t("settings.dataSaverHint")}</span>
        </span>
      </label>

      <div className="mt-1 flex flex-col gap-1 border-t border-border pt-4 text-sm text-faint">
        <p>{t("settings.timeZone", { zone: settings.timeZone })}</p>
        <p className="max-md:hidden">{t("settings.shortcutsHint")}</p>
      </div>
    </Panel>
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
