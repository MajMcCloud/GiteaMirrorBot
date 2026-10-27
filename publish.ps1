<#
.SYNOPSIS
	Publishes the GiteaMirrorBot for all platforms and packs each build as ZIP.

.DESCRIPTION
	Builds the project for Windows, macOS and Linux (x64 + ARM64). The build
	variants (framework-dependent and/or self-contained) can be chosen via the
	-Variant parameter or interactively. Every build is zipped into the
	"Publish" folder at solution level. The real appsettings.json is replaced
	by appsettings.example.json in the packages so no API keys are distributed.

.PARAMETER Version
	Version to stamp into the assemblies and ZIP file names (e.g. 1.0.0 or 1.2.3-beta.1).
	If omitted, the script asks interactively.

.PARAMETER Variant
	Which build variants to create: All, FrameworkDependent or SelfContained.
	If omitted, the script asks interactively.

.PARAMETER Configuration
	Build configuration, defaults to Release.

.EXAMPLE
	.\publish.ps1
	.\publish.ps1 -Version 1.2.0 -Variant SelfContained
#>
[CmdletBinding()]
param(
	[string]$Version,
	[string]$Variant,
	[string]$Configuration = "Release"
)

$ErrorActionPreference = 'Stop'

$SolutionRoot = $PSScriptRoot
$ProjectDir   = Join-Path $SolutionRoot 'GiteaMirrorBot'
$Project      = Join-Path $ProjectDir 'GiteaMirrorBot.csproj'
$PublishDir   = Join-Path $SolutionRoot 'Publish'
$TempDir      = Join-Path $PublishDir '_temp'
$AppName      = 'GiteaMirrorBot'

$RuntimeIds = @(
	'win-x64',  'win-arm64',
	'osx-x64',  'osx-arm64',
	'linux-x64', 'linux-arm64'
)

$AllVariants = @(
	@{ Key = 'FrameworkDependent'; Name = 'framework-dependent'; SelfContained = $false }
	@{ Key = 'SelfContained';      Name = 'self-contained';      SelfContained = $true }
)

# --- Ask for version / validate -----------------------------------------------------
$versionPattern = '^\d+\.\d+\.\d+(-[0-9A-Za-z.\-]+)?$'
while ([string]::IsNullOrWhiteSpace($Version)) {
	$Version = Read-Host 'Please enter the version number (e.g. 1.0.0)'
	if ($Version -notmatch $versionPattern) {
		Write-Host "  Invalid version '$Version'. Format: MAJOR.MINOR.PATCH[-PreRelease]" -ForegroundColor Yellow
		$Version = $null
	}
}

# --- Ask which variants to build / validate ------------------------------------------
while ($true) {
	if ([string]::IsNullOrWhiteSpace($Variant)) {
		Write-Host ''
		Write-Host 'Which build variants should be created?'
		Write-Host '  [1] Framework-dependent (small, requires the .NET runtime to be installed)'
		Write-Host '  [2] Self-contained (includes the .NET runtime, larger)'
		Write-Host '  [3] Both (default)'
		$Variant = Read-Host 'Choose 1, 2 or 3'
	}
	switch ($Variant.ToLowerInvariant()) {
		'frameworkdependent' { $Variant = 'FrameworkDependent'; break }
		'1'                  { $Variant = 'FrameworkDependent'; break }
		'selfcontained'      { $Variant = 'SelfContained';      break }
		'2'                  { $Variant = 'SelfContained';      break }
		{ $_ -in 'all', '3', '' } { $Variant = 'All';          break }
		default {
			Write-Host "  Unknown choice '$Variant'. Please enter 1, 2 or 3." -ForegroundColor Yellow
			$Variant = $null
		}
	}
	if (-not [string]::IsNullOrWhiteSpace($Variant)) { break }
}

$selectedVariants = @(if ($Variant -eq 'All') {
	$AllVariants
} else {
	$AllVariants | Where-Object { $_.Key -eq $Variant }
})

Write-Host ''
Write-Host "=== $AppName Publish v$Version ($Configuration) ===" -ForegroundColor Green
Write-Host "  Output dir : $PublishDir"
Write-Host "  Targets    : $($RuntimeIds -join ', ')"
Write-Host "  Variants   : $($selectedVariants.Name -join ', ')"
Write-Host ''

# --- Prepare --------------------------------------------------------------------------
if (Test-Path $TempDir) { Remove-Item $TempDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $PublishDir | Out-Null
New-Item -ItemType Directory -Force -Path $TempDir | Out-Null

Add-Type -AssemblyName System.IO.Compression.FileSystem

$swTotal = [System.Diagnostics.Stopwatch]::StartNew()
$results = [System.Collections.Generic.List[object]]::new()
$total   = $RuntimeIds.Count * $selectedVariants.Count
$step    = 0

try {
	foreach ($rid in $RuntimeIds) {
		foreach ($variantDef in $selectedVariants) {
			$step++
			$label  = "$AppName-$Version-$rid-$($variantDef.Name)"
			$outDir = Join-Path $TempDir $label
			$zip    = Join-Path $PublishDir "$label.zip"

			Write-Host ("[{0}/{1}] {2}" -f $step, $total, $label) -ForegroundColor Cyan

			$sw = [System.Diagnostics.Stopwatch]::StartNew()
			dotnet publish $Project -c $Configuration -r $rid --self-contained:$($variantDef.SelfContained) `
				-o $outDir -p:Version=$Version -p:DebugType=none --nologo -v minimal
			if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed: $label" }

			# Replace the real appsettings.json (with keys) by the example configuration
			$pubSettings = Join-Path $outDir 'appsettings.json'
			if (Test-Path $pubSettings) { Remove-Item $pubSettings -Force }
			$exampleSettings = Join-Path $ProjectDir 'appsettings.example.json'
			if (Test-Path $exampleSettings) { Copy-Item $exampleSettings $pubSettings }

			if (Test-Path $zip) { Remove-Item $zip -Force }
			[System.IO.Compression.ZipFile]::CreateFromDirectory(
				$outDir, $zip,
				[System.IO.Compression.CompressionLevel]::Optimal,
				$true  # keep the folder name as top-level entry inside the ZIP
			)

			$sizeMb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
			$results.Add([pscustomobject]@{
				File    = Split-Path $zip -Leaf
				SizeMB  = $sizeMb
				Seconds = [math]::Round($sw.Elapsed.TotalSeconds, 0)
			})
			Write-Host ("        -> {0} ({1} MB, {2}s)" -f (Split-Path $zip -Leaf), $sizeMb, [math]::Round($sw.Elapsed.TotalSeconds, 0)) -ForegroundColor DarkGreen
		}
	}
}
finally {
	if (Test-Path $TempDir) { Remove-Item $TempDir -Recurse -Force -ErrorAction SilentlyContinue }
}

# --- Summary ---------------------------------------------------------------------------
Write-Host ''
Write-Host "=== Done in $([math]::Round($swTotal.Elapsed.TotalMinutes, 1)) minutes - $($results.Count) packages in $PublishDir ===" -ForegroundColor Green
$results | Format-Table -AutoSize
