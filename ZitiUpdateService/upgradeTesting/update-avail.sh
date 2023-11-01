published_at=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
version=2.1.20
cat > update-check.json <<HERE
{
  "name": "${version} Override",
  "tag_name": "${version}",
  "published_at": "${published_at}",
  "assets": [
    {
      "name": "Ziti.Desktop.Edge.Client-2.1.1.exe",
      "browser_download_url": "http://localhost:8000/ZitiDesktopEdgeClient-${version}/Ziti Desktop Edge Client-${version}.exe"
    }
  ]
}
HERE