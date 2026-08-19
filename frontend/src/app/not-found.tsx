// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import Link from "next/link";
import { NoteIcon } from "@/components/Icons";
import { StatusPage } from "@/components/StatusPage";
import { Button } from "@/components/ui/button";
import { useT } from "@/contexts/I18nContext";

export default function NotFound() {
  const t = useT();

  return (
    <StatusPage
      icon={<NoteIcon size={36} />}
      title={t("error.notFoundTitle")}
      description={t("error.notFoundDescription")}
      actions={
        <Button variant="primary" asChild>
          <Link href="/">{t("action.goHome")}</Link>
        </Button>
      }
    />
  );
}
