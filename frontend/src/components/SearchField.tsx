"use client";

import { useEffect, useId, useState } from "react";
import { useT } from "@/contexts/I18nContext";
import { CloseIcon, SearchIcon } from "./Icons";

const DEBOUNCE_MS = 300;

/**
 * A filter box for a paged list. Typing is local and instant; the query the page reloads on only
 * follows once typing pauses, so narrowing a long list does not fire a request per keystroke.
 */
export function SearchField({
  value,
  onChange,
  placeholder,
  label,
}: {
  /** The committed query — what the list is currently filtered by. */
  value: string;
  onChange: (value: string) => void;
  placeholder: string;
  /** Accessible name; falls back to the placeholder. */
  label?: string;
}) {
  const t = useT();
  const inputId = useId();

  const [input, setInput] = useState(value);
  const [lastCommitted, setLastCommitted] = useState(value);

  // A reset from the outside (a cleared filter, a different page) wins over what was typed.
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
