#!/usr/bin/env bash
#
# Ставит SPDX-шапку в начало каждого исходного файла.
#
#   scripts/license-headers.sh            проставить недостающие
#   scripts/license-headers.sh --check    только проверить (так делает CI)
#
# Шапка — две строки, а не абзац юридического текста: SPDX читается инструментами вроде
# reuse и лицензионных сканеров, не устаревает при смене года и не отодвигает первую
# значащую строку файла на экран вниз.
#
# Пропускаются сгенерированные файлы: next-env.d.ts переписывает Next при каждой сборке,
# *.Designer.cs и снимок модели — EF при каждой миграции. Шапка там не переживёт следующую
# генерацию, и проверка начнёт падать на ровном месте.

set -euo pipefail

readonly LICENSE_ID="MIT"
readonly HOLDER="Bulat Ruslanovich"
readonly YEAR="2026"

readonly MARKER="SPDX-License-Identifier"

check_only=false
[ "${1:-}" = "--check" ] && check_only=true

cd "$(dirname "$0")/.."

sources() {
    find backend frontend \
        \( -name node_modules -o -name .next -o -name obj -o -name bin \) -prune -o \
        \( -name '*.cs' -o -name '*.ts' -o -name '*.tsx' -o -name '*.js' -o -name '*.mjs' -o -name '*.css' \) -print |
        grep -v -e 'next-env\.d\.ts$' -e '\.Designer\.cs$' -e 'ModelSnapshot\.cs$' |
        sort
}

header_for() {
    case "$1" in
    *.css) printf '/* SPDX-License-Identifier: %s */\n/* Copyright (c) %s %s */\n\n' "$LICENSE_ID" "$YEAR" "$HOLDER" ;;
    *) printf '// SPDX-License-Identifier: %s\n// Copyright (c) %s %s\n\n' "$LICENSE_ID" "$YEAR" "$HOLDER" ;;
    esac
}

missing=0
stamped=0

while IFS= read -r file; do
    head -n 2 "$file" | grep -q "$MARKER" && continue

    missing=$((missing + 1))

    if $check_only; then
        echo "no license header: $file"
        continue
    fi

    { header_for "$file"; cat "$file"; } >"$file.tmp"
    mv "$file.tmp" "$file"
    stamped=$((stamped + 1))
done < <(sources)

if $check_only; then
    [ "$missing" -eq 0 ] || {
        echo "$missing file(s) without a license header — run scripts/license-headers.sh" >&2
        exit 1
    }
    echo "all source files carry the license header"
    exit 0
fi

echo "stamped $stamped file(s)"
