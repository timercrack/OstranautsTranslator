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
$releaseNotesEnglishPath = Join-Path $outputRootPath "release-notes.en.md"
$releaseNotesChinesePath = Join-Path $outputRootPath "release-notes.zh-CN.md"
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

$bepInExSourcePath = Join-Path $gameRootResolvedPath "BepInEx"
$toolSourcePath = Join-Path $gameRootResolvedPath "OstranautsTranslator"
$modsRootPath = Join-Path $gameRootResolvedPath "Ostranauts_Data/Mods"
$loadingOrderSourcePath = Join-Path $modsRootPath "loading_order.json"
$modSourcePath = Join-Path $modsRootPath "OstranautsTranslate"
$modInfoPath = Join-Path $modSourcePath "mod_info.json"
$doorstopConfigSourcePath = Join-Path $gameRootResolvedPath "doorstop_config.ini"
$winHttpSourcePath = Join-Path $gameRootResolvedPath "winhttp.dll"

foreach( $requiredPath in @( $bepInExSourcePath, $toolSourcePath, $loadingOrderSourcePath, $modSourcePath, $modInfoPath, $doorstopConfigSourcePath, $winHttpSourcePath ) ) {
   if( -not ( Test-Path -LiteralPath $requiredPath ) ) {
      throw "Required release input was not found: '$requiredPath'."
   }
}

if( Test-Path -LiteralPath $outputRootPath ) {
   Remove-Item -LiteralPath $outputRootPath -Recurse -Force
}

New-Item -ItemType Directory -Path $outputRootPath -Force | Out-Null

Copy-DirectoryContents -SourcePath $bepInExSourcePath -DestinationPath ( Join-Path $stageRootPath "BepInEx" ) -ExcludeNames @( "cache", "LogOutput.log" )
Copy-DirectoryContents -SourcePath $toolSourcePath -DestinationPath ( Join-Path $stageRootPath "OstranautsTranslator" ) -ExcludeNames @( "config.ini" )
Copy-Item -LiteralPath $doorstopConfigSourcePath -Destination ( Join-Path $stageRootPath "doorstop_config.ini" ) -Force
Copy-Item -LiteralPath $winHttpSourcePath -Destination ( Join-Path $stageRootPath "winhttp.dll" ) -Force
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

Simplified Chinese package for Ostranauts.

Compatible game version: $gameVersion

## Install

1. Close the game.
2. Download $zipFileName.
3. Extract the archive directly into the game root, the folder that contains Ostranauts.exe.
4. Overwrite existing files when prompted.
5. Launch the game. Press F6 to open the status window.

## Notes

- This release is meant to be extracted directly into the game folder.
- It already includes the required runtime files, translation plugin, Chinese mod, and translation data.
- If the game updates and the translation looks outdated, run OstranautsTranslator.exe once, then launch the game again.
- config.ini is not included in the public release package.

## Download

- Asset: $zipFileName
"@ | Set-Content -LiteralPath $releaseNotesEnglishPath -Encoding UTF8

@"
# OstranautsTranslator $Tag

面向 Ostranauts 玩家使用的简体中文翻译包。

兼容游戏版本：$gameVersion

## 安装方法

1. 关闭游戏。
2. 下载 $zipFileName。
3. 将压缩包直接解压到游戏根目录，也就是包含 Ostranauts.exe 的文件夹。
4. 如果提示覆盖现有文件，选择覆盖。
5. 启动游戏。按 F6 可以打开状态窗口。

## 说明

- 这个 release 设计为直接解压到游戏目录使用。
- 包里已经包含所需运行时文件、翻译插件、中文 mod 和翻译数据。
- 如果游戏更新后翻译显得过时，请先运行一次 OstranautsTranslator.exe，再启动游戏。
- 公开 release 不包含 config.ini。

## 下载文件

- 文件名：$zipFileName
"@ | Set-Content -LiteralPath $releaseNotesChinesePath -Encoding UTF8

@"
Language / 语言: [English](#english) | [简体中文](#zh-cn)

<a id="english"></a>

# OstranautsTranslator $Tag

Simplified Chinese package for Ostranauts.

Compatible game version: $gameVersion

## Install

1. Close the game.
2. Download $zipFileName.
3. Extract the archive directly into the game root, the folder that contains Ostranauts.exe.
4. Overwrite existing files when prompted.
5. Launch the game. Press F6 to open the status window.

## Notes

- This release is meant to be extracted directly into the game folder.
- It already includes the required runtime files, translation plugin, Chinese mod, and translation data.
- If the game updates and the translation looks outdated, run OstranautsTranslator.exe once, then launch the game again.
- config.ini is not included in the public release package.

## Download

- Asset: $zipFileName

---

<a id="zh-cn"></a>

# OstranautsTranslator $Tag

面向 Ostranauts 玩家使用的简体中文翻译包。

兼容游戏版本：$gameVersion

## 安装方法

1. 关闭游戏。
2. 下载 $zipFileName。
3. 将压缩包直接解压到游戏根目录，也就是包含 Ostranauts.exe 的文件夹。
4. 如果提示覆盖现有文件，选择覆盖。
5. 启动游戏。按 F6 可以打开状态窗口。

## 说明

- 这个 release 设计为直接解压到游戏目录使用。
- 包里已经包含所需运行时文件、翻译插件、中文 mod 和翻译数据。
- 如果游戏更新后翻译显得过时，请先运行一次 OstranautsTranslator.exe，再启动游戏。
- 公开 release 不包含 config.ini。

## 下载文件

- 文件名：$zipFileName
"@ | Set-Content -LiteralPath $releaseNotesPath -Encoding UTF8

Compress-Archive -Path ( Join-Path $stageRootPath "*" ) -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host "Release package created: $zipPath"
Write-Host "English release notes created: $releaseNotesEnglishPath"
Write-Host "Simplified Chinese release notes created: $releaseNotesChinesePath"
Write-Host "Release notes created: $releaseNotesPath"
