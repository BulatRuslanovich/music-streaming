#!/usr/bin/env bash

set -euo pipefail

readonly PROPS="backend/Directory.Build.props"
readonly PACKAGE="frontend/package.json"

die() {
    echo "error: $*" >&2
    exit 1
}

[ $# -eq 1 ] || die "нужна ровно одна версия. Пример: scripts/release.sh 1.1.0"

version="$1"
version="${version#v}"

[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]] \
    || die "версия '$version' не похожа на X.Y.Z"

readonly tag="v$version"

cd "$(git rev-parse --show-toplevel)" || die "запускать нужно внутри репозитория"

[ -f "$PROPS" ] || die "не найден $PROPS"
[ -f "$PACKAGE" ] || die "не найден $PACKAGE"

[ -z "$(git status --porcelain)" ] \
    || die "в рабочем дереве есть незакоммиченные изменения — сначала разберитесь с ними"

git rev-parse -q --verify "refs/tags/$tag" >/dev/null \
    && die "тег $tag уже существует"

current="$(sed -n 's|.*<Version>\(.*\)</Version>.*|\1|p' "$PROPS" | head -1)"
[ "$current" != "$version" ] || die "версия уже $version"

sed -i "s|<Version>[^<]*</Version>|<Version>$version</Version>|" "$PROPS"
sed -i "0,/\"version\": \"[^\"]*\"/s//\"version\": \"$version\"/" "$PACKAGE"

grep -q "<Version>$version</Version>" "$PROPS" || die "не удалось обновить $PROPS"
grep -q "\"version\": \"$version\"" "$PACKAGE" || die "не удалось обновить $PACKAGE"

git add -- "$PROPS" "$PACKAGE"
git commit -m "chore: release $version" >/dev/null
git tag -a "$tag" -m "Release $version"

echo "$current → $version, тег $tag на $(git rev-parse --short HEAD)"
echo
echo "отправить:  git push origin $(git rev-parse --abbrev-ref HEAD) && git push origin $tag"
echo "отменить:   git tag -d $tag && git reset --hard HEAD~1"
