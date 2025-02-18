### promotes a given stream to a different stream while modifying the published_at date to 'now'
param (
    [string]$FromPath,
    [string]$ToPath
)

# Load the JSON from the source file
$json = Get-Content -Path $FromPath | ConvertFrom-Json

# Change the 'published_at' field to the current date and time
$json.published_at = (Get-Date).ToString("yyyy-MM-ddTHH:mm:ssZ")

# Convert the updated JSON back to a string
$updatedJson = $json | ConvertTo-Json -Depth 10

# Save the modified JSON to the destination path
$updatedJson | Set-Content -Path $ToPath
