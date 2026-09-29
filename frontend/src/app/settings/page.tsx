// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useRouter, useSearchParams } from "next/navigation";
import { Suspense, useCallback } from "react";
import { useForm } from "react-hook-form";
import { api } from "@/lib/api";
import { limits, passwordChangeSchema, type PasswordChangeValues } from "@/lib/schemas";
import { LOCALES, LOCALE_NAMES, type Locale } from "@/lib/i18n";
import { setTheme, useThemeChoice, useThemeChoices, type ThemeChoice } from "@/lib/theme";
import { setVisualizerEnabled, useVisualizerEnabled } from "@/lib/useVisualizerEnabled";
import { cn } from "@/lib/cn";
import { shelfScrollbar } from "@/components/collection/layout";
import { PageHeader } from "@/components/PageHeader";
import { Button } from "@/components/ui/button";
import { Surface } from "@/components/ui/card";
import { TextField } from "@/components/ui/form";
import { RadioCard, RadioGroup } from "@/components/ui/radio-group";
import { Switch } from "@/components/ui/switch";
import { useSettings } from "@/contexts/SettingsContext";
import { useI18n, useT } from "@/contexts/I18nContext";
import { useToast } from "@/contexts/ToastContext";
import type { AudioQuality } from "@/lib/types";

export default function SettingsPage() {
  const t = useT();

  return (
    <>
      <PageHeader title={t("settings.title")} />
      <Suspense fallback={null}>
        <SettingsSections />
      </Suspense>
    </>
  );
}

type SettingsSection = "playback" | "appearance" | "account";

const SECTIONS: SettingsSection[] = ["playback", "appearance", "account"];

const DEFAULT_SECTION: SettingsSection = "playback";

function isSection(value: string | null): value is SettingsSection {
  return value !== null && (SECTIONS as string[]).includes(value);
}

/**
 * Раздел живёт в адресе: без этого нельзя дать ссылку «настройки → воспроизведение», а
 * кнопка «назад» уносит со страницы целиком вместо возврата на прошлую вкладку.
 */
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

  const sections: { key: SettingsSection; label: string }[] = [
    { key: "playback", label: t("settings.playback") },
    { key: "appearance", label: t("settings.appearance") },
    { key: "account", label: t("settings.account") },
  ];

  return (
    <div className="grid w-full items-start gap-5 lg:grid-cols-[15rem_minmax(0,1fr)]">
      {/*
        Липкой полоса остаётся только там, где она сбоку. На узком экране она стояла поперёк
        сверху и с тем же `sticky` наезжала на настройки: карточка со скруглениями висела
        посреди списка и разрезала ближайший переключатель пополам. Внизу же она уходит под
        обрез страницы — и обрезанная вкладка у самого края читается как «пролистай», а не
        как сломанная карточка; свою полосу прокрутки лента прячет по той же причине, что и
        витрины на главной.
      */}
      <Surface
        className={cn(
          "flex gap-1 p-2 max-lg:overflow-x-auto",
          shelfScrollbar,
          "lg:sticky lg:top-0 lg:flex-col",
          "max-md:-mx-4 max-md:rounded-none max-md:px-4",
        )}
      >
        {sections.map((item) => (
          <button
            key={item.key}
            type="button"
            onClick={() => setSection(item.key)}
            aria-current={section === item.key ? "page" : undefined}
            className="rounded-lg px-3 py-2.5 text-left text-sm font-medium whitespace-nowrap text-muted-foreground transition-colors hover:bg-card hover:text-foreground aria-[current=page]:bg-accent aria-[current=page]:font-semibold aria-[current=page]:text-foreground"
          >
            {item.label}
          </button>
        ))}
      </Surface>

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
    <Surface className="flex flex-col gap-4">
      <h2 className="text-section font-semibold">{title}</h2>
      {children}
    </Surface>
  );
}

function Toggle({
  label,
  hint,
  checked,
  onChange,
}: {
  label: string;
  hint: string;
  checked: boolean;
  onChange: (value: boolean) => void;
}) {
  return (
    <label className="flex cursor-pointer items-start gap-3">
      <Switch checked={checked} onCheckedChange={onChange} className="mt-0.5" />
      <span className="flex flex-col gap-0.5">
        <span className="font-medium">{label}</span>
        <span className="text-sm text-muted-foreground">{hint}</span>
      </span>
    </label>
  );
}

function Appearance() {
  const t = useT();
  const { locale, setLocale } = useI18n();
  const theme = useThemeChoice();
  const themeChoices = useThemeChoices();

  return (
    <Panel title={t("settings.appearance")}>
      <fieldset className="flex flex-col gap-2 border-0 p-0">
        <legend className="font-semibold">{t("settings.theme")}</legend>
        <p className="text-sm text-muted-foreground">{t("settings.themeHint")}</p>

        <RadioGroup
          className="mt-1"
          value={theme}
          onValueChange={(next) => setTheme(next as ThemeChoice)}
        >
          {themeChoices.map((value) => (
            <RadioCard key={value} value={value} label={t(`settings.theme.${value}`)} />
          ))}
        </RadioGroup>
      </fieldset>

      <fieldset className="flex flex-col gap-2 border-0 p-0">
        <legend className="font-semibold">{t("settings.language")}</legend>
        <p className="text-sm text-muted-foreground">{t("settings.languageHint")}</p>

        <RadioGroup
          className="mt-1"
          value={locale}
          onValueChange={(next) => setLocale(next as Locale)}
        >
          {LOCALES.map((value) => (
            <RadioCard key={value} value={value} label={LOCALE_NAMES[value]} />
          ))}
        </RadioGroup>
      </fieldset>
    </Panel>
  );
}

// Ступени те же, что `AudioBitrates` на сервере: ниже оригинала качество бывает только в HLS.
const QUALITIES: { quality: AudioQuality; bitrateKbps: number | null }[] = [
  { quality: "Low", bitrateKbps: 64 },
  { quality: "Normal", bitrateKbps: 128 },
  { quality: "High", bitrateKbps: 192 },
  { quality: "Original", bitrateKbps: null },
];

function Playback() {
  const t = useT();
  const settings = useSettings();

  return (
    <Panel title={t("settings.playback")}>
      <fieldset className="flex flex-col gap-2 border-0 p-0">
        <legend className="font-semibold">{t("settings.quality")}</legend>
        <p className="text-sm text-muted-foreground">{t("settings.qualityHint")}</p>

        <RadioGroup
          className="mt-1"
          value={settings.quality}
          onValueChange={(quality) => settings.update({ quality: quality as AudioQuality })}
        >
          {QUALITIES.map((option) => (
            <RadioCard
              key={option.quality}
              value={option.quality}
              label={t(`settings.quality.${option.quality}` as const)}
              hint={
                option.bitrateKbps
                  ? t("settings.qualityBitrate", { bitrate: option.bitrateKbps })
                  : t("settings.qualityOriginal")
              }
            />
          ))}
        </RadioGroup>
      </fieldset>

      <Toggle
        label={t("settings.dataSaver")}
        hint={t("settings.dataSaverHint")}
        checked={settings.dataSaver}
        onChange={(dataSaver) => settings.update({ dataSaver })}
      />

      {settings.networkIsSlow && !settings.dataSaver && (
        <p className="rounded-md bg-primary-soft px-3 py-2.5 text-sm">
          {t("settings.slowNetwork")}
        </p>
      )}

      <Visualizer />

      {/* Часовой пояс — не настройка, а факт об этом браузере: по нему подбираются полки
          по времени суток и режется день в статистике. Отдельной строкой под чертой он
          больше не читается как настройка, у которой потеряли переключатель. */}
      <p className="mt-1 border-t border-border pt-4 text-sm text-faint">
        {t("settings.timeZone", { zone: settings.timeZone })}
      </p>
    </Panel>
  );
}

/**
 * Спектр — настройка устройства, а не учётной записи: она зависит от того, тянет ли
 * процессор лишний кадр, а не от вкуса слушателя. Поэтому живёт в localStorage и не
 * ходит на сервер (иначе это свойство опции, правило валидации, `.env.example` и
 * маппинг в docker-compose ради переключателя, у которого нет смысла между машинами).
 */
function Visualizer() {
  const t = useT();
  const enabled = useVisualizerEnabled();

  return (
    <Toggle
      label={t("settings.visualizer")}
      hint={t("settings.visualizerHint")}
      checked={enabled}
      onChange={setVisualizerEnabled}
    />
  );
}

function Account() {
  const t = useT();
  const { notify, notifyError } = useToast();

  const form = useForm<PasswordChangeValues>({
    resolver: zodResolver(passwordChangeSchema),
    defaultValues: { current: "", next: "", repeat: "" },
  });

  const submit = form.handleSubmit(async ({ current, next }) => {
    try {
      await api.changePassword(current, next);
      form.reset();
      notify(t("settings.passwordChanged"), "success");
    } catch (error) {
      notifyError(error, t("settings.passwordFailed"));
    }
  });

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
          disabled={form.formState.isSubmitting}
        >
          {t("settings.changePassword")}
        </Button>
      </form>
    </Panel>
  );
}
