[CmdletBinding()]
param(
   [string]$RepoRoot = ( Resolve-Path ( Join-Path $PSScriptRoot ".." ) ).Path,
   [string]$GameRootPath,
   [string]$Version,
   [string]$Tag,
   [string]$OutputRoot,
   [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Get-ProjectPropertyValue {
   param(
      [Parameter( Mandatory = $true )]
      [string]$ProjectFilePath,
      [Parameter( Mandatory = $true )]
      [string]$PropertyName
   )

   [xml]$projectXml = Get-Content -LiteralPath $ProjectFilePath -Raw
   foreach( $propertyGroup in $projectXml.Project.PropertyGroup ) {
      $propertyElement = $propertyGroup.SelectSingleNode( $PropertyName )
      if( $null -eq $propertyElement ) {
         continue
      }

      $propertyValue = $propertyElement.InnerText
      if( -not [string]::IsNullOrWhiteSpace( $propertyValue ) ) {
         return $propertyValue.Trim()
      }
   }

   throw "Property '$PropertyName' was not found in '$ProjectFilePath'."
}

function Copy-DirectoryContents {
   param(
      [Parameter( Mandatory = $true )]
      [string]$SourcePath,
      [Parameter( Mandatory = $true )]
      [string]$DestinationPath,
      [string[]]$ExcludeNames = @()
   )

   New-Item -ItemType Directory -Path $DestinationPath -Force | Out-Null
   Get-ChildItem -LiteralPath $SourcePath -Force | Where-Object {
      $ExcludeNames -notcontains $_.Name
   } | ForEach-Object {
      Copy-Item -LiteralPath $_.FullName -Destination ( Join-Path $DestinationPath $_.Name ) -Recurse -Force
   }
}

$repoRootPath = ( Resolve-Path -LiteralPath $RepoRoot ).Path
$propsPath = Join-Path $repoRootPath "Directory.Build.props"

if( -not ( Test-Path -LiteralPath $propsPath ) ) {
   throw "Directory.Build.props was not found at '$propsPath'."
}

if( [string]::IsNullOrWhiteSpace( $Version ) ) {
   $Version = Get-ProjectPropertyValue -ProjectFilePath $propsPath -PropertyName "Version"
}

if( [string]::IsNullOrWhiteSpace( $GameRootPath ) ) {
   $GameRootPath = Get-ProjectPropertyValue -ProjectFilePath $propsPath -PropertyName "GameRootPath"
}

$gameRootResolvedPath = ( Resolve-Path -LiteralPath $GameRootPath ).Path

if( [string]::IsNullOrWhiteSpace( $Tag ) ) {
   $Tag = "v$Version"
}

if( [string]::IsNullOrWhiteSpace( $OutputRoot ) ) {
   $OutputRoot = Join-Path $repoRootPath ( Join-Path "artifacts/release" $Tag )
}

$outputRootPath = [System.IO.Path]::GetFullPath( $OutputRoot )
$stageRootPath = Join-Path $outputRootPath "package"
$zipFileName = "OstranautsTranslator-$Tag.zip"
$zipPath = Join-Path $outputRootPath $zipFileName
$releaseNotesPath = Join-Path $outputRootPath "release-notes.md"

if( -not $SkipBuild ) {
   $vsDevShellPath = "D:/Program Files/Microsoft Visual Studio/18/BuildTools/Common7/Tools/Launch-VsDevShell.ps1"
   if( -not ( Test-Path -LiteralPath $vsDevShellPath ) ) {
      throw "Visual Studio developer shell script was not found at '$vsDevShellPath'."
   }

   & $vsDevShellPath -Arch amd64 -HostArch amd64

   Push-Location $repoRootPath
   try {
      msbuild .\OstranautsTranslator.sln /p:Configuration=Release /nologo /verbosity:quiet /clp:Summary
   }
   finally {
      Pop-Location
   }
}

$pluginSourcePath = Join-Path $gameRootResolvedPath "BepInEx/plugins/OstranautsTranslator"
$toolSourcePath = Join-Path $gameRootResolvedPath "OstranautsTranslator"
$modsRootPath = Join-Path $gameRootResolvedPath "Ostranauts_Data/Mods"
$loadingOrderSourcePath = Join-Path $modsRootPath "loading_order.json"
$modSourcePath = Join-Path $modsRootPath "OstranautsTranslate"
$modInfoPath = Join-Path $modSourcePath "mod_info.json"

foreach( $requiredPath in @( $pluginSourcePath, $toolSourcePath, $loadingOrderSourcePath, $modSourcePath, $modInfoPath ) ) {
   if( -not ( Test-Path -LiteralPath $requiredPath ) ) {
      throw "Required release input was not found: '$requiredPath'."
   }
}

if( Test-Path -LiteralPath $outputRootPath ) {
   Remove-Item -LiteralPath $outputRootPath -Recurse -Force
}

New-Item -ItemType Directory -Path $outputRootPath -Force | Out-Null

Copy-DirectoryContents -SourcePath $pluginSourcePath -DestinationPath ( Join-Path $stageRootPath "BepInEx/plugins/OstranautsTranslator" )
Copy-DirectoryContents -SourcePath $toolSourcePath -DestinationPath ( Join-Path $stageRootPath "OstranautsTranslator" ) -ExcludeNames @( "config.ini", "workspace", "workspace-validation", "XUnity.AutoTranslator.Workspace" )
New-Item -ItemType Directory -Path ( Join-Path $stageRootPath "Ostranauts_Data/Mods" ) -Force | Out-Null
Copy-Item -LiteralPath $loadingOrderSourcePath -Destination ( Join-Path $stageRootPath "Ostranauts_Data/Mods/loading_order.json" ) -Force
Copy-Item -LiteralPath $modSourcePath -Destination ( Join-Path $stageRootPath "Ostranauts_Data/Mods/OstranautsTranslate" ) -Recurse -Force

$modInfo = Get-Content -LiteralPath $modInfoPath -Raw | ConvertFrom-Json
$gameVersion = if( $modInfo.Count -gt 0 -and -not [string]::IsNullOrWhiteSpace( $modInfo[0].strGameVersion ) ) {
   $modInfo[0].strGameVersion
}
else {
   "Unknown"
}

@"
# OstranautsTranslator $Tag

Compatible game version: $gameVersion

## Installation

1. Close the game.
2. Download $zipFileName.
3. Extract the archive directly into the game root, the folder that contains Ostranauts.exe.
4. Overwrite existing files when prompted.
5. Launch the game. Press F6 to open the status window.

## Included content

- BepInEx/plugins/OstranautsTranslator
- OstranautsTranslator
- Ostranauts_Data/Mods/loading_order.json
- Ostranauts_Data/Mods/OstranautsTranslate

## Notes

- This package targets Simplified Chinese.
- config.ini and workspace data are intentionally excluded from the release package.
- If the game updates, rerun OstranautsTranslator.exe or install a newer release package.
"@ | Set-Content -LiteralPath $releaseNotesPath -Encoding UTF8

Compress-Archive -Path ( Join-Path $stageRootPath "*" ) -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host "Release package created: $zipPath"
Write-Host "Release notes created: $releaseNotesPath"
