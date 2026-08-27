// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import dynamic from "next/dynamic";
import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { api } from "@/lib/api";
import { queries } from "@/lib/queries";
import { useFormat } from "@/lib/useFormat";
import { usePage } from "@/lib/usePage";
import { PageHeader } from "@/components/PageHeader";
import { Pagination } from "@/components/PageToolbar";
import { Query } from "@/components/Query";
import { useConfirm } from "@/components/ui/alert-dialog";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Cell, HeaderCell, Row, Table } from "@/components/ui/table";
import { PlusIcon } from "@/components/Icons";
import { useAuth } from "@/contexts/AuthContext";
import { useT } from "@/contexts/I18nContext";
import { useToast } from "@/contexts/ToastContext";
import type { AdminUser } from "@/lib/types";

const CreateUserDialog = dynamic(() =>
  import("@/components/CreateUserDialog").then((m) => m.CreateUserDialog),
);
const ResetPasswordDialog = dynamic(() =>
  import("@/components/ResetPasswordDialog").then((m) => m.ResetPasswordDialog),
);

const PAGE_SIZE = 50;

const columns = "grid-cols-[minmax(0,1.6fr)_0.7fr_0.7fr_0.8fr_minmax(0,1.4fr)]";

export default function AdminUsersPage() {
  const t = useT();
  const format = useFormat();
  const { user: signedIn } = useAuth();
  const { notify, notifyError } = useToast();
  const [confirm, confirmDialog] = useConfirm();

  const [creating, setCreating] = useState(false);
  const [resetting, setResetting] = useState<AdminUser | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [page, setPage] = usePage([]);

  const users = useQuery(queries.adminUsers({ page, pageSize: PAGE_SIZE }));

  const refresh = () => void users.refetch();

  const run = async (user: AdminUser, action: () => Promise<unknown>) => {
    setBusy(user.id);

    try {
      await action();
      notify(t("admin.actionDone"), "success");
      refresh();
    } catch (reason) {
      notifyError(reason, t("admin.actionFailed"));
    } finally {
      setBusy(null);
    }
  };

  const ask = (user: AdminUser, question: string, action: () => Promise<unknown>) =>
    confirm({
      title: question,
      destructive: true,
      action: () => void run(user, action),
    });

  return (
    <>
      <PageHeader
        title={t("admin.users")}
        subtitle={users.data ? t("count.accounts", { count: users.data.total }) : undefined}
        actions={
          <Button variant="primary" onClick={() => setCreating(true)}>
            <PlusIcon size={16} /> {t("admin.addUser")}
          </Button>
        }
      />

      <Query result={users} skeleton="row" empty={{ title: t("admin.empty") }}>
        {(data) => (
          <>
            <Table aria-label={t("admin.users")}>
              <Row head className={columns}>
                <HeaderCell>{t("field.username")}</HeaderCell>
                <HeaderCell>{t("field.role")}</HeaderCell>
                <HeaderCell>{t("admin.status")}</HeaderCell>
                <HeaderCell>{t("field.created")}</HeaderCell>
                <HeaderCell>{t("admin.actions")}</HeaderCell>
              </Row>

              {data.items.map((user) => {
                const isSelf = user.id === signedIn?.id;
                const pending = busy === user.id;

                return (
                  <Row key={user.id} className={columns}>
                    <Cell className="truncate">
                      {user.username}
                      <span className="text-muted-foreground"> · {user.displayName}</span>
                    </Cell>

                    <Cell>
                      {user.isAdmin ? <Badge>{t("admin.roleAdmin")}</Badge> : t("admin.roleUser")}
                    </Cell>

                    <Cell>
                      <Badge variant={user.isActive ? "primary" : "neutral"}>
                        {user.isActive ? t("admin.active") : t("admin.inactive")}
                      </Badge>
                    </Cell>

                    <Cell className="text-muted-foreground">
                      {format.relativeDate(user.createdAt)}
                    </Cell>

                    <Cell className="flex flex-wrap justify-end gap-2 max-md:justify-start">
                      <Button
                        variant="text"
                        size="auto"
                        className="text-xs"
                        disabled={pending}
                        onClick={() => setResetting(user)}
                      >
                        {t("admin.resetPassword")}
                      </Button>

                      <Button
                        variant="text"
                        size="auto"
                        className="text-xs"
                        disabled={pending}
                        onClick={() =>
                          ask(user, t("admin.confirmRevoke", { username: user.username }), () =>
                            api.revokeUserSessions(user.id),
                          )
                        }
                      >
                        {t("admin.revokeSessions")}
                      </Button>

                      <Button
                        variant="text"
                        size="auto"
                        className="text-xs"
                        disabled={pending || isSelf}
                        onClick={() =>
                          ask(
                            user,
                            t(
                              user.isAdmin ? "admin.confirmRemoveAdmin" : "admin.confirmMakeAdmin",
                              { username: user.username },
                            ),
                            () => api.setUserRole(user.id, !user.isAdmin),
                          )
                        }
                      >
                        {t(user.isAdmin ? "admin.removeAdmin" : "admin.makeAdmin")}
                      </Button>

                      <Button
                        variant="text"
                        size="auto"
                        className="text-xs text-destructive hover:text-destructive"
                        disabled={pending || isSelf}
                        onClick={() =>
                          ask(
                            user,
                            t(
                              user.isActive ? "admin.confirmDeactivate" : "admin.confirmReactivate",
                              { username: user.username },
                            ),
                            () => api.setUserActive(user.id, !user.isActive),
                          )
                        }
                      >
                        {t(user.isActive ? "admin.deactivate" : "admin.reactivate")}
                      </Button>
                    </Cell>
                  </Row>
                );
              })}
            </Table>

            <Pagination result={data} onChange={setPage} />
          </>
        )}
      </Query>

      {creating && <CreateUserDialog onClose={() => setCreating(false)} onCreated={refresh} />}

      {resetting && <ResetPasswordDialog user={resetting} onClose={() => setResetting(null)} />}

      {confirmDialog}
    </>
  );
}
