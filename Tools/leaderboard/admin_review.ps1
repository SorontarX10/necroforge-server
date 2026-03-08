param(
    [string]$BaseUrl = "http://localhost:8080",
    [string]$AdminToken,
    [ValidateSet("list", "review")]
    [string]$Mode = "list",
    [string]$State = "manual_review",
    [int]$Page = 1,
    [int]$PageSize = 20,
    [string]$RunId = "",
    [string]$Action = "accept",
    [string]$Note = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($AdminToken)) {
    throw "Admin token is required. Pass -AdminToken or set LEADERBOARD_ADMIN_API_KEY."
}

$headers = @{
    "X-Admin-Token" = $AdminToken
}

if ($Mode -eq "list") {
    $url = "$BaseUrl/admin/runs/flagged?state=$State&page=$Page&page_size=$PageSize"
    $response = Invoke-RestMethod -Uri $url -Method Get -Headers $headers
    Write-Host "total_count: $($response.total_count)"
    if ($response.entries -eq $null -or $response.entries.Count -eq 0) {
        Write-Host "entries: none"
        exit 0
    }

    foreach ($entry in $response.entries) {
        Write-Host ("run_id={0} state={1} score={2} player={3} reason={4}" -f `
            $entry.run_id, $entry.validation_state, $entry.score, $entry.player_id, $entry.validation_reason)
    }
    exit 0
}

if ([string]::IsNullOrWhiteSpace($RunId)) {
    throw "RunId is required for review mode."
}

$body = @{
    run_id = $RunId
    action = $Action
    note = $Note
} | ConvertTo-Json

$result = Invoke-RestMethod -Uri "$BaseUrl/admin/runs/review" -Method Post -Headers $headers -Body $body -ContentType "application/json"
Write-Host "run_id: $($result.run_id)"
Write-Host "validation_state: $($result.validation_state)"
Write-Host "validation_reason: $($result.validation_reason)"
