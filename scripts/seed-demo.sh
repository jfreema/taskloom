#!/usr/bin/env bash
# Fills a local Taskloom with demo data for the dashboard and the views (DemoSeed.cs): five spaces (one
# private), fourteen lists, about 500 tasks and subtasks, eight demo people, tags, comments and repeating tasks.
# It only adds. Your own spaces, lists and tasks are never touched; --replace and --remove delete only the
# demo spaces (and anything you put inside them).
#
#   scripts/seed-demo.sh you@example.com              add the demo data (refuses if it's already there)
#   scripts/seed-demo.sh you@example.com --replace    rebuild it, dated around today
#   scripts/seed-demo.sh you@example.com --remove     take it out again
#
# Needs the dev sign-in (on in docker compose) and a workspace you own. TASKLOOM_URL picks the server
# (default http://localhost:8080) and TASKLOOM_WORKSPACE a workspace by name (default: the first one you own).
set -euo pipefail

email="${1:-}"
mode="${2:-add}"
url="${TASKLOOM_URL:-http://localhost:8080}"
if [[ -z "$email" || "$email" == -* ]]; then
  sed -n '2,13p' "$0" | sed 's/^# \{0,1\}//'
  exit 1
fi
command -v curl >/dev/null || { echo "curl is needed" >&2; exit 1; }
command -v python3 >/dev/null || { echo "python3 is needed" >&2; exit 1; }

jar="$(mktemp)"
trap 'rm -f "$jar"' EXIT
today="$(date +%Y-%m-%d)"
headers=(-H "X-Taskloom: 1" -H "X-Taskloom-Date: $today" -H "Content-Type: application/json")

body="$(python3 -c 'import json,sys; print(json.dumps({"email": sys.argv[1]}))' "$email")"
curl -sf -c "$jar" -b "$jar" "${headers[@]}" -X POST "$url/auth/dev-login" -d "$body" >/dev/null \
  || { echo "Couldn't sign in as $email at $url (is the app running with the dev sign-in on?)" >&2; exit 1; }

workspace="$(curl -sf -b "$jar" "${headers[@]}" "$url/api/workspaces" | python3 -c '
import json, os, sys
spaces = json.load(sys.stdin)
want = os.environ.get("TASKLOOM_WORKSPACE")
owned = [w for w in spaces if w["role"] == "Owner" and (not want or w["name"] == want)]
if not owned:
    sys.exit("No workspace you own" + (f" named {want!r}" if want else "") + ".")
print(owned[0]["id"] + "\t" + owned[0]["name"])
')"
ws_id="${workspace%%$'\t'*}"
ws_name="${workspace#*$'\t'}"

case "$mode" in
  --remove)
    curl -sf -b "$jar" "${headers[@]}" -X DELETE "$url/api/dev/demo-seed/$ws_id" >/dev/null
    echo "Removed the demo spaces from \"$ws_name\"."
    ;;
  add|--replace)
    replace=false
    [[ "$mode" == "--replace" ]] && replace=true
    out="$(curl -s -w '\n%{http_code}' -b "$jar" "${headers[@]}" -X POST "$url/api/dev/demo-seed" \
      -d "{\"workspaceId\":\"$ws_id\",\"replace\":$replace}")"
    code="${out##*$'\n'}"
    json="${out%$'\n'*}"
    if [[ "$code" == "409" ]]; then
      echo "\"$ws_name\" already has the demo data. Run with --replace to rebuild it, or --remove to take it out." >&2
      exit 1
    elif [[ "$code" != "200" ]]; then
      echo "The seed failed ($code): $json" >&2
      exit 1
    fi
    echo "$json" | python3 -c '
import json, sys
r = json.load(sys.stdin)
print("Added to \"%s\": %d spaces, %d lists, %d tasks, %d subtasks, %d demo people." % (sys.argv[1], r["spaces"], r["lists"], r["tasks"], r["subtasks"], r["people"]))
' "$ws_name"
    echo "Open $url/dashboard"
    ;;
  *)
    echo "Unknown option $mode (use --replace or --remove)" >&2
    exit 1
    ;;
esac
