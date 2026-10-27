// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect } from "react";
import { LogOutIcon } from "lucide-react";
import { api } from "@/lib/api";
import { cn } from "@/lib/cn";
import { describeDevice } from "@/lib/playback/deviceName";
import { queries } from "@/lib/queries";
import { useFormat } from "@/lib/useFormat";
import { useToast } from "@/lib/useToast";
import type { AccountSession, SignInRecord } from "@/lib/types";
import { useI18n } from "@/contexts/I18nContext";
import { Button } from "@/components/ui/button";

function deviceOf(entry: Pick<AccountSession, "deviceName" | "userAgent">): string {
  return entry.deviceName || (entry.userAgent ? describeDevice(entry.userAgent) : "") || "—";
}

export function AccountSecurity() {
  return (
    <>
      <Sessions />
      <SingleSignOn />
      <SignIns />
    </>
  );
}

function Sessions() {
  const { t } = useI18n();
  const format = useFormat();
  const { notify, notifyError } = useToast();
  const client = useQueryClient();
  const sessions = useQuery(queries.sessions());
  const refresh = () => void client.invalidateQueries({ queryKey: ["sessions"] });

  const end = useMutation({
    mutationFn: (id: string) => api.endSession(id),
    onSuccess: refresh,
    onError: (error) => notifyError(error),
  });
  const endOthers = useMutation({
    mutationFn: () => api.endOtherSessions(),
    onSuccess: (result) => {
      notify(t("account.endedOthers", { count: result.ended }), "success");
      refresh();
    },
    onError: (error) => notifyError(error),
  });

  const list = sessions.data ?? [];

  return (
    <section className="flex flex-col gap-3 rounded-lg bg-card p-5 max-md:p-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 className="font-display text-heading">{t("account.sessions")}</h2>
        {list.some((session) => !session.current) && (
          <Button
            variant="outline"
            size="sm"
            disabled={endOthers.isPending}
            onClick={() => endOthers.mutate()}
          >
            {t("account.endOthers")}
          </Button>
        )}
      </div>
      <ul className="flex flex-col divide-y divide-border">
        {list.map((session) => (
          <li key={session.id} className="flex items-center gap-3 py-2.5">
            <div className="flex min-w-0 flex-1 flex-col">
              <span className="truncate font-medium">
                {deviceOf(session)}
                {session.current && (
                  <span className="text-primary"> · {t("account.sessionThis")}</span>
                )}
              </span>
              <span className="truncate text-xs text-muted-foreground">
                {[
                  session.ip,
                  t("account.sessionLastUsed", { date: format.playedAt(session.lastUsedAt) }),
                  session.longLived && t("account.sessionDevice"),
                ]
                  .filter(Boolean)
                  .join(" · ")}
              </span>
            </div>
            {!session.current && (
              <Button
                variant="ghost"
                size="icon-sm"
                disabled={end.isPending}
                onClick={() => end.mutate(session.id)}
                aria-label={t("account.endSession")}
                title={t("account.endSession")}
              >
                <LogOutIcon size={16} />
              </Button>
            )}
          </li>
        ))}
      </ul>
    </section>
  );
}

function SingleSignOn() {
  const { t, locale } = useI18n();
  const { notify, notifyError } = useToast();
  const client = useQueryClient();
  const providers = useQuery(queries.providers());
  const identities = useQuery({ ...queries.identities(), enabled: Boolean(providers.data?.sso) });

  const unlink = useMutation({
    mutationFn: () => api.unlinkIdentities(),
    onSuccess: () => void client.invalidateQueries({ queryKey: ["identities"] }),
    onError: (error) => notifyError(error),
  });

  useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    const outcome = params.get("sso");
    if (!outcome) return;
    if (outcome === "linked") notify(t("account.sso.linked"), "success");
    else
      notifyError(new Error(t(outcome === "taken" ? "account.sso.taken" : "account.sso.failed")));
    params.delete("sso");
    const rest = params.toString();
    window.history.replaceState(null, "", `${window.location.pathname}${rest ? `?${rest}` : ""}`);
  }, [notify, notifyError, t]);

  const sso = providers.data?.sso;
  if (!sso) return null;

  const linked = identities.data?.[0];

  return (
    <section className="flex flex-wrap items-center justify-between gap-3 rounded-lg bg-card p-5 max-md:p-4">
      <div className="flex flex-col gap-0.5">
        <h2 className="font-display text-heading">{t("account.sso", { name: sso.name })}</h2>
        {linked && (
          <span className="text-sm text-muted-foreground">
            {t("account.ssoLinked", {
              date: new Date(linked.linkedAt).toLocaleDateString(locale, {
                day: "numeric",
                month: "long",
              }),
            })}
          </span>
        )}
      </div>
      {linked ? (
        <Button
          variant="outline"
          size="sm"
          disabled={unlink.isPending}
          onClick={() => unlink.mutate()}
        >
          {t("account.ssoUnlink")}
        </Button>
      ) : (
        <Button variant="outline" size="sm" asChild>
          <a href="/api/auth/oidc/start?intent=link">{t("account.ssoLink")}</a>
        </Button>
      )}
    </section>
  );
}

function SignIns() {
  const { t } = useI18n();
  const format = useFormat();
  const signIns = useQuery(queries.signIns());

  const describe = (entry: SignInRecord) =>
    [
      t(`account.signIn.${entry.method}`),
      !entry.success &&
        [
          t("account.signIn.failed"),
          entry.reason && t(`account.signIn.reason.${entry.reason}` as never),
        ]
          .filter(Boolean)
          .join(": "),
      deviceOf(entry),
      entry.ip,
    ]
      .filter(Boolean)
      .join(" · ");

  if (!signIns.data || signIns.data.length === 0) return null;

  return (
    <section className="flex flex-col gap-3 rounded-lg bg-card p-5 max-md:p-4">
      <h2 className="font-display text-heading">{t("account.signIns")}</h2>
      <ul className="flex flex-col divide-y divide-border text-sm">
        {signIns.data.map((entry, index) => (
          <li
            key={`${entry.occurredAt}-${index}`}
            className="flex items-center justify-between gap-3 py-2"
          >
            <span className={cn("min-w-0 truncate", !entry.success && "text-destructive")}>
              {describe(entry)}
            </span>
            <span className="shrink-0 text-xs text-muted-foreground tabular-nums">
              {format.playedAt(entry.occurredAt)}
            </span>
          </li>
        ))}
      </ul>
    </section>
  );
}
