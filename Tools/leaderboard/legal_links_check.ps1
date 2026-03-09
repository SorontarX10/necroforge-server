param(
    [string]$BaseUrl = "https://necroforge-lb.duckdns.org"
)

$ErrorActionPreference = "Stop"

function Invoke-LegalUrlCheck {
    param(
        [string]$InitialUrl,
        [bool]$RequireRedirect
    )

    $response = Invoke-WebRequest -Uri $InitialUrl -Method Get -MaximumRedirection 10 -ErrorAction Stop
    $statusCode = [int]$response.StatusCode
    $finalUrl = $response.BaseResponse.ResponseUri.AbsoluteUri
    $isHttps = $finalUrl.StartsWith("https://", [System.StringComparison]::OrdinalIgnoreCase)
    $redirected = -not $finalUrl.Equals($InitialUrl, [System.StringComparison]::OrdinalIgnoreCase)
    $is2xx = $statusCode -ge 200 -and $statusCode -lt 300
    $is404 = $statusCode -eq 404

    return [PSCustomObject]@{
        initial_url = $InitialUrl
        final_url = $finalUrl
        status_code = $statusCode
        redirected = $redirected
        is_https = $isHttps
        pass = $is2xx -and -not $is404 -and $isHttps -and (-not $RequireRedirect -or $redirected)
        require_redirect = $RequireRedirect
    }
}

$baseUri = [System.Uri]$BaseUrl
$host = $baseUri.Host

if ([string]::IsNullOrWhiteSpace($host)) {
    throw "Invalid BaseUrl: $BaseUrl"
}

$httpsOrigin = "https://$host"
$httpOrigin = "http://$host"

$paths = @(
    "/legal/privacy-policy.html",
    "/legal/eula.html",
    "/legal/third-party-licenses.html"
)

$results = New-Object System.Collections.Generic.List[object]

foreach ($path in $paths) {
    $results.Add((Invoke-LegalUrlCheck -InitialUrl "$httpsOrigin$path" -RequireRedirect:$false))
    $results.Add((Invoke-LegalUrlCheck -InitialUrl "$httpOrigin$path" -RequireRedirect:$true))
}

$failed = $results | Where-Object { -not $_.pass }

foreach ($result in $results) {
    $redirectFlag = if ($result.redirected) { "Y" } else { "N" }
    $ok = if ($result.pass) { "PASS" } else { "FAIL" }
    Write-Host "$ok status=$($result.status_code) https=$($result.is_https) redirected=$redirectFlag`n  from: $($result.initial_url)`n  to:   $($result.final_url)"
}

if ($failed.Count -gt 0) {
    throw "Legal URL check failed for $($failed.Count) request(s)."
}

Write-Host "All legal URL checks passed."
