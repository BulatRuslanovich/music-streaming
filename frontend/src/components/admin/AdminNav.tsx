// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { cn } from "@/lib/cn";
import { useT } from "@/contexts/I18nContext";
import type { TranslationKey } from "@/lib/i18n";
import { ChartIcon, ShieldIcon, UploadIcon, ArtistIcon } from "@/components/Icons";

const sections: { href: string; labelKey: TranslationKey; icon: typeof ChartIcon }[] = [
  { href: "/admin", labelKey: "admin.nav.users", icon: ShieldIcon },
  { href: "/admin/statistics", labelKey: "admin.nav.overview", icon: ChartIcon },
  { href: "/admin/statistics/users", labelKey: "admin.nav.listeners", icon: ArtistIcon },
  { href: "/admin/statistics/uploads", labelKey: "admin.nav.uploads", icon: UploadIcon },
];

/**
 * Переключение разделов админки.
 *
 * Активный раздел ищется точным совпадением, а не префиксом: иначе `/admin` подсвечивался бы
 * на каждой вложенной странице, а `/admin/statistics` — на карточке отдельного слушателя.
 * Исключение одно — список слушателей, куда карточка и ведёт.
 */
export function AdminNav() {
  const t = useT();
  const pathname = usePathname();

  return (
    <nav aria-label={t("admin.nav.label")} className="-mx-1 overflow-x-auto">
      <ul className="flex min-w-max items-center gap-1 px-1">
        {sections.map(({ href, labelKey, icon: Icon }) => {
          const active =
            pathname === href ||
            (href === "/admin/statistics/users" && pathname.startsWith("/admin/statistics/users/"));

          return (
            <li key={href}>
              <Link
                href={href}
                aria-current={active ? "page" : undefined}
                className={cn(
                  "flex items-center gap-2 rounded-lg px-3 py-2 text-sm font-medium transition-colors",
                  active
                    ? "bg-raised text-foreground"
                    : "text-muted-foreground hover:bg-accent hover:text-foreground",
                )}
              >
                <Icon size={16} />
                {t(labelKey)}
              </Link>
            </li>
          );
        })}
      </ul>
    </nav>
  );
}
