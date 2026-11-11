#!/usr/bin/env bash
# Production Linux packaging, shared by the workflow and the offline packaging check.
set -euo pipefail

if [[ $# != 4 ]]; then
  echo "Usage: $0 BUILD_DIRECTORY BUILD_NAME DISPLAY_VERSION OUTPUT_DIRECTORY" >&2
  exit 1
fi
SOURCE=$(cd -- "$1" && pwd)
NAME=$2
VERSION=$3
[[ "$NAME" =~ ^[A-Za-z0-9._-]+$ && "$VERSION" =~ ^[A-Za-z0-9._-]+$ ]] || {
  echo "Invalid build name or display version" >&2; exit 1;
}
[[ -f "$SOURCE/$NAME" && -x "$SOURCE/$NAME" && -d "$SOURCE/${NAME}_Data" && -f "$SOURCE/UnityPlayer.so" ]] || {
  echo "Missing Linux executable, Data directory or UnityPlayer.so" >&2; exit 1;
}
# Accept only build payload at the root, never a previously run game's settings, scores,
# songs, logs or home/cache directories. Unknown Unity output fails rather than being lost.
shopt -s dotglob nullglob
for entry in "$SOURCE"/*; do
  case "${entry##*/}" in
    "$NAME"|"${NAME}_Data"|UnityCrashHandler64|*.so|*.so.*) ;;
    *_BurstDebugInformation_DoNotShip|*_BackUpThisFolder_ButDontShipItWithYourGame) ;;
    *) echo "Unexpected Linux build-root entry: ${entry##*/}; use a clean build output" >&2; exit 1 ;;
  esac
done
mkdir -p -- "$4"
OUT=$(cd -- "$4" && pwd)
[[ "$OUT/" != "$SOURCE/"* ]] || { echo "Output directory must be outside the build directory" >&2; exit 1; }
ARCHIVE="$OUT/$NAME-$VERSION-Linux-x64.tar.gz"
[[ ! -e "$ARCHIVE" ]] || { echo "Archive already exists: $ARCHIVE" >&2; exit 1; }
# No --dereference: archive executable modes and symlinks as built. Match excluded directory
# names at any depth, including debug/backup directories nested in the Data directory.
tar -czf "$ARCHIVE" \
  --exclude='*_BurstDebugInformation_DoNotShip' \
  --exclude='*_BackUpThisFolder_ButDontShipItWithYourGame' \
  -C "$SOURCE" .
printf '%s\n' "$ARCHIVE"
