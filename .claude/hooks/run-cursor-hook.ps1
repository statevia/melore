#Requires -Version 5.1

<#
.SYNOPSIS
  Claude Code の PostToolUse フックから、正本の Cursor フック（.cursor/hooks/*.ps1）を呼ぶ参照ラッパー。

.DESCRIPTION
  Claude Code のペイロードは tool_response に編集前後のファイル本文を含むため、そのまま渡すと
  本文中のパス文字列に正本スクリプトの正規表現が反応し、無関係なソリューションまで整形してしまう。
  そこで tool_input.file_path だけを取り出し、Cursor と同じ {"file_path": "..."} 形式で正本へ渡す。
  file_path が取れない場合は何もしない。

.PARAMETER HookName
  呼び出す正本フックのファイル名（拡張子なし）。.cursor/hooks/ 直下に存在するものに限る。
#>
param(
  [Parameter(Mandatory = $true)]
  [ValidateSet('format-after-file-edit')]
  [string]$HookName
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$rawPayload = [Console]::In.ReadToEnd()
if ([string]::IsNullOrWhiteSpace($rawPayload)) {
  exit 0
}

$filePath = $null
try {
  $payload = $rawPayload | ConvertFrom-Json
  $toolInput = $payload.PSObject.Properties['tool_input']
  if ($null -ne $toolInput -and $null -ne $toolInput.Value.PSObject.Properties['file_path']) {
    $filePath = [string]$toolInput.Value.file_path
  }
} catch {
  exit 0
}

if ([string]::IsNullOrWhiteSpace($filePath)) {
  exit 0
}

# .claude/hooks/run-cursor-hook.ps1 -> .claude/hooks -> .claude -> repo root
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$canonicalHookPath = Join-Path $repoRoot ".cursor\hooks\$HookName.ps1"
if (-not (Test-Path -LiteralPath $canonicalHookPath)) {
  [Console]::Error.WriteLine("canonical hook not found: $canonicalHookPath")
  exit 1
}

$forwardPayload = @{ file_path = $filePath } | ConvertTo-Json -Compress
$forwardPayload | & powershell -NoProfile -ExecutionPolicy Bypass -File $canonicalHookPath
exit $LASTEXITCODE
