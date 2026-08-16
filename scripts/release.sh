#!/usr/bin/env bash
#
# Проставляет версию приложения и ставит на неё тег.
#
# Версия живёт в двух местах — сборке бэкенда и манифесте фронта, — и разъехаться им нельзя:
# бэкенд отдаёт свою в /api/system, и подвал интерфейса показывает её рядом с версией фронта
# именно затем, чтобы рассинхрон было видно. Поэтому оба файла правит один скрипт, а не человек.
#
#   scripts/release.sh 1.1.0
#
# Не пушит: отправка тега — это публикация, и решать, когда она случится, должен человек.
# Готовую команду скрипт печатает в конце.

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

# Строго X.Y.Z, с необязательным суффиксом предрелиза: теги репозитория выглядят как v1.0.2,
# и своевольная форма сломала бы и сортировку тегов, и разбор версии сборкой .NET.
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]] \
    || die "версия '$version' не похожа на X.Y.Z"

readonly tag="v$version"

cd "$(git rev-parse --show-toplevel)" || die "запускать нужно внутри репозитория"

[ -f "$PROPS" ] || die "не найден $PROPS"
[ -f "$PACKAGE" ] || die "не найден $PACKAGE"

# Тег обязан указывать ровно на то, что лежит в файлах, поэтому незакоммиченное — это отказ,
# а не предупреждение.
[ -z "$(git status --porcelain)" ] \
    || die "в рабочем дереве есть незакоммиченные изменения — сначала разберитесь с ними"

git rev-parse -q --verify "refs/tags/$tag" >/dev/null \
    && die "тег $tag уже существует"

current="$(sed -n 's|.*<Version>\(.*\)</Version>.*|\1|p' "$PROPS" | head -1)"
[ "$current" != "$version" ] || die "версия уже $version"

# В props элемент один, в package.json своя версия пакета идёт первой — заменяем только её,
# чтобы не задеть версии зависимостей.
sed -i "s|<Version>[^<]*</Version>|<Version>$version</Version>|" "$PROPS"
sed -i "0,/\"version\": \"[^\"]*\"/s//\"version\": \"$version\"/" "$PACKAGE"

# Проверяем, что замена и правда случилась: молча не сработавший sed оставил бы тег на старой версии.
grep -q "<Version>$version</Version>" "$PROPS" || die "не удалось обновить $PROPS"
grep -q "\"version\": \"$version\"" "$PACKAGE" || die "не удалось обновить $PACKAGE"

git add -- "$PROPS" "$PACKAGE"
git commit -m "chore: release $version" >/dev/null
git tag -a "$tag" -m "Release $version"

echo "$current → $version, тег $tag на $(git rev-parse --short HEAD)"
echo
echo "отправить:  git push origin $(git rev-parse --abbrev-ref HEAD) && git push origin $tag"
echo "отменить:   git tag -d $tag && git reset --hard HEAD~1"
