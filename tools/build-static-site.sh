#!/usr/bin/env bash
# Bygger en statisk udgave af Starch Wallpapers til GitHub Pages.
#
# GitHub Pages kan kun vise faste filer, ikke køre .NET. Derfor startes appen her i
# "StaticSite"-tilstand (uden login/Submit), og hver side gemmes som en HTML-fil.
# Bruges af .github/workflows/pages.yml, men kan også køres lokalt:
#
#   tools/build-static-site.sh            # bygger til ./_site
#   tools/build-static-site.sh /tmp/site  # eller en anden mappe
#
# Valgfrit: NASA_API_KEY=din-nøgle (ellers bruges DEMO_KEY).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$(realpath -m "${1:-$ROOT/_site}")"
BASE="/starchwallpapers"          # Pages-adressen er beyonddreadful.github.io/starchwallpapers/
PORT="${PORT:-5300}"
PAGES=("" "Home/ShowProducts" "Home/BingIotd" "Home/NASAIotd" "Home/About" "Home/Privacy")

WORK="$(mktemp -d)"
APP_PID=""
cleanup() {
    [[ -n "$APP_PID" ]] && kill "$APP_PID" 2>/dev/null || true
    rm -rf "$WORK"
}
trap cleanup EXIT

echo "==> Publishing app"
dotnet publish "$ROOT/MvcMovie/MvcMovie.csproj" -c Release -o "$WORK/app" --nologo -v quiet

echo "==> Starting app in static mode on port $PORT"
(
    cd "$WORK/app"
    ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS="http://127.0.0.1:$PORT" \
    StaticSite=true \
    PathBase="$BASE" \
    ConnectionStrings__VideoDbConnection="Data Source=$WORK/snapshot.db" \
    Nasa__ApiKey="${NASA_API_KEY:-DEMO_KEY}" \
    exec dotnet MvcMovie.dll
) > "$WORK/app.log" 2>&1 &
APP_PID=$!

# Vent til baggrundstjenesten har prøvet både NASA og Bing (højst 2 minutter).
for _ in $(seq 120); do
    if grep -qE 'Bing: [0-9]+ new|Could not fetch Bing' "$WORK/app.log" \
       && grep -qE 'NASA: [0-9]+ new|Could not fetch NASA' "$WORK/app.log"; then
        break
    fi
    if ! kill -0 "$APP_PID" 2>/dev/null; then
        echo "App exited early:" >&2; cat "$WORK/app.log" >&2; exit 1
    fi
    sleep 1
done
grep -E 'Bing: |NASA: |Could not fetch' "$WORK/app.log" | sed 's/^ */    /' || true

echo "==> Saving pages to $OUT"
rm -rf "$OUT"
mkdir -p "$OUT"
for page in "${PAGES[@]}"; do
    file="$OUT/${page:-index}.html"
    mkdir -p "$(dirname "$file")"
    curl -fsS "http://127.0.0.1:$PORT$BASE/$page" -o "$file"
    echo "    /${page} -> ${file#$OUT/}"
done

echo "==> Copying static files"
# Komprimerede kopier (.gz/.br) og source maps skal ikke med.
(cd "$WORK/app/wwwroot" && find . -type f ! -name '*.gz' ! -name '*.br' ! -name '*.map' -print0 \
    | xargs -0 -I{} install -D -m 644 {} "$OUT/{}")
touch "$OUT/.nojekyll"   # GitHub Pages skal ikke køre Jekyll på filerne.

cat > "$OUT/404.html" <<EOF
<!DOCTYPE html>
<html lang="en"><head><meta charset="utf-8"><title>Not found - Starch Wallpapers</title>
<meta http-equiv="refresh" content="3; url=$BASE/"></head>
<body style="font-family: sans-serif; text-align: center; padding-top: 4rem;">
<h1>Page not found</h1><p>Taking you back to <a href="$BASE/">Starch Wallpapers</a>...</p>
</body></html>
EOF

echo "==> Checking internal links"
missing=0
while read -r link; do
    path="${link#$BASE}"; path="${path%%\?*}"; path="${path%%#*}"; path="${path#/}"
    if [[ -z "$path" ]]; then target="$OUT/index.html"; else target="$OUT/$path"; fi
    if [[ ! -f "$target" && ! -f "$target.html" ]]; then
        echo "    MISSING: $link" >&2; missing=1
    fi
done < <(grep -ohE "(href|src)=\"$BASE[^\"]*\"|url\\('$BASE[^']*'\\)" "$OUT"/*.html "$OUT"/Home/*.html \
         | sed -E "s/^(href|src)=\"//; s/\"$//; s/^url\\('//; s/'\\)$//" | sort -u)
if grep -lE "(href|src)=\"/(Home|css|js|lib|images)/" "$OUT"/*.html "$OUT"/Home/*.html; then
    echo "    Links above are missing the $BASE prefix" >&2; missing=1
fi
[[ $missing -eq 0 ]] || { echo "Broken links found" >&2; exit 1; }

echo "==> Done: $(find "$OUT" -type f | wc -l) files, $(du -sh "$OUT" | cut -f1)"
