#!/usr/bin/env bash
# Uploads the GitHub Actions secrets used by .github/workflows/build.yml.
# Run it yourself in a terminal: values are read from local files or hidden prompts
# and piped straight to `gh secret set`; nothing is printed or written to disk.
#
#   .github/scripts/set-ci-secrets.sh            # all sections
#   .github/scripts/set-ci-secrets.sh android    # only some: unity android apple
set -euo pipefail

REPO="${REPO:-OurTaiko/OurTaikoPlay}"
sections=("$@")
[ ${#sections[@]} -gt 0 ] || sections=(unity android apple)

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

# iOS signing and TestFlight upload (build.yml's ios job). See Building.md "iOS TestFlight" for
# where each file comes from. The .p12 must hold the Apple Distribution certificate with its key.
apple() {
  echo "Apple signing and App Store Connect"
  local dir="$HOME/Documents/OurTaiko/apple-signing" p12 profile p8
  p12=$(ask "Apple Distribution .p12" "$dir/distribution.p12")
  need_file "$p12"
  put_base64 APPSTORE_CERTIFICATES_FILE_BASE64 "$p12"
  put_hidden APPSTORE_CERTIFICATES_PASSWORD ".p12 export password"
  profile=$(ask "App Store provisioning profile (.mobileprovision)" "$dir/appstore.mobileprovision")
  need_file "$profile"
  put_base64 APPSTORE_PROFILE_BASE64 "$profile"
  local name
  name=$(security cms -D -i "$profile" | plutil -extract Name raw - 2>/dev/null || true)
  put_value APPSTORE_PROFILE_NAME "$(ask "Profile name" "$name")"
  put_value APPLE_TEAM_ID "$(ask "Apple Team ID" "$(security cms -D -i "$profile" | plutil -extract TeamIdentifier.0 raw - 2>/dev/null || true)")"
  p8=$(ask "App Store Connect API key (.p8)" "$(ls "$dir"/AuthKey_*.p8 "$dir"/ApiKey_*.p8 2>/dev/null | head -n 1)")
  need_file "$p8"
  put_file APPSTORE_PRIVATE_KEY "$p8"
  local key_id; key_id=$(basename "$p8" .p8); key_id=${key_id#AuthKey_}; key_id=${key_id#ApiKey_}
  put_value APPSTORE_KEY_ID "$(ask "Key ID" "$key_id")"
  local issuer; issuer=$(ask "Issuer ID (leave empty for an individual API key)" "")
  if [ -n "$issuer" ]; then
    put_value APPSTORE_ISSUER_ID "$issuer"
  else
    echo "  Individual API key: APPSTORE_ISSUER_ID must remain unset in GitHub."
  fi
}

for section in "${sections[@]}"; do "$section"; echo; done
gh secret list -R "$REPO"
