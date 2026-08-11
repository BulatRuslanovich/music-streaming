"use client";

import { useState } from "react";
import { api } from "@/lib/api";
import { formatRelativeDate } from "@/lib/format";
import { useApi } from "@/lib/useApi";
import { CreateUserDialog } from "@/components/CreateUserDialog";
import { PlusIcon } from "@/components/Icons";
import { LoadError, PageHeader, Pagination, Skeleton } from "@/components/ui";

const PAGE_SIZE = 50;

export default function AdminUsersPage() {
  const [page, setPage] = useState(1);
  const [creating, setCreating] = useState(false);

  const { data, error, loading, reload } = useApi(
    () => api.adminUsers({ page, pageSize: PAGE_SIZE }),
    [page],
  );

  return (
    <>
      <PageHeader
        title="Users"
        subtitle={data ? `${data.total.toLocaleString()} accounts` : undefined}
        actions={
          <button type="button" className="button button-primary" onClick={() => setCreating(true)}>
            <PlusIcon size={16} /> Add user
          </button>
        }
      />

      {error && <LoadError message={error} onRetry={reload} />}
      {loading && !data && <Skeleton variant="row" count={6} />}

      {data && (
        <>
          {data.items.length === 0 ? (
            <p className="empty-state">No users yet.</p>
          ) : (
            <div className="admin-table" role="table" aria-label="Users">
              <div className="admin-row admin-row-head" role="row">
                <span role="columnheader">Username</span>
                <span role="columnheader">Display name</span>
                <span role="columnheader">Role</span>
                <span role="columnheader">Created</span>
              </div>

              {data.items.map((user) => (
                <div className="admin-row" role="row" key={user.id}>
                  <span role="cell">{user.username}</span>
                  <span role="cell" className="muted">
                    {user.displayName}
                  </span>
                  <span role="cell">
                    {user.isAdmin ? <span className="role-badge">Admin</span> : "User"}
                  </span>
                  <span role="cell" className="muted">
                    {formatRelativeDate(user.createdAt)}
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
