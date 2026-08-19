"use client";

import type { ReactNode } from "react";
import { cn } from "@/lib/cn";
import { usePageTitle } from "@/lib/documentTitle";
import { Overline } from "./ui/label";

export function DetailHeader({
  kind,
  title,
  art,
  facts,
  description,
  actions,
  tint,
  round = false,
}: {
  kind: string;
  title: string;
  art: ReactNode;
  facts?: ReactNode;
  description?: ReactNode;
  actions?: ReactNode;
  tint?: string | null;
  round?: boolean;
}) {
  usePageTitle(title);

  return (
    <header
      style={{ ["--art-tint" as string]: tint ?? "" }}
      className={cn(
        "flex flex-wrap items-end gap-8 rounded-xl p-5 max-md:items-start max-md:gap-3 max-md:p-3",
        "bg-[linear-gradient(180deg,color-mix(in_srgb,var(--art-tint)_42%,transparent),transparent)]",
        "[transition:--art-tint_700ms_var(--ease)]",
      )}
    >
      <div
        className={cn(
          "grid size-52 shrink-0 place-items-center overflow-hidden rounded-lg text-faint shadow-art max-md:size-30",
          round && "rounded-full",
        )}
      >
        {art}
      </div>

      <div className="flex min-w-[min(16rem,100%)] flex-1 flex-col gap-2">
        <Overline>{kind}</Overline>
        <h1 className="text-[clamp(1.75rem,1.2rem+1.8vw,2.6rem)]">{title}</h1>
        {description && (
          <p className="max-w-[62ch] text-muted-foreground max-md:text-sm">{description}</p>
        )}
        {facts && <p className="text-sm text-muted-foreground">{facts}</p>}
        {actions && <div className="mt-1 flex flex-wrap items-center gap-3">{actions}</div>}
      </div>
    </header>
  );
}
