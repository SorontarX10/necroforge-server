param(
    [string]$BaseUrl = "http://localhost:8080"
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

function New-CanonicalPayload {
    param(
        [string]$RunId,
        [string]$PlayerId,
        [int]$Score,
        [double]$Duration,
        [int]$Kills,
        [string]$BuildVersion,
        [bool]$IsCheatSession
    )

    $cheatBit = if ($IsCheatSession) { "1" } else { "0" }
    $durationNorm = $Duration.ToString("0.###", [System.Globalization.CultureInfo]::InvariantCulture)
    return "$RunId|$PlayerId|$Score|$durationNorm|$Kills|$BuildVersion|$cheatBit"
}

function Start-Run {
    param(
        [string]$BaseUrl,
        [string]$PlayerId,
        [string]$DisplayName,
        [string]$BuildVersion
    )

    $body = @{
        player_id = $PlayerId
        display_name = $DisplayName
        season = "global_all_time"
        build_version = $BuildVersion
    } | ConvertTo-Json

    return Invoke-RestMethod -Uri "$BaseUrl/runs/start" -Method Post -Body $body -ContentType "application/json"
}

function Submit-Run {
    param(
        [string]$BaseUrl,
        [string]$RunId,
        [string]$PlayerId,
        [string]$DisplayName,
        [int]$Score,
        [double]$Duration,
        [int]$Kills,
        [string]$BuildVersion,
        [bool]$IsCheatSession,
        [string]$Signature,
        [bool]$RawResponse = $false
    )

    $body = @{
        run_id = $RunId
        player_id = $PlayerId
        display_name = $DisplayName
        score = $Score
        run_duration_sec = $Duration
        kills = $Kills
        build_version = $BuildVersion
        is_cheat_session = $IsCheatSession
        signature = $Signature
    } | ConvertTo-Json

    if ($RawResponse) {
        return Invoke-WebRequest -Uri "$BaseUrl/runs/submit" -Method Post -Body $body -ContentType "application/json" -SkipHttpErrorCheck
    }

    return Invoke-RestMethod -Uri "$BaseUrl/runs/submit" -Method Post -Body $body -ContentType "application/json"
}

Write-Host "Running security smoke tests against: $BaseUrl"

# Case 1: invalid signature should be rejected (validation_state == rejected).
$player1 = "sec-invalidsig-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
$displayName = "SecuritySmoke"
$buildVersion = "smoke"
$score = 500
$duration = 120.5
$kills = 10
$isCheat = $false

$start1 = Start-Run -BaseUrl $BaseUrl -PlayerId $player1 -DisplayName $displayName -BuildVersion $buildVersion
$badSignature = "AAAA"
$invalidSigSubmit = Submit-Run `
    -BaseUrl $BaseUrl `
    -RunId $start1.run_id `
    -PlayerId $player1 `
    -DisplayName $displayName `
    -Score $score `
    -Duration $duration `
    -Kills $kills `
    -BuildVersion $buildVersion `
    -IsCheatSession $isCheat `
    -Signature $badSignature

$invalidSigOk = $invalidSigSubmit.validation_state -eq "rejected"
Write-Host "Case invalid signature -> validation_state: $($invalidSigSubmit.validation_state)"

# Case 2: duplicate submit for same run_id should return HTTP 409.
$player2 = "sec-duplicate-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
$start2 = Start-Run -BaseUrl $BaseUrl -PlayerId $player2 -DisplayName $displayName -BuildVersion $buildVersion
$canonical2 = New-CanonicalPayload `
    -RunId $start2.run_id `
    -PlayerId $player2 `
    -Score $score `
    -Duration $duration `
    -Kills $kills `
    -BuildVersion $buildVersion `
    -IsCheatSession $isCheat
$signature2 = New-Signature -SessionKey $start2.session_key -CanonicalPayload $canonical2

$firstSubmit = Submit-Run `
    -BaseUrl $BaseUrl `
    -RunId $start2.run_id `
    -PlayerId $player2 `
    -DisplayName $displayName `
    -Score $score `
    -Duration $duration `
    -Kills $kills `
    -BuildVersion $buildVersion `
    -IsCheatSession $isCheat `
    -Signature $signature2

$duplicateResponse = Submit-Run `
    -BaseUrl $BaseUrl `
    -RunId $start2.run_id `
    -PlayerId $player2 `
    -DisplayName $displayName `
    -Score $score `
    -Duration $duration `
    -Kills $kills `
    -BuildVersion $buildVersion `
    -IsCheatSession $isCheat `
    -Signature $signature2 `
    -RawResponse $true

$duplicateOk = [int]$duplicateResponse.StatusCode -eq 409
Write-Host "Case duplicate submit -> status: $([int]$duplicateResponse.StatusCode)"

$allOk = $invalidSigOk -and $duplicateOk
if (-not $allOk) {
    Write-Error "Security smoke failed. invalid_signature_ok=$invalidSigOk duplicate_ok=$duplicateOk"
    exit 1
}

Write-Host "Security smoke passed."
