#!/usr/bin/env bash
# Uploads the GitHub Actions secrets used by .github/workflows/build.yml.
# Run it yourself in a terminal: values are read from local files or hidden prompts
# and piped straight to `gh secret set`; nothing is printed or written to disk.
#
#   .github/scripts/set-ci-secrets.sh            # all sections
#   .github/scripts/set-ci-secrets.sh android    # only some: unity android
set -euo pipefail

REPO="${REPO:-OurTaiko/OurTaikoPlay}"
sections=("$@")
[ ${#sections[@]} -gt 0 ] || sections=(unity android)

put() { gh secret set "$1" -R "$REPO" >/dev/null && echo "  ✓ $1"; }
put_value() { printf '%s' "$2" | put "$1"; }
put_file() { put "$1" < "$2"; }
put_base64() { base64 -i "$2" | tr -d '\n' | put "$1"; }
put_hidden() {
  local value
  read -r -s -p "  $2: " value; echo
  [ -n "$value" ] || { echo "  ✗ $1 is empty"; return 1; }
  put_value "$1" "$value"
}
ask() { local answer; read -r -p "  $1 [$2]: " answer; echo "${answer:-$2}"; }
need_file() { [ -f "$1" ] || { echo "  ✗ Not found: $1"; return 1; }; }

unity() {
  echo "Unity license"
  local ulf
  ulf=$(ask "Unity_lic.ulf path" "/Library/Application Support/Unity/Unity_lic.ulf")
  need_file "$ulf"
  put_file UNITY_LICENSE "$ulf"
  put_value UNITY_EMAIL "$(ask "Unity account email" "")"
  put_hidden UNITY_PASSWORD "Unity account password"
}

android() {
  echo "Android signing"
  local dir="$HOME/Documents/OurTaiko/android-signing" jks alias
  jks=$(ask "Keystore" "$dir/ourtaiko-release.jks")
  need_file "$jks"
  alias=$(ask "Key alias" "ourtaiko")
  put_base64 ANDROID_KEYSTORE_BASE64 "$jks"
  put_value ANDROID_KEYALIAS_NAME "$alias"
  local pass_file="$(dirname "$jks")/password.txt"
  if [ -f "$pass_file" ] && [ "$(ask "Use $pass_file for keystore and key password? (y/n)" y)" = y ]; then
    local pass; pass=$(tr -d '\r\n' < "$pass_file")
    put_value ANDROID_KEYSTORE_PASS "$pass"
    put_value ANDROID_KEYALIAS_PASS "$pass"
  else
    put_hidden ANDROID_KEYSTORE_PASS "Keystore password"
    put_hidden ANDROID_KEYALIAS_PASS "Key password"
  fi
}

for section in "${sections[@]}"; do "$section"; echo; done
gh secret list -R "$REPO"
