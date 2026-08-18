"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useQuery } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { useForm } from "react-hook-form";
import { api } from "@/lib/api";
import { queries } from "@/lib/queries";
import { limits, passwordChangeSchema, type PasswordChangeValues } from "@/lib/schemas";
import { useFormat } from "@/lib/useFormat";
import { LOCALES, LOCALE_NAMES, type Locale } from "@/lib/i18n";
import { setTheme, useTheme, type Theme } from "@/lib/theme";
import { Cover } from "@/components/Cover";
import { PageHeader } from "@/components/PageHeader";
import { Button } from "@/components/ui/button";
import { Surface } from "@/components/ui/card";
import { TextField } from "@/components/ui/form";
import { RadioCard, RadioGroup } from "@/components/ui/radio-group";
import { Switch } from "@/components/ui/switch";
import { TrashIcon } from "@/components/Icons";
import { useOffline } from "@/contexts/OfflineContext";
import { useSettings } from "@/contexts/SettingsContext";
import { useI18n, useT } from "@/contexts/I18nContext";
import { useToast } from "@/contexts/ToastContext";
import type { AudioQuality } from "@/lib/types";

export default function SettingsPage() {
  const t = useT();

  return (
    <>
      <PageHeader title={t("settings.title")} />

      <div className="flex max-w-3xl flex-col gap-5">
        <Appearance />
        <Playback />
        <Downloads />
        <Lastfm />
        <Account />
      </div>
    </>
  );
}

function Panel({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <Surface className="flex flex-col gap-4">
      <h2 className="text-lg">{title}</h2>
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
        <span className="font-semibold">{label}</span>
        <span className="text-sm text-muted-foreground">{hint}</span>
      </span>
    </label>
  );
}

function Appearance() {
  const t = useT();
  const { locale, setLocale } = useI18n();
  const theme = useTheme();

  return (
    <Panel title={t("settings.appearance")}>
      <fieldset className="flex flex-col gap-2 border-0 p-0">
        <legend className="font-semibold">{t("settings.theme")}</legend>
        <p className="text-sm text-muted-foreground">{t("settings.themeHint")}</p>

        <RadioGroup
          className="mt-1"
          value={theme}
          onValueChange={(next) => setTheme(next as Theme)}
        >
          <RadioCard value="dark" label={t("settings.themeDark")} />
          <RadioCard value="light" label={t("settings.themeLight")} />
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
          {settings.qualities.map((option) => (
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

      <Toggle
        label={t("settings.autoplay")}
        hint={t("settings.autoplayHint")}
        checked={settings.autoplay}
        onChange={(autoplay) => settings.update({ autoplay })}
      />

      <p className="text-sm text-muted-foreground">
        {t("settings.timeZone", { zone: settings.timeZone })}
      </p>
    </Panel>
  );
}

function Downloads() {
  const t = useT();
  const format = useFormat();
  const offline = useOffline();

  if (!offline.supported) {
    return (
      <Panel title={t("offline.title")}>
        <p className="text-sm text-muted-foreground">{t("offline.unsupported")}</p>
      </Panel>
    );
  }

  return (
    <Panel title={t("offline.title")}>
      <p className="text-sm text-muted-foreground">{t("offline.hint")}</p>

      {offline.downloads.length === 0 ? (
        <p className="text-muted-foreground">{t("offline.empty")}</p>
      ) : (
        <>
          <div className="flex flex-wrap items-center justify-between gap-3">
            <span className="text-sm text-muted-foreground">
              {t("offline.stored", {
                count: offline.downloads.length,
                size: format.bytes(offline.totalBytes),
              })}
            </span>
            <Button variant="text" size="auto" onClick={() => void offline.clear()}>
              {t("offline.clear")}
            </Button>
          </div>

          <ul className="flex flex-col gap-1">
            {offline.downloads.map(({ track, quality }) => (
              <li key={track.id} className="flex items-center gap-3 rounded-md p-2 hover:bg-raised">
                <Cover
                  albumId={track.albumId}
                  trackId={track.id}
                  hasCover={track.hasCover}
                  name={track.albumTitle ?? track.title}
                  size={36}
                />
                <span className="flex min-w-0 flex-1 flex-col">
                  <span className="truncate text-sm">{track.title}</span>
                  <span className="truncate text-sm text-muted-foreground">
                    {track.artistName} · {t(`settings.quality.${quality}` as const)}
                  </span>
                </span>
                <Button
                  variant="ghost"
                  size="icon"
                  onClick={() => void offline.remove(track.id)}
                  aria-label={t("offline.remove")}
                >
                  <TrashIcon size={15} />
                </Button>
              </li>
            ))}
          </ul>
        </>
      )}
    </Panel>
  );
}

function Lastfm() {
  const t = useT();
  const format = useFormat();
  const { notify, notifyError } = useToast();

  const status = useQuery(queries.lastfmStatus());
  const [busy, setBusy] = useState(false);

  const refetch = status.refetch;
  useEffect(() => {
    const outcome = new URLSearchParams(window.location.search).get("lastfm");
    if (!outcome) return;

    if (outcome === "connected") notify(t("settings.lastfmDone"), "success");
    else
      notify(t(outcome === "denied" ? "settings.lastfmDenied" : "settings.lastfmFailed"), "error");

    window.history.replaceState(null, "", window.location.pathname);
    void refetch();
  }, [notify, refetch, t]);

  const data = status.data;
  if (!data) return null;

  const connect = async () => {
    setBusy(true);

    try {
      const { authorizeUrl } = await api.lastfmConnect();
      window.location.href = authorizeUrl;
    } catch (error) {
      notifyError(error, t("settings.lastfmFailed"));
      setBusy(false);
    }
  };

  const disconnect = async () => {
    setBusy(true);

    try {
      await api.lastfmDisconnect();
      void refetch();
    } catch (error) {
      notifyError(error, t("error.generic"));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Panel title={t("settings.lastfm")}>
      <p className="text-sm text-muted-foreground">{t("settings.lastfmHint")}</p>

      {!data.available ? (
        <p className="text-sm text-muted-foreground">{t("settings.lastfmUnavailable")}</p>
      ) : data.username ? (
        <div className="flex flex-wrap items-center justify-between gap-3">
          <span>
            {t("settings.lastfmConnected", { username: data.username })}
            <span className="text-muted-foreground">
              {" · "}
              {data.lastScrobbleAt
                ? t("settings.lastfmLast", { when: format.relativeDate(data.lastScrobbleAt) })
                : t("settings.lastfmNever")}
            </span>
          </span>
          <Button onClick={() => void disconnect()} disabled={busy}>
            {t("settings.lastfmDisconnect")}
          </Button>
        </div>
      ) : (
        <Button
          variant="primary"
          className="self-start"
          onClick={() => void connect()}
          disabled={busy}
        >
          {t("settings.lastfmConnect")}
        </Button>
      )}
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
