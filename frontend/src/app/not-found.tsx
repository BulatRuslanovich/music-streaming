"use client";

import Link from "next/link";
import { NoteIcon } from "@/components/Icons";
import { StatusPage } from "@/components/ui";
import { useT } from "@/contexts/I18nContext";

export default function NotFound() {
  const t = useT();

  return (
    <StatusPage
      icon={<NoteIcon size={36} />}
      title={t("error.notFoundTitle")}
      description={t("error.notFoundDescription")}
      actions={
        <Link href="/" className="button button-primary">
          {t("action.goHome")}
        </Link>
      }
    />
  );
}
