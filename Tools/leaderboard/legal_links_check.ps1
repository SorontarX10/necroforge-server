param(
    [string]$BaseUrl = "https://necroforge-lb.duckdns.org"
)

$ErrorActionPreference = "Stop"

function Invoke-LegalUrlCheck {
    param(
        [string]$InitialUrl,
        [bool]$RequireRedirect
    )

    $statusCode = 0
    $finalUrl = $InitialUrl
    $response = $null
    $locationHeader = $null

    try {
        $response = Invoke-WebRequest -Uri $InitialUrl -Method Get -MaximumRedirection 10 -ErrorAction Stop
        $statusCode = [int]$response.StatusCode
        if ($response.BaseResponse -and $response.BaseResponse.ResponseUri) {
            $finalUrl = $response.BaseResponse.ResponseUri.AbsoluteUri
        }
        if ($response.Headers) {
            $locationHeader = $response.Headers["Location"]
        }
    } catch {
        $httpResponse = $null
        if ($_.Exception -and $_.Exception.Response) {
            $httpResponse = $_.Exception.Response
        }

        if ($httpResponse -eq $null) {
            throw
        }

        try {
            $statusCode = [int]$httpResponse.StatusCode
        } catch {
            $statusCode = 0
        }

        try {
            if ($httpResponse.ResponseUri) {
                $finalUrl = $httpResponse.ResponseUri.AbsoluteUri
            }
        } catch {
            $finalUrl = $InitialUrl
        }

        try {
            if ($httpResponse.Headers) {
                $locationHeader = $httpResponse.Headers["Location"]
            }
        } catch {
            $locationHeader = $null
        }
    }

    $isHttps = $finalUrl.StartsWith("https://", [System.StringComparison]::OrdinalIgnoreCase)
    $redirected = -not $finalUrl.Equals($InitialUrl, [System.StringComparison]::OrdinalIgnoreCase)
    $is2xx = $statusCode -ge 200 -and $statusCode -lt 300
    $is404 = $statusCode -eq 404
    $isRedirectStatus = $statusCode -ge 300 -and $statusCode -lt 400
    $locationIsHttps = -not [string]::IsNullOrWhiteSpace($locationHeader) -and $locationHeader.StartsWith("https://", [System.StringComparison]::OrdinalIgnoreCase)
    $redirectOk = ($redirected -and $isHttps) -or ($isRedirectStatus -and $locationIsHttps)
    $pass = if ($RequireRedirect) { $redirectOk } else { $is2xx -and -not $is404 -and $isHttps }

    return [PSCustomObject]@{
        initial_url = $InitialUrl
        final_url = $finalUrl
        redirect_location = $locationHeader
        status_code = $statusCode
        redirected = $redirected
        is_https = $isHttps
        pass = $pass
        require_redirect = $RequireRedirect
    }
}

$baseUri = [System.Uri]$BaseUrl
$targetHost = $baseUri.Host

if ([string]::IsNullOrWhiteSpace($targetHost)) {
    throw "Invalid BaseUrl: $BaseUrl"
}

$httpsOrigin = "https://$targetHost"
$httpOrigin = "http://$targetHost"

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
    $redirectLocation = if ($result.redirect_location) { $result.redirect_location } else { "-" }
    Write-Host "$ok status=$($result.status_code) https=$($result.is_https) redirected=$redirectFlag`n  from: $($result.initial_url)`n  to:   $($result.final_url)`n  location: $redirectLocation"
}

if ($failed.Count -gt 0) {
    throw "Legal URL check failed for $($failed.Count) request(s)."
}

Write-Host "All legal URL checks passed."
