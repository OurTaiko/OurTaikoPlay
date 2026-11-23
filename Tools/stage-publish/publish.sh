#!/bin/sh
# Uploads the play backdrops' original .otanim files that build.py laid out to the public bucket,
# read at https://distrib.ourtaiko.org/<prefix>/otanim/. Only the originals: the bundles and the
# catalog are uploaded by the "Stage bundles" workflow, which fetches these from there.
#
# The files go first and otanim/index.json, which names them, last, so the index never names a
# file that is not there yet. Every file but the index carries its own hash in its name and is
# never replaced, so it may be cached for good; the index may be cached for five minutes, and the
# upload ends by invalidating it on CloudFront so the new one is read at once. Nothing is deleted.
#
# Needs `aws login` with write access to the bucket and to the distribution.
#
# Usage: Tools/stage-publish/publish.sh <folder holding stage/> [prefix, default stage]
set -eu
dir=${1:?usage: publish.sh <folder holding stage/> [prefix]}
prefix=${2:-stage}
from="$dir/stage/otanim"
[ -f "$from/index.json" ] || { echo "no stage/otanim/index.json under $dir (run build.py first)" >&2; exit 1; }
dest=s3://ourtaiko-public/$prefix/otanim
aws s3 sync "$from" "$dest" --exclude '*' --include '*.otanim' \
  --size-only --content-type application/octet-stream --cache-control 'public, max-age=31536000, immutable'
aws s3 cp "$from/index.json" "$dest/index.json" \
  --content-type application/json --cache-control 'public, max-age=300'
# distrib.ourtaiko.org
aws cloudfront create-invalidation --distribution-id E29SNWDH0GYCHC \
  --paths "/$prefix/otanim/index.json" \
  --query 'Invalidation.{Id:Id,Status:Status}' --output text
echo "published: https://distrib.ourtaiko.org/$prefix/otanim/index.json"
