#!/bin/bash
set -euo pipefail

version="$1"
if ! grep -qE "^# Release ${version}\$" release-notes.md; then
  echo "❌ Version ${version} not found in release-notes.md!"
  exit 1
fi

release_notes=$(awk '/^# Release /{if (seen++) exit} seen' release-notes.md)

if [[ "$release_notes" != \#\ Release\ "$version"* ]]; then
  echo "❌ release_notes does not start with '# Release $version'" >&2
  exit 1
fi

action_id="$2"
repo="openziti/desktop-edge-win"
tmp_dir="/tmp/zdew-artifacts"
rm -rf "/tmp/zdew-artifacts"
mkdir -p "${tmp_dir}"

if ! json=$(gh api "repos/${repo}/actions/runs/${action_id}/artifacts" --jq '.artifacts[] | {name, url: .archive_download_url}'); then
  echo "❌ Failed to fetch artifacts" >&2
  exit 1
fi

echo "$json" | jq -c '.' | while read -r artifact; do
#echo "$json" | jq -c 'select(.name | test("win32crypto"))' | while read -r artifact; do
  name=$(echo "$artifact" | jq -r .name)
  url=$(echo "$artifact" | jq -r .url)

  [[ -z "$url" ]] && {
    echo "⚠️  Skipping artifact with missing URL: $name" >&2
    continue
  }

  zip_path="${tmp_dir}/${name}.zip"
  if ! gh api "$url" > "$zip_path"; then
    echo "❌ Failed to download $url" >&2
    exit 1
  fi
  
  echo "📂 Unzipping to ${tmp_dir}/${name}"
  unzip -oq "$zip_path" -d "${tmp_dir}/${name}" || {
    echo "❌ Failed to unzip to ${tmp_dir}/${name}" >&2
    exit 1
  }
done

rm /tmp/zdew-artifacts/*.zip

tree "/tmp/zdew-artifacts"
artifact_version=$(find "${tmp_dir}" -name "*.exe" | grep -oP '\d+\.\d+\.\d+(\.\d+)?' | sort | uniq )
count=$(echo "${artifact_version}" | wc -l)

if [[ "$count" -eq 1 ]]; then
  echo "✅ All artifact versions matched: ${artifact_version}. Uploading..."
else
  echo "❌ Version mismatch detected:"
  echo "${artifact_version}"
  exit 1
fi

if [[ "${artifact_version}" == "${version}" ]]; then
  echo "✅ artifact version ${artifact_version} matched supplied version: ${version}. Uploading can continue..."
else
  echo "❌ artifact version ${artifact_version} does not match supplied version: ${version}:"
  exit 1
fi

for f in /tmp/zdew-artifacts/ZitiDesktopEdgeClient-"${artifact_version}"-win32crypto/*; do
  new="$(dirname "$f")/$(basename "$f" | tr ' ' '.')"
  echo "renaming $f"
  echo "      to $new"
  mv "$f" "$new"
  echo "---------"
done

repo="downloads"
base_path="/tmp/zdew-artifacts/ZitiDesktopEdgeClient-${artifact_version}-win32crypto"
JFROG_TOKEN="$(cat /mnt/c/temp/jfrog.token)"

for file in "$base_path"/*; do
  name=$(basename "$file")
  url="https://netfoundry.jfrog.io/artifactory/$repo/desktop-edge-win/${artifact_version}-win32crypto/$name"

  echo "⬆️  Uploading $name to $url"
  response=$(curl -s -w "%{http_code}" -o /dev/null \
    -H "Authorization: Bearer $JFROG_TOKEN" \
    -T "$file" "$url")

  if [[ "$response" == "201" || "$response" == "200" ]]; then
    echo "✅ Uploaded: $name"
  else
    echo "❌ Failed to upload $name (HTTP $response)" >&2
    exit 1
  fi
done

echo "Creating a release on GitHub"
gh release create "v${version}" \
  --title "v${version}" \
  --notes "$release_notes" \
  --prerelease

gh release upload "v${version}" /tmp/zdew-artifacts/ZitiDesktopEdgeClient-2.7.1.2/*

