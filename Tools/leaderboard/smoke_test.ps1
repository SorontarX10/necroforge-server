param(
    [string]$BaseUrl = "http://localhost:8080",
    [string]$BuildVersion = "smoke"
)

$ErrorActionPreference = "Stop"

function New-Signature {
    param(
        [string]$SessionKey,
        [string]$CanonicalPayload
    )

    $hmac = New-Object System.Security.Cryptography.HMACSHA256
    $hmac.Key = [System.Text.Encoding]::UTF8.GetBytes($SessionKey)
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($CanonicalPayload)
    $hash = $hmac.ComputeHash($bytes)
    return [System.Convert]::ToBase64String($hash)
}

$playerId = "smoke-player-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
$displayName = "SmokeRunner"

$startBody = @{
    player_id = $playerId
    display_name = $displayName
    season = "global_all_time"
    build_version = $BuildVersion
} | ConvertTo-Json

$start = Invoke-RestMethod -Uri "$BaseUrl/runs/start" -Method Post -Body $startBody -ContentType "application/json"

$runId = $start.run_id
$sessionKey = $start.session_key
$score = 9999
$duration = 120.5
$kills = 42
$buildVersion = $BuildVersion
$isCheat = $false
$cheatBit = if ($isCheat) { "1" } else { "0" }
$durationNorm = $duration.ToString("0.###", [System.Globalization.CultureInfo]::InvariantCulture)
$canonical = "$runId|$playerId|$score|$durationNorm|$kills|$buildVersion|$cheatBit"
$signature = New-Signature -SessionKey $sessionKey -CanonicalPayload $canonical

$submitBody = @{
    run_id = $runId
    player_id = $playerId
    display_name = $displayName
    score = $score
    run_duration_sec = $duration
    kills = $kills
    build_version = $buildVersion
    is_cheat_session = $isCheat
    signature = $signature
} | ConvertTo-Json

$submit = Invoke-RestMethod -Uri "$BaseUrl/runs/submit" -Method Post -Body $submitBody -ContentType "application/json"
$top = Invoke-RestMethod -Uri "$BaseUrl/leaderboard?season=global_all_time&page=1&page_size=5" -Method Get
$me = Invoke-RestMethod -Uri "$BaseUrl/leaderboard/me?season=global_all_time&player_id=$playerId" -Method Get

Write-Host "run_id: $runId"
Write-Host "submit validation_state: $($submit.validation_state)"
Write-Host "submit validation_reason: $($submit.validation_reason)"
Write-Host "top count: $($top.entries.Count)"
Write-Host "my rank found: $($me.found)"
