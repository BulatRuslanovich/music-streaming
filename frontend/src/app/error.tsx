"use client";

import { useEffect } from "react";
import Link from "next/link";
import { WarningIcon } from "@/components/Icons";
import { StatusPage } from "@/components/ui";
import { useT } from "@/contexts/I18nContext";

export default function Error({
  error,
  reset,
}: {
  error: Error & { digest?: string };
  reset: () => void;
}) {
  const t = useT();

  useEffect(() => {
    console.error(error);
  }, [error]);

  return (
    <StatusPage
      icon={<WarningIcon size={32} />}
      tone="danger"
      title={t("error.pageTitle")}
      description={t("error.pageDescription")}
      actions={
        <>
          <button type="button" className="button button-primary" onClick={reset}>
            {t("action.tryAgain")}
          </button>
          <Link href="/" className="button">
            {t("action.goHome")}
          </Link>
        </>
      }
    />
  );
}
