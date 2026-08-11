"use client";

import { useState } from "react";
import { api } from "@/lib/api";
import { useFormat } from "@/lib/useFormat";
import { useApi } from "@/lib/useApi";
import { CreateUserDialog } from "@/components/CreateUserDialog";
import { PlusIcon } from "@/components/Icons";
import { LoadError, PageHeader, Pagination, Skeleton } from "@/components/ui";
import { useT } from "@/contexts/I18nContext";

const PAGE_SIZE = 50;

export default function AdminUsersPage() {
  const t = useT();
  const format = useFormat();

  const [page, setPage] = useState(1);
  const [creating, setCreating] = useState(false);

  const { data, error, loading, reload } = useApi(
    () => api.adminUsers({ page, pageSize: PAGE_SIZE }),
    [page],
  );

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
            <div className="admin-table" role="table" aria-label={t("admin.users")}>
              <div className="admin-row admin-row-head" role="row">
                <span role="columnheader">{t("field.username")}</span>
                <span role="columnheader">{t("field.displayName")}</span>
                <span role="columnheader">{t("field.role")}</span>
                <span role="columnheader">{t("field.created")}</span>
              </div>

              {data.items.map((user) => (
                <div className="admin-row" role="row" key={user.id}>
                  <span role="cell">{user.username}</span>
                  <span role="cell" className="muted">
                    {user.displayName}
                  </span>
                  <span role="cell">
                    {user.isAdmin ? <span className="role-badge">{t("admin.roleAdmin")}</span> : t("admin.roleUser")}
                  </span>
                  <span role="cell" className="muted">
                    {format.relativeDate(user.createdAt)}
                  </span>
                </div>
              ))}
            </div>
          )}

          <Pagination
            page={data.page}
            totalPages={data.totalPages}
            onChange={(next) => {
              setPage(next);
              window.scrollTo({ top: 0 });
            }}
          />
        </>
      )}

      {creating && <CreateUserDialog onClose={() => setCreating(false)} onCreated={reload} />}
    </>
  );
}
