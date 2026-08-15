"use client";

import { useState } from "react";
import { api } from "@/lib/api";
import { useFormat } from "@/lib/useFormat";
import { usePagedApi } from "@/lib/usePagedApi";
import { CreateUserDialog } from "@/components/CreateUserDialog";
import { PlusIcon } from "@/components/Icons";
import { LoadError, PageHeader, Pagination, Skeleton } from "@/components/ui";
import { useAuth } from "@/contexts/AuthContext";
import { useT } from "@/contexts/I18nContext";
import { useToast } from "@/contexts/ToastContext";
import type { AdminUser } from "@/lib/types";

const PAGE_SIZE = 50;

export default function AdminUsersPage() {
  const t = useT();
  const format = useFormat();
  const { user: signedIn } = useAuth();
  const { notify, notifyError } = useToast();

  const [creating, setCreating] = useState(false);
  const [busy, setBusy] = useState<string | null>(null);

  const { data, error, loading, reload, setPage } = usePagedApi(
    (page) => api.adminUsers({ page, pageSize: PAGE_SIZE }),
    [],
    "adminUsers",
  );

  /**
   * Всё здесь необратимо в глазах пользователя, поэтому каждое действие подтверждается словами о
   * том, что именно случится, а не одним «вы уверены?».
   */
  const run = async (user: AdminUser, question: string, action: () => Promise<unknown>) => {
    if (!window.confirm(question)) return;

    setBusy(user.id);

    try {
      await action();
      notify(t("admin.actionDone"), "success");
      reload();
    } catch (reason) {
      notifyError(reason, t("admin.actionFailed"));
    } finally {
      setBusy(null);
    }
  };

  const resetPassword = async (user: AdminUser) => {
    const password = window.prompt(t("admin.resetPasswordFor", { username: user.username }));
    if (!password) return;

    setBusy(user.id);

    try {
      await api.resetUserPassword(user.id, password);
      notify(t("admin.passwordReset"), "success");
    } catch (reason) {
      notifyError(reason, t("admin.actionFailed"));
    } finally {
      setBusy(null);
    }
  };

  return (
    <>
      <PageHeader
        title={t("admin.users")}
        subtitle={data ? t("count.accounts", { count: data.total }) : undefined}
        actions={
          <button type="button" className="button button-primary" onClick={() => setCreating(true)}>
            <PlusIcon size={16} /> {t("admin.addUser")}
          </button>
        }
      />

      {error && <LoadError message={error} onRetry={reload} />}
      {loading && !data && <Skeleton variant="row" count={6} />}

      {data && (
        <>
          {data.items.length === 0 ? (
            <p className="empty-state">{t("admin.empty")}</p>
          ) : (
            <div
              className="admin-table admin-table-users"
              role="table"
              aria-label={t("admin.users")}
            >
              <div className="admin-row admin-row-head" role="row">
                <span role="columnheader">{t("field.username")}</span>
                <span role="columnheader">{t("field.role")}</span>
                <span role="columnheader">{t("admin.status")}</span>
                <span role="columnheader">{t("field.created")}</span>
                <span role="columnheader">{t("admin.actions")}</span>
              </div>

              {data.items.map((user) => {
                // Себя нельзя ни отключить, ни разжаловать: это самый быстрый способ остаться без
                // доступа к админке вообще, и сервер такое всё равно откажется делать.
                const isSelf = user.id === signedIn?.id;
                const pending = busy === user.id;

                return (
                  <div className="admin-row" role="row" key={user.id}>
                    <span role="cell">
                      {user.username}
                      <span className="muted"> · {user.displayName}</span>
                    </span>

                    <span role="cell">
                      {user.isAdmin ? (
                        <span className="role-badge">{t("admin.roleAdmin")}</span>
                      ) : (
                        t("admin.roleUser")
                      )}
                    </span>

                    <span role="cell">
                      <span className={`status-badge ${user.isActive ? "is-active" : ""}`}>
                        {user.isActive ? t("admin.active") : t("admin.inactive")}
                      </span>
                    </span>

                    <span role="cell" className="muted">
                      {format.relativeDate(user.createdAt)}
                    </span>

                    <span role="cell" className="admin-actions">
                      <button
                        type="button"
                        className="text-button"
                        disabled={pending}
                        onClick={() => void resetPassword(user)}
                      >
                        {t("admin.resetPassword")}
                      </button>

                      <button
                        type="button"
                        className="text-button"
                        disabled={pending}
                        onClick={() =>
                          void run(
                            user,
                            t("admin.confirmRevoke", { username: user.username }),
                            () => api.revokeUserSessions(user.id),
                          )
                        }
                      >
                        {t("admin.revokeSessions")}
                      </button>

                      <button
                        type="button"
                        className="text-button"
                        disabled={pending || isSelf}
                        onClick={() =>
                          void run(
                            user,
                            t(
                              user.isAdmin ? "admin.confirmRemoveAdmin" : "admin.confirmMakeAdmin",
                              {
                                username: user.username,
                              },
                            ),
                            () => api.setUserRole(user.id, !user.isAdmin),
                          )
                        }
                      >
                        {t(user.isAdmin ? "admin.removeAdmin" : "admin.makeAdmin")}
                      </button>

                      <button
                        type="button"
                        className="text-button is-danger"
                        disabled={pending || isSelf}
                        onClick={() =>
                          void run(
                            user,
                            t(
                              user.isActive ? "admin.confirmDeactivate" : "admin.confirmReactivate",
                              {
                                username: user.username,
                              },
                            ),
                            () => api.setUserActive(user.id, !user.isActive),
                          )
                        }
                      >
                        {t(user.isActive ? "admin.deactivate" : "admin.reactivate")}
                      </button>
                    </span>
                  </div>
                );
              })}
            </div>
          )}

          <Pagination result={data} onChange={setPage} />
        </>
      )}

      {creating && <CreateUserDialog onClose={() => setCreating(false)} onCreated={reload} />}
    </>
  );
}
