// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { EllipsisIcon, SearchIcon } from "lucide-react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { useState } from "react";
import { cn } from "@/lib/cn";
import { isActivePath, libraryNav, primaryNav, serviceNav } from "@/lib/navigation";
import { useAuth } from "@/contexts/AuthContext";
import { useUpload } from "@/contexts/UploadContext";
import { useT } from "@/contexts/I18nContext";
import { BrandMark, BrandWordmark } from "./Brand";
import { Copyright } from "./Copyright";
import { AccountRow, NavLink } from "./Sidebar";
import { Button } from "./ui/button";
import { Sheet, SheetContent, SheetTitle } from "./ui/sheet";

const tabClass =
  "flex min-w-0 flex-col items-center justify-center gap-0.5 px-0.5 text-2xs hover:no-underline";

export function MobileHeader() {
  const t = useT();

  return (
    <header
      className="hidden items-center justify-between border-b border-border px-4 [grid-area:mobile-header] max-md:flex"
      style={{
        minHeight: "calc(3.5rem + env(safe-area-inset-top))",
        paddingTop: "env(safe-area-inset-top)",
      }}
    >
      <Link
        href="/"
        aria-label={t("nav.home")}
        className="flex items-center gap-2 hover:no-underline"
      >
        <BrandMark className="size-7" />
        <BrandWordmark />
      </Link>
      <Button variant="ghost" size="icon" asChild>
        <Link href="/search" aria-label={t("nav.search")}>
          <SearchIcon />
        </Link>
      </Button>
    </header>
  );
}

export function MobileNav() {
  const t = useT();
  const pathname = usePathname();
  const { isAdmin } = useAuth();
  const { progress } = useUpload();
  const [moreOpen, setMoreOpen] = useState(false);

  const sheetLinks = [...libraryNav, ...serviceNav(isAdmin)];
  const sheetActive = sheetLinks.some((entry) => isActivePath(pathname, entry.href));

  return (
    <>
      <nav
        aria-label={t("nav.main")}
        className="hidden auto-cols-fr grid-flow-col border-t border-border bg-card [grid-area:nav] max-md:grid"
        style={{
          height: "calc(var(--mobile-nav-height) + env(safe-area-inset-bottom))",
          paddingBottom: "env(safe-area-inset-bottom)",
        }}
      >
        {primaryNav.map(({ href, labelKey, icon: Icon }) => {
          const active = isActivePath(pathname, href);

          return (
            <Link
              key={href}
              href={href}
              aria-current={active ? "page" : undefined}
              className={cn(tabClass, active ? "font-semibold text-foreground" : "text-faint")}
            >
              <Icon />
              <span className="max-w-full truncate">{t(labelKey)}</span>
            </Link>
          );
        })}

        <button
          type="button"
          onClick={() => setMoreOpen(true)}
          aria-expanded={moreOpen}
          className={cn(
            tabClass,
            moreOpen || sheetActive ? "font-semibold text-foreground" : "text-faint",
          )}
        >
          <span className="relative">
            <EllipsisIcon />
            {progress !== null && (
              <span
                aria-hidden="true"
                className="absolute -top-0.5 -right-0.5 size-2 rounded-full bg-primary"
              />
            )}
          </span>
          <span className="max-w-full truncate">{t("nav.more")}</span>
        </button>
      </nav>

      <Sheet open={moreOpen} onOpenChange={setMoreOpen}>
        <SheetContent>
          <SheetTitle className="sr-only">{t("nav.more")}</SheetTitle>

          <nav aria-label={t("nav.more")} className="flex flex-col gap-0.5">
            {sheetLinks.map((entry) => (
              <NavLink key={entry.href} entry={entry} onNavigate={() => setMoreOpen(false)} />
            ))}
          </nav>

          <div className="mt-3 flex flex-col gap-3 pt-3">
            <AccountRow />
            <Copyright />
          </div>
        </SheetContent>
      </Sheet>
    </>
  );
}
