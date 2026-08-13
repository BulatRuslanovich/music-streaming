"use client";

import { useEffect, useId, useState } from "react";
import { useT } from "@/contexts/I18nContext";
import { CloseIcon, SearchIcon } from "./Icons";

const DEBOUNCE_MS = 300;

export function SearchField({
  value,
  onChange,
  placeholder,
  label,
  autoFocus = false,
}: {
  value: string;
  onChange: (value: string) => void;
  placeholder: string;
  label?: string;
  autoFocus?: boolean;
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
    <div className="search-field search-field-inline">
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
      />
      {input !== "" && (
        <button type="button" className="icon-button" onClick={clear} aria-label={t("action.clear")}>
          <CloseIcon size={16} />
        </button>
      )}
    </div>
  );
}
