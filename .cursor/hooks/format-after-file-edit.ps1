#Requires -Version 5.1

# Formats only the edited C# file. `dotnet format whitespace --folder` needs no MSBuild
# workspace load, so it runs in about a second and needs no debounce lock.
# Unused-using cleanup needs a workspace load; leave it to the /dotnet-format command.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$rawPayload = [Console]::In.ReadToEnd()
if ([string]::IsNullOrWhiteSpace($rawPayload)) {
  exit 0
}

$filePath = $null
try {
  $payload = $rawPayload | ConvertFrom-Json
  $filePathProperty = $payload.PSObject.Properties['file_path']
  if ($null -ne $filePathProperty) {
    $filePath = [string]$filePathProperty.Value
  }
} catch {
  exit 0
}

if ([string]::IsNullOrWhiteSpace($filePath)) {
  exit 0
}

# .cursor/hooks/format-after-file-edit.ps1 -> .cursor/hooks -> .cursor -> repo root
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$dotnetRoot = Join-Path $repoRoot 'dotnet'

$fullPath = if ([System.IO.Path]::IsPathRooted($filePath)) { $filePath } else { Join-Path $repoRoot $filePath }
$fullPath = [System.IO.Path]::GetFullPath($fullPath)

$isCSharpFile = $fullPath.EndsWith('.cs', [System.StringComparison]::OrdinalIgnoreCase)
$isUnderDotnet = $fullPath.StartsWith($dotnetRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)
$isBuildOutput = $fullPath -match '[\\/](bin|obj)[\\/]'
if (-not $isCSharpFile -or -not $isUnderDotnet -or $isBuildOutput -or -not (Test-Path -LiteralPath $fullPath)) {
  exit 0
}

# --include is resolved against the --folder path. A path that matches nothing still exits 0,
# so it must stay relative to dotnet/. Run from the repository root so global.json applies.
Push-Location $repoRoot
try {
  $relativePath = $fullPath.Substring($dotnetRoot.Length + 1).Replace('\', '/')
  & dotnet format whitespace dotnet --folder --include $relativePath --verbosity quiet
  if ($LASTEXITCODE -ne 0) {
    [Console]::Error.WriteLine("dotnet format failed: $relativePath")
    exit $LASTEXITCODE
  }
} finally {
  Pop-Location
}
