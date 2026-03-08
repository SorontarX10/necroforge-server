param(
    [string]$BaseUrl = "http://localhost:8080",
    [string]$BuildVersion = "smoke",
    [int]$PollAttempts = 10,
    [int]$PollDelaySeconds = 1
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

function Assert-True {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Invoke-JsonGet {
    param([string]$Url)
    return Invoke-RestMethod -Uri $Url -Method Get
}

function Invoke-JsonPost {
    param(
        [string]$Url,
        [hashtable]$Body
    )

    $json = $Body | ConvertTo-Json
    return Invoke-RestMethod -Uri $Url -Method Post -Body $json -ContentType "application/json"
}

$playerId = "integration-player-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
$displayName = "IntegrationRunner"
$season = "global_all_time"

$start = Invoke-JsonPost -Url "$BaseUrl/runs/start" -Body @{
    player_id = $playerId
    display_name = $displayName
    season = $season
    build_version = $BuildVersion
}

Assert-True -Condition (-not [string]::IsNullOrWhiteSpace($start.run_id)) -Message "runs/start did not return run_id."
Assert-True -Condition (-not [string]::IsNullOrWhiteSpace($start.session_key)) -Message "runs/start did not return session_key."

$score = 987654321
$duration = 120.5
$kills = 42
$isCheat = $false
$cheatBit = if ($isCheat) { "1" } else { "0" }
$durationNorm = $duration.ToString("0.###", [System.Globalization.CultureInfo]::InvariantCulture)
$canonical = "$($start.run_id)|$playerId|$score|$durationNorm|$kills|$BuildVersion|$cheatBit"
$signature = New-Signature -SessionKey $start.session_key -CanonicalPayload $canonical

$submit = Invoke-JsonPost -Url "$BaseUrl/runs/submit" -Body @{
    run_id = $start.run_id
    player_id = $playerId
    display_name = $displayName
    score = $score
    run_duration_sec = $duration
    kills = $kills
    build_version = $BuildVersion
    is_cheat_session = $isCheat
    signature = $signature
}

Assert-True -Condition (-not [string]::IsNullOrWhiteSpace($submit.validation_state)) -Message "runs/submit did not return validation_state."

if ($submit.validation_state -ne "accepted") {
    $reason = $submit.validation_reason
    throw "runs/submit validation_state='$($submit.validation_state)' reason='$reason'. " +
        "Integration rank assertions require accepted run. " +
        "If backend has LEADERBOARD_VERSION_LOCK, pass matching -BuildVersion."
}

$me = $null
for ($attempt = 1; $attempt -le [Math]::Max(1, $PollAttempts); $attempt++) {
    $me = Invoke-JsonGet -Url "$BaseUrl/leaderboard/me?season=$season&player_id=$playerId"
    if ($me.found -eq $true -and $me.entry -ne $null) {
        break
    }

    if ($attempt -lt $PollAttempts) {
        Start-Sleep -Seconds ([Math]::Max(0, $PollDelaySeconds))
    }
}

Assert-True -Condition ($me -ne $null) -Message "leaderboard/me did not return a response."
Assert-True -Condition ($me.found -eq $true) -Message "leaderboard/me found=false after polling."
Assert-True -Condition ($me.entry -ne $null) -Message "leaderboard/me entry is null."
Assert-True -Condition ($me.entry.player_id -eq $playerId) -Message "leaderboard/me returned wrong player_id."
Assert-True -Condition ([int]$me.entry.score -eq $score) -Message "leaderboard/me returned wrong score."

$top = Invoke-JsonGet -Url "$BaseUrl/leaderboard?season=$season&page=1&page_size=20"
Assert-True -Condition ($top -ne $null) -Message "leaderboard top request returned null."
Assert-True -Condition ($top.entries -ne $null) -Message "leaderboard top did not return entries."

$inTop = $false
foreach ($entry in $top.entries) {
    if ($entry.player_id -eq $playerId) {
        $inTop = $true
        break
    }
}

Assert-True -Condition $inTop -Message "submitted player not found in top page after accepted run."

Write-Host "integration status: PASS"
Write-Host "run_id: $($start.run_id)"
Write-Host "validation_state: $($submit.validation_state)"
Write-Host "my_rank: $($me.entry.rank)"
Write-Host "player_id: $playerId"
