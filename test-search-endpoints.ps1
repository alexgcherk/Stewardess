#Requires -Version 5.1
<#
.SYNOPSIS
    Smoke-tests all major file-search endpoints of the StewardessMCPService.

.DESCRIPTION
    Calls every repo-browser search surface (tree, grep, file, find, search)
    with representative payloads and prints a coloured pass/fail summary.
    Works against any repository the service is currently configured to browse.

.PARAMETER BaseUrl
    Service base URL.  Default: http://localhost:55703

.PARAMETER ApiKey
    API key sent as a Bearer token.

.EXAMPLE
    .\test-search-endpoints.ps1
    .\test-search-endpoints.ps1 -BaseUrl http://myserver:55703 -ApiKey "..."
#>

param(
    [string] $BaseUrl = "http://localhost:55703",
    [string] $ApiKey  = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference    = "SilentlyContinue"

# ── helpers ───────────────────────────────────────────────────────────────────

$headers   = @{ Authorization = "Bearer $ApiKey"; Accept = "application/json" }
$passCount = 0
$failCount = 0
$cases     = [System.Collections.Generic.List[PSCustomObject]]::new()

# Unwrap the standard API envelope { success, data, requestId, timestamp }
function Unwrap { param($r); if ($null -ne $r.data) { return $r.data } else { return $r } }

# Execute one named test case.  $Assert receives the unwrapped data object.
function Invoke-Case {
    param(
        [string]      $Name,
        [string]      $Method,
        [string]      $Url,
        [object]      $Body   = $null,
        [scriptblock] $Assert
    )
    $status = "PASS"
    $detail = ""
    try {
        $params = @{
            Method          = $Method
            Uri             = $Url
            Headers         = $headers
            UseBasicParsing = $true
            TimeoutSec      = 30
        }
        if ($Body) {
            $params.Body        = ($Body | ConvertTo-Json -Depth 10)
            $params.ContentType = "application/json"
        }
        $raw  = Invoke-WebRequest @params
        $data = Unwrap ($raw.Content | ConvertFrom-Json)
        & $Assert $data
        $detail = "HTTP $($raw.StatusCode)"
    }
    catch [System.Net.WebException] {
        $resp = $_.Exception.Response
        if ($resp) {
            $status = "FAIL"
            $detail = "HTTP $([int]$resp.StatusCode) - $($_.Exception.Message)"
        } else {
            $status = "FAIL"
            $detail = "Network error - $($_.Exception.Message)"
        }
    }
    catch {
        $status = "FAIL"
        $detail = $_.Exception.Message
    }

    $tag   = if ($status -eq "PASS") { "[PASS]" } else { "[FAIL]" }
    $color = if ($status -eq "PASS") { "Green"  } else { "Red"    }
    Write-Host ("  {0,-6} {1}" -f $tag, $Name) -ForegroundColor $color
    if ($detail) { Write-Host "         $detail" -ForegroundColor DarkGray }

    $script:cases.Add([PSCustomObject]@{ Name = $Name; Status = $status; Detail = $detail })
    if ($status -eq "PASS") { $script:passCount++ } else { $script:failCount++ }
}

# Verify a request returns HTTP 400.
function Invoke-Expect400 {
    param(
        [string] $Name,
        [string] $Method,
        [string] $Url,
        [object] $Body = $null
    )
    $status = "PASS"
    $detail = ""
    try {
        $params = @{
            Method          = $Method
            Uri             = $Url
            Headers         = $headers
            UseBasicParsing = $true
            TimeoutSec      = 30
        }
        if ($Body) {
            $params.Body        = ($Body | ConvertTo-Json -Depth 10)
            $params.ContentType = "application/json"
        }
        $raw    = Invoke-WebRequest @params
        $status = "FAIL"
        $detail = "Expected HTTP 400, got $($raw.StatusCode)"
    }
    catch [System.Net.WebException] {
        $resp = $_.Exception.Response
        if ($resp) {
            $code = [int]$resp.StatusCode
            if ($code -eq 400) {
                $detail = "HTTP 400 (expected)"
            } else {
                $status = "FAIL"
                $detail = "Expected 400, got $code"
            }
        } else {
            $status = "FAIL"
            $detail = "Network error - $($_.Exception.Message)"
        }
    }
    catch {
        $status = "FAIL"
        $detail = $_.Exception.Message
    }

    $tag   = if ($status -eq "PASS") { "[PASS]" } else { "[FAIL]" }
    $color = if ($status -eq "PASS") { "Green"  } else { "Red"    }
    Write-Host ("  {0,-6} {1}" -f $tag, $Name) -ForegroundColor $color
    if ($detail) { Write-Host "         $detail" -ForegroundColor DarkGray }

    $script:cases.Add([PSCustomObject]@{ Name = $Name; Status = $status; Detail = $detail })
    if ($status -eq "PASS") { $script:passCount++ } else { $script:failCount++ }
}

function Assert-Prop {
    param($obj, [string]$prop)
    if ($null -eq $obj.$prop) { throw "Missing property '$prop'" }
}

# ── connectivity pre-check ────────────────────────────────────────────────────

Write-Host ""
Write-Host "Stewardess MCP Service - file-search endpoint smoke tests" -ForegroundColor Cyan
Write-Host "BaseUrl : $BaseUrl"
Write-Host "ApiKey  : $($ApiKey.Substring(0, [Math]::Min(8,$ApiKey.Length)))..."
Write-Host ""

try {
    $ping     = Invoke-WebRequest "$BaseUrl/api/health" -Headers $headers -UseBasicParsing -TimeoutSec 10
    $pingData = Unwrap ($ping.Content | ConvertFrom-Json)
    Write-Host "  Service reachable  HTTP $($ping.StatusCode)  status=$($pingData.status)" -ForegroundColor Cyan
} catch {
    Write-Host "  ERROR: Cannot reach $BaseUrl  -  is the service running?" -ForegroundColor Red
    Write-Host "  $_"
    exit 1
}

# ── discovery: probe real repo structure for data-driven assertions ───────────

$rootTree = Unwrap (Invoke-RestMethod ("$BaseUrl/api/repo-browser/tree?maxDepth=1") -Headers $headers -TimeoutSec 30)
$firstDir = ($rootTree.items | Where-Object { $_.kind -eq "directory" } | Select-Object -First 1)

$deepTree = Unwrap (Invoke-RestMethod ("$BaseUrl/api/repo-browser/tree?maxDepth=3&includeDirectories=false") -Headers $headers -TimeoutSec 30)
$firstCs  = ($deepTree.items | Where-Object { $_.name -like "*.cs" } | Select-Object -First 1)

$firstDirPath  = if ($firstDir) { $firstDir.path  } else { "" }
$firstCsPath   = if ($firstCs)  { $firstCs.path   } else { "" }
$firstCsName   = if ($firstCs)  { $firstCs.name   } else { "" }
$firstCsStem   = if ($firstCs)  { [System.IO.Path]::GetFileNameWithoutExtension($firstCs.name) } else { "Program" }
$firstCsStemUC = $firstCsStem.ToUpper()

Write-Host "  Repo root : $($rootTree.rootPath)"
if ($firstDir) { Write-Host "  First dir : $firstDirPath" }
if ($firstCs)  { Write-Host "  First .cs : $firstCsPath" }
Write-Host ""

# =============================================================================
# 1. TREE  (GET /api/repo-browser/tree)
# =============================================================================

Write-Host "-- GET /api/repo-browser/tree --" -ForegroundColor Yellow

Invoke-Case "tree: root at default depth" `
    GET "$BaseUrl/api/repo-browser/tree?maxDepth=1" -Assert {
    param($d)
    Assert-Prop $d "items"
    Assert-Prop $d "entryCount"
    Assert-Prop $d "rootPath"
    if ($d.entryCount -eq 0) { throw "entryCount=0, expected entries" }
}

if ($firstDirPath) {
    $encDir = [Uri]::EscapeDataString($firstDirPath)
    Invoke-Case "tree: specific subdirectory" `
        GET "$BaseUrl/api/repo-browser/tree?relativePath=$encDir&maxDepth=1" -Assert {
        param($d)
        if ($d.relativePath -ne $firstDirPath) {
            throw "relativePath echo mismatch: $($d.relativePath)"
        }
    }
}

Invoke-Case "tree: directories only" `
    GET "$BaseUrl/api/repo-browser/tree?maxDepth=2&includeFiles=false" -Assert {
    param($d)
    $files = @($d.items | Where-Object { $_.kind -eq "file" })
    if ($files.Count -gt 0) { throw "includeFiles=false still returned $($files.Count) files" }
}

Invoke-Case "tree: files only" `
    GET "$BaseUrl/api/repo-browser/tree?maxDepth=1&includeDirectories=false" -Assert {
    param($d)
    $dirs = @($d.items | Where-Object { $_.kind -eq "directory" })
    if ($dirs.Count -gt 0) { throw "includeDirectories=false still returned $($dirs.Count) dirs" }
}

Invoke-Case "tree: dot path same as root" `
    GET "$BaseUrl/api/repo-browser/tree?relativePath=.&maxDepth=1" -Assert {
    param($d)
    if ($d.entryCount -eq 0) { throw "Dot path returned 0 entries" }
}

Invoke-Case "tree: maxEntries cap honoured" `
    GET "$BaseUrl/api/repo-browser/tree?maxDepth=5&maxEntries=5" -Assert {
    param($d)
    if ($d.items.Count -gt 5) { throw "maxEntries=5 returned $($d.items.Count) entries" }
}

Invoke-Case "tree: item shape is correct" `
    GET "$BaseUrl/api/repo-browser/tree?maxDepth=1" -Assert {
    param($d)
    $item = $d.items | Select-Object -First 1
    Assert-Prop $item "path"
    Assert-Prop $item "name"
    Assert-Prop $item "kind"
    Assert-Prop $item "depth"
    if ($item.kind -notin @("file","directory")) { throw "kind must be file|directory, got $($item.kind)" }
}

Write-Host ""

# =============================================================================
# 2. GREP  (POST /api/repo-browser/grep)
# =============================================================================

Write-Host "-- POST /api/repo-browser/grep --" -ForegroundColor Yellow

Invoke-Case "grep: literal match for 'namespace'" `
    POST "$BaseUrl/api/repo-browser/grep" `
    -Body @{ query = "namespace"; mode = "literal"; maxResults = 5 } -Assert {
    param($d)
    Assert-Prop $d "items"
    Assert-Prop $d "matchCount"
    if ($d.matchCount -eq 0) { throw "No matches for 'namespace'" }
}

Invoke-Case "grep: word mode" `
    POST "$BaseUrl/api/repo-browser/grep" `
    -Body @{ query = "class"; mode = "word"; maxResults = 10 } -Assert {
    param($d)
    if ($d.items.Count -eq 0) { throw "Word-mode 'class' returned 0 items" }
    $item = $d.items | Select-Object -First 1
    Assert-Prop $item "filePath"
    Assert-Prop $item "lineNumber"
    Assert-Prop $item "lineText"
}

Invoke-Case "grep: regex mode" `
    POST "$BaseUrl/api/repo-browser/grep" `
    -Body @{ query = "public\s+(class|interface|enum)\s+\w+"; mode = "regex"; maxResults = 10 } -Assert {
    param($d)
    if ($d.items.Count -eq 0) { throw "Regex grep returned no matches" }
}

Invoke-Case "grep: contextLines included" `
    POST "$BaseUrl/api/repo-browser/grep" `
    -Body @{ query = "namespace"; mode = "literal"; maxResults = 3; contextLines = 2 } -Assert {
    param($d)
    $item = $d.items | Select-Object -First 1
    if ($null -eq $item.beforeContext -and $null -eq $item.afterContext) {
        throw "contextLines not reflected in response"
    }
}

if ($firstDirPath) {
    Invoke-Case "grep: pathPrefix restricts to subdirectory" `
        POST "$BaseUrl/api/repo-browser/grep" `
        -Body @{ query = "namespace"; pathPrefix = $firstDirPath; maxResults = 20 } -Assert {
        param($d)
        foreach ($item in $d.items) {
            if (-not $item.filePath.StartsWith($firstDirPath, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Result outside pathPrefix '$firstDirPath': $($item.filePath)"
            }
        }
    }
}

Invoke-Expect400 "grep: missing query returns 400" `
    POST "$BaseUrl/api/repo-browser/grep" -Body @{ mode = "literal" }

Write-Host ""

# =============================================================================
# 3. READ FILE  (GET /api/repo-browser/file)
# =============================================================================

Write-Host "-- GET /api/repo-browser/file --" -ForegroundColor Yellow

if ($firstCsPath) {
    $enc = [Uri]::EscapeDataString($firstCsPath)

    Invoke-Case "file: full read of known .cs file" `
        GET "$BaseUrl/api/repo-browser/file?filePath=$enc" -Assert {
        param($d)
        Assert-Prop $d "content"
        Assert-Prop $d "exists"
        if (-not $d.exists) { throw "exists=false for '$firstCsPath'" }
        if ([string]::IsNullOrEmpty($d.content)) { throw "content is empty" }
    }

    Invoke-Case "file: partial read (startLine=1 endLine=5)" `
        GET "$BaseUrl/api/repo-browser/file?filePath=$enc&startLine=1&endLine=5" -Assert {
        param($d)
        if (-not $d.exists) { throw "exists=false" }
        $lines = ($d.content -split "`n").Count
        if ($lines -gt 10) { throw "startLine/endLine not respected, got $lines lines" }
    }
} else {
    Write-Host "  (skipped - no .cs file found at tree depth 3)" -ForegroundColor DarkGray
}

Invoke-Expect400 "file: missing filePath returns 400" `
    GET "$BaseUrl/api/repo-browser/file"

Invoke-Case "file: non-existent path returns exists=false" `
    GET "$BaseUrl/api/repo-browser/file?filePath=does%2Fnot%2Fexist.cs" -Assert {
    param($d)
    if ($d.exists -ne $false) { throw "Expected exists=false, got $($d.exists)" }
}

Write-Host ""

# =============================================================================
# 4. FIND PATH  (POST /api/repo-browser/find)
# =============================================================================

Write-Host "-- POST /api/repo-browser/find --" -ForegroundColor Yellow

Invoke-Case "find: name mode - partial match" `
    POST "$BaseUrl/api/repo-browser/find" `
    -Body @{ query = $firstCsStem; matchMode = "name"; targetKind = "file" } -Assert {
    param($d)
    Assert-Prop $d "resultCount"
    if ($d.resultCount -eq 0) { throw "No results for name '$firstCsStem'" }
    $item = $d.items | Select-Object -First 1
    Assert-Prop $item "path"
    Assert-Prop $item "matchReason"
}

if ($firstCsName) {
    Invoke-Case "find: name mode - exact filename" `
        POST "$BaseUrl/api/repo-browser/find" `
        -Body @{ query = $firstCsName; matchMode = "name"; targetKind = "file" } -Assert {
        param($d)
        if ($d.resultCount -eq 0) { throw "Exact name '$firstCsName' not found" }
    }
}

if ($firstDirPath) {
    Invoke-Case "find: path_fragment mode" `
        POST "$BaseUrl/api/repo-browser/find" `
        -Body @{ query = $firstDirPath; matchMode = "path_fragment"; targetKind = "file" } -Assert {
        param($d)
        if ($d.resultCount -eq 0) { throw "path_fragment '$firstDirPath' returned 0" }
    }
}

Invoke-Case "find: targetKind=directory" `
    POST "$BaseUrl/api/repo-browser/find" `
    -Body @{ query = $firstCsStem; matchMode = "name"; targetKind = "directory" } -Assert {
    param($d)
    foreach ($item in $d.items) {
        if ($item.kind -ne "directory") {
            throw "Got non-directory '$($item.path)' with targetKind=directory"
        }
    }
}

if ($firstCsName) {
    $regexAnchoredName = "^" + [Regex]::Escape($firstCsName) + "$"
    Invoke-Case "find: regex anchored name (auto-detected)" `
        POST "$BaseUrl/api/repo-browser/find" `
        -Body @{ query = $regexAnchoredName; matchMode = "name"; targetKind = "file" } -Assert {
        param($d)
        if ($d.resultCount -eq 0) { throw "Anchored regex for '$firstCsName' found nothing" }
        foreach ($item in $d.items) {
            if ($item.name -ne $firstCsName) {
                throw "Regex returned unexpected name '$($item.name)'"
            }
        }
    }

    $regexPathFrag = [Regex]::Escape($firstDirPath) + "[/\\].*\.cs$"
    Invoke-Case "find: regex path_fragment (auto-detected)" `
        POST "$BaseUrl/api/repo-browser/find" `
        -Body @{ query = $regexPathFrag; matchMode = "path_fragment"; targetKind = "file" } -Assert {
        param($d)
        if ($d.resultCount -eq 0) { throw "Regex path_fragment found nothing" }
    }
}

Invoke-Expect400 "find: missing query returns 400" `
    POST "$BaseUrl/api/repo-browser/find" -Body @{ matchMode = "name" }

Write-Host ""

# =============================================================================
# 5. SEARCH  (GET /api/repo-browser/search)
# =============================================================================

Write-Host "-- GET /api/repo-browser/search --" -ForegroundColor Yellow

Invoke-Case "search: partial name match" `
    GET ("$BaseUrl/api/repo-browser/search?query=" + [Uri]::EscapeDataString($firstCsStem)) -Assert {
    param($d)
    Assert-Prop $d "items"
    Assert-Prop $d "resultCount"
    if ($d.resultCount -eq 0) { throw "No results for '$firstCsStem'" }
    $item = $d.items | Select-Object -First 1
    Assert-Prop $item "path"
    Assert-Prop $item "name"
}

if ($firstCsName) {
    Invoke-Case "search: exact filename" `
        GET ("$BaseUrl/api/repo-browser/search?query=" + [Uri]::EscapeDataString($firstCsName)) -Assert {
        param($d)
        if ($d.resultCount -eq 0) { throw "'$firstCsName' not found" }
    }
}

Invoke-Case "search: wildcard *.cs" `
    GET ("$BaseUrl/api/repo-browser/search?query=" + [Uri]::EscapeDataString("*.cs")) -Assert {
    param($d)
    if ($d.resultCount -eq 0) { throw "Wildcard '*.cs' returned 0" }
    foreach ($item in $d.items) {
        if (-not $item.name.EndsWith(".cs")) { throw "Non-.cs result: $($item.name)" }
    }
}

Invoke-Case "search: regex suffix .cs (auto-detected)" `
    GET ("$BaseUrl/api/repo-browser/search?query=" + [Uri]::EscapeDataString('\.cs$')) -Assert {
    param($d)
    if ($d.resultCount -eq 0) { throw "Regex '\.cs$' returned 0" }
    foreach ($item in $d.items) {
        if (-not $item.name.EndsWith(".cs")) { throw "Non-.cs result: $($item.name)" }
    }
}

if ($firstCsName) {
    $regexExact = "^" + [Regex]::Escape($firstCsName) + "$"
    Invoke-Case "search: regex anchored exact name (auto-detected)" `
        GET ("$BaseUrl/api/repo-browser/search?query=" + [Uri]::EscapeDataString($regexExact)) -Assert {
        param($d)
        if ($d.resultCount -eq 0) { throw "Anchored regex for '$firstCsName' found nothing" }
        foreach ($item in $d.items) {
            if ($item.name -ne $firstCsName) { throw "Unexpected name '$($item.name)'" }
        }
    }
}

Invoke-Case "search: maxResults respected" `
    GET ("$BaseUrl/api/repo-browser/search?query=" + [Uri]::EscapeDataString("*.cs") + "&maxResults=3") -Assert {
    param($d)
    if ($d.items.Count -gt 3) { throw "maxResults=3 but got $($d.items.Count) items" }
}

Invoke-Case "search: case-insensitive by default" `
    GET ("$BaseUrl/api/repo-browser/search?query=" + [Uri]::EscapeDataString($firstCsStemUC)) -Assert {
    param($d)
    if ($d.resultCount -eq 0) { throw "Case-insensitive search returned 0 for '$firstCsStemUC'" }
}

if ($firstDirPath) {
    Invoke-Case "search: pathPrefix restricts results" `
        GET ("$BaseUrl/api/repo-browser/search?query=" + [Uri]::EscapeDataString("*.cs") +
             "&pathPrefix=" + [Uri]::EscapeDataString($firstDirPath)) -Assert {
        param($d)
        foreach ($item in $d.items) {
            if (-not $item.path.StartsWith($firstDirPath, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Result outside pathPrefix '$firstDirPath': $($item.path)"
            }
        }
    }
}

Invoke-Expect400 "search: missing query returns 400" `
    GET "$BaseUrl/api/repo-browser/search"

Write-Host ""

# =============================================================================
# Summary
# =============================================================================

$total = $passCount + $failCount
$bar   = "-" * 60
Write-Host $bar -ForegroundColor Cyan
$resultColor = if ($failCount -eq 0) { "Green" } else { "Yellow" }
Write-Host ("  Results : {0} / {1} passed" -f $passCount, $total) -ForegroundColor $resultColor

if ($failCount -gt 0) {
    Write-Host ""
    Write-Host "  Failed cases:" -ForegroundColor Red
    $cases | Where-Object { $_.Status -eq "FAIL" } | ForEach-Object {
        Write-Host "    [FAIL] $($_.Name)" -ForegroundColor Red
        Write-Host "           $($_.Detail)" -ForegroundColor DarkGray
    }
}
Write-Host $bar -ForegroundColor Cyan
Write-Host ""

exit $(if ($failCount -eq 0) { 0 } else { 1 })
