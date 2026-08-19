// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Bulat Ruslanovich

"use client";

import { useEffect, useId, useState } from "react";
import { cn } from "@/lib/cn";
import { useT } from "@/contexts/I18nContext";
import { Button } from "./ui/button";
import { CloseIcon, SearchIcon } from "./Icons";

const DEBOUNCE_MS = 300;

export function SearchField({
  value,
  onChange,
  placeholder,
  label,
  autoFocus = false,
  className,
}: {
  value: string;
  onChange: (value: string) => void;
  placeholder: string;
  label?: string;
  autoFocus?: boolean;
  className?: string;
}) {
  const t = useT();
  const inputId = useId();

  const [input, setInput] = useState(value);
  const [lastCommitted, setLastCommitted] = useState(value);

  if (value !== lastCommitted) {
    setLastCommitted(value);
    setInput(value);
  }

  useEffect(() => {
    const trimmed = input.trim();
    if (trimmed === value) return;

    const timer = window.setTimeout(() => {
      setLastCommitted(trimmed);
      onChange(trimmed);
    }, DEBOUNCE_MS);

    return () => window.clearTimeout(timer);
  }, [input, value, onChange]);

  const clear = () => {
    setInput("");
    setLastCommitted("");
    onChange("");
  };

  return (
    <div
      className={cn(
        "flex max-w-xl items-center gap-2.5 rounded-full border border-transparent bg-raised px-3.5 text-muted-foreground transition-colors",
        "hover:bg-accent focus-within:border-ring focus-within:text-foreground",
        className,
      )}
    >
      <SearchIcon size={18} />
      <label htmlFor={inputId} className="sr-only">
        {label ?? placeholder}
      </label>
      <input
        id={inputId}
        type="search"
        placeholder={placeholder}
        value={input}
        autoComplete="off"
        autoFocus={autoFocus}
        onChange={(event) => setInput(event.target.value)}
        onKeyDown={(event) => {
          if (event.key === "Escape" && input !== "") {
            event.preventDefault();
            clear();
          }
        }}
        className="w-full min-w-0 bg-transparent py-2.5 text-base text-foreground outline-none placeholder:text-faint [&::-webkit-search-cancel-button]:hidden"
      />
      {input !== "" && (
        <Button
          variant="ghost"
          size="icon-sm"
          className="-mr-1.5"
          onClick={clear}
          aria-label={t("action.clear")}
        >
          <CloseIcon size={16} />
        </Button>
      )}
    </div>
  );
}
