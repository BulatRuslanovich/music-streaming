"use client";

import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import { useApi } from "@/lib/useApi";
import { useFormat } from "@/lib/useFormat";
import { Cover } from "@/components/Cover";
import { PageHeader } from "@/components/ui";
import { TrashIcon } from "@/components/Icons";
import { useOffline } from "@/contexts/OfflineContext";
import { useSettings } from "@/contexts/SettingsContext";
import { useT } from "@/contexts/I18nContext";
import { useToast } from "@/contexts/ToastContext";

export default function SettingsPage() {
  const t = useT();

  return (
    <>
      <PageHeader title={t("settings.title")} />

      <div className="settings">
        <Playback />
        <Downloads />
        <Lastfm />
        <Account />
      </div>
    </>
  );
}

function Playback() {
  const t = useT();
  const settings = useSettings();

  return (
    <section className="settings-panel">
      <h2>{t("settings.playback")}</h2>

      <fieldset className="settings-field">
        <legend>{t("settings.quality")}</legend>
        <p className="muted">{t("settings.qualityHint")}</p>

        <div className="quality-options">
          {settings.qualities.map((option) => (
            <label key={option.quality} className="quality-option">
              <input
                type="radio"
                name="quality"
                checked={settings.quality === option.quality}
                onChange={() => settings.update({ quality: option.quality })}
              />
              <span className="quality-name">
                {t(`settings.quality.${option.quality}` as const)}
              </span>
              <span className="muted">
                {option.bitrateKbps
                  ? t("settings.qualityBitrate", { bitrate: option.bitrateKbps })
                  : t("settings.qualityOriginal")}
              </span>
            </label>
          ))}
        </div>
      </fieldset>

      <Toggle
        label={t("settings.dataSaver")}
        hint={t("settings.dataSaverHint")}
        checked={settings.dataSaver}
        onChange={(dataSaver) => settings.update({ dataSaver })}
      />

      {settings.networkIsSlow && !settings.dataSaver && (
        <p className="settings-suggestion">{t("settings.slowNetwork")}</p>
      )}

      <Toggle
        label={t("settings.autoplay")}
        hint={t("settings.autoplayHint")}
        checked={settings.autoplay}
        onChange={(autoplay) => settings.update({ autoplay })}
      />

      <p className="muted">{t("settings.timeZone", { zone: settings.timeZone })}</p>
    </section>
  );
}

/**
 * Скачанное: список того, что доступно без сети, и сколько это занимает. Скачивают отсюда не
 * «всё подряд», а из меню конкретного трека, альбома или плейлиста — библиотека целиком в телефон
 * не поместится, да и не нужна там целиком.
 */
function Downloads() {
  const t = useT();
  const format = useFormat();
  const offline = useOffline();

  if (!offline.supported) {
    return (
      <section className="settings-panel">
        <h2>{t("offline.title")}</h2>
        <p className="muted">{t("offline.unsupported")}</p>
      </section>
    );
  }

  return (
    <section className="settings-panel">
      <h2>{t("offline.title")}</h2>
      <p className="muted">{t("offline.hint")}</p>

      {offline.downloads.length === 0 ? (
        <p className="empty-state">{t("offline.empty")}</p>
      ) : (
        <>
          <div className="settings-row">
            <span className="muted">
              {t("offline.stored", {
                count: offline.downloads.length,
                size: format.bytes(offline.totalBytes),
              })}
            </span>
            <button type="button" className="text-button" onClick={() => void offline.clear()}>
              {t("offline.clear")}
            </button>
          </div>

          <ul className="download-list">
            {offline.downloads.map(({ track, quality }) => (
              <li key={track.id}>
                <Cover
                  albumId={track.albumId}
                  trackId={track.id}
                  hasCover={track.hasCover}
                  name={track.albumTitle ?? track.title}
                  size={36}
                />
                <span className="download-meta">
                  <span>{track.title}</span>
                  <span className="muted">
                    {track.artistName} · {t(`settings.quality.${quality}` as const)}
                  </span>
                </span>
                <button
                  type="button"
                  className="icon-button"
                  onClick={() => void offline.remove(track.id)}
                  aria-label={t("offline.remove")}
                >
                  <TrashIcon size={15} />
                </button>
              </li>
            ))}
          </ul>
        </>
      )}
    </section>
  );
}

/**
 * Подключение Last.fm уходит на саму Last.fm и возвращается на эту же страницу с отметкой в
 * адресе — поэтому результат разбирается здесь, а не там, откуда ушёл запрос.
 */
function Lastfm() {
  const t = useT();
  const format = useFormat();
  const { notify, notifyError } = useToast();

  const { data, reload } = useApi(() => api.lastfmStatus(), [], "lastfm");
  const [busy, setBusy] = useState(false);

  // Отметка читается из адреса напрямую: страница целиком клиентская, и заворачивать её в Suspense
  // ради одного параметра, который нужен ровно один раз после возврата, незачем.
  useEffect(() => {
    const outcome = new URLSearchParams(window.location.search).get("lastfm");
    if (!outcome) return;

    if (outcome === "connected") notify(t("settings.lastfmDone"), "success");
    else
      notify(t(outcome === "denied" ? "settings.lastfmDenied" : "settings.lastfmFailed"), "error");

    window.history.replaceState(null, "", window.location.pathname);
    reload();
  }, [notify, reload, t]);

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
      reload();
    } catch (error) {
      notifyError(error, t("error.generic"));
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className="settings-panel">
      <h2>{t("settings.lastfm")}</h2>
      <p className="muted">{t("settings.lastfmHint")}</p>

      {!data.available ? (
        <p className="muted">{t("settings.lastfmUnavailable")}</p>
      ) : data.username ? (
        <div className="settings-row">
          <span>
            {t("settings.lastfmConnected", { username: data.username })}
            <span className="muted">
              {" · "}
              {data.lastScrobbleAt
                ? t("settings.lastfmLast", { when: format.relativeDate(data.lastScrobbleAt) })
                : t("settings.lastfmNever")}
            </span>
          </span>
          <button
            type="button"
            className="button"
            onClick={() => void disconnect()}
            disabled={busy}
          >
            {t("settings.lastfmDisconnect")}
          </button>
        </div>
      ) : (
        <button
          type="button"
          className="button button-primary"
          onClick={() => void connect()}
          disabled={busy}
        >
          {t("settings.lastfmConnect")}
        </button>
      )}
    </section>
  );
}

function Account() {
  const t = useT();
  const { notify, notifyError } = useToast();

  const [current, setCurrent] = useState("");
  const [next, setNext] = useState("");
  const [repeat, setRepeat] = useState("");
  const [busy, setBusy] = useState(false);

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();

    if (next !== repeat) {
      notify(t("settings.passwordMismatch"), "error");
      return;
    }

    setBusy(true);

    try {
      await api.changePassword(current, next);

      setCurrent("");
      setNext("");
      setRepeat("");
      notify(t("settings.passwordChanged"), "success");
    } catch (error) {
      notifyError(error, t("settings.passwordFailed"));
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className="settings-panel">
      <h2>{t("settings.account")}</h2>

      <form className="settings-form" onSubmit={(event) => void submit(event)}>
        <label>
          {t("settings.currentPassword")}
          <input
            type="password"
            autoComplete="current-password"
            value={current}
            onChange={(event) => setCurrent(event.target.value)}
            required
          />
        </label>

        <label>
          {t("settings.newPassword")}
          <input
            type="password"
            autoComplete="new-password"
            value={next}
            onChange={(event) => setNext(event.target.value)}
            minLength={8}
            required
          />
        </label>

        <label>
          {t("settings.repeatPassword")}
          <input
            type="password"
            autoComplete="new-password"
            value={repeat}
            onChange={(event) => setRepeat(event.target.value)}
            minLength={8}
            required
          />
        </label>

        <button type="submit" className="button button-primary" disabled={busy}>
          {t("settings.changePassword")}
        </button>
      </form>
    </section>
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
    <label className="settings-toggle">
      <input
        type="checkbox"
        checked={checked}
        onChange={(event) => onChange(event.target.checked)}
      />
      <span>
        <span className="settings-toggle-label">{label}</span>
        <span className="muted">{hint}</span>
      </span>
    </label>
  );
}
