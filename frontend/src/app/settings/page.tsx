// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import * as RadioGroup from "@radix-ui/react-radio-group";
import { useRouter, useSearchParams } from "next/navigation";
import { Suspense, useCallback } from "react";
import { useForm } from "react-hook-form";
import { api } from "@/lib/api";
import { limits, passwordChangeSchema, type PasswordChangeValues } from "@/lib/schemas";
import { LOCALES, LOCALE_NAMES } from "@/lib/i18n";
import { setTheme, THEME_CHOICES, useThemeChoice } from "@/lib/theme";
import { cn } from "@/lib/cn";
import { PageHeader } from "@/components/PageHeader";
import { Button } from "@/components/ui/button";
import { TextField } from "@/components/ui/form";
import { Switch } from "@/components/ui/switch";
import { useSettings } from "@/lib/useSettings";
import { useI18n, useT } from "@/contexts/I18nContext";
import { useToast } from "@/lib/useToast";
import type { AudioQuality } from "@/lib/types";

export default function SettingsPage() {
  const t = useT();

  return (
    <>
      <PageHeader compact title={t("settings.title")} />
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

/** Выбор одного из нескольких вариантов карточками: тема, язык, качество. */
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
  options: { value: T; label: string; hint?: string }[];
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
        options={THEME_CHOICES.map((value) => ({ value, label: t(`settings.theme.${value}`) }))}
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

      {/* Часовой пояс — не настройка, а факт об этом браузере: по нему режется день
          для микса дня. Отдельной строкой под чертой он
          больше не читается как настройка, у которой потеряли переключатель. */}
      <div className="mt-1 flex flex-col gap-1 border-t border-border pt-4 text-sm text-faint">
        <p>{t("settings.timeZone", { zone: settings.timeZone })}</p>
        {/* Единственное место, где о справке по клавишам сказано словами: сама она
            открывается по «?» и иначе оставалась бы секретом. На телефоне клавиатуры нет. */}
        <p className="max-md:hidden">{t("settings.shortcutsHint")}</p>
      </div>
    </Panel>
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
