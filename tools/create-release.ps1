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

function Copy-SelectedFiles {
   param(
      [Parameter( Mandatory = $true )]
      [string]$SourcePath,
      [Parameter( Mandatory = $true )]
      [string]$DestinationPath,
      [Parameter( Mandatory = $true )]
      [string[]]$FileNames
   )

   New-Item -ItemType Directory -Path $DestinationPath -Force | Out-Null
   foreach( $fileName in $FileNames ) {
      $sourceFilePath = Join-Path $SourcePath $fileName
      if( -not ( Test-Path -LiteralPath $sourceFilePath ) ) {
         throw "Required release file was not found: '$sourceFilePath'."
      }

      Copy-Item -LiteralPath $sourceFilePath -Destination ( Join-Path $DestinationPath $fileName ) -Force
   }
}

function Copy-FilesByPattern {
   param(
      [Parameter( Mandatory = $true )]
      [string]$SourcePath,
      [Parameter( Mandatory = $true )]
      [string]$DestinationPath,
      [Parameter( Mandatory = $true )]
      [string]$Filter
   )

   if( -not ( Test-Path -LiteralPath $SourcePath ) ) {
      throw "Required release directory was not found: '$SourcePath'."
   }

   New-Item -ItemType Directory -Path $DestinationPath -Force | Out-Null
   Get-ChildItem -LiteralPath $SourcePath -File -Filter $Filter | ForEach-Object {
      Copy-Item -LiteralPath $_.FullName -Destination ( Join-Path $DestinationPath $_.Name ) -Force
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
$bepInExCoreSourcePath = Join-Path $bepInExSourcePath "core"
$bepInExConfigSourcePath = Join-Path $bepInExSourcePath "config"
$pluginSourcePath = Join-Path $bepInExSourcePath "plugins/OstranautsTranslator"
$toolSourcePath = Join-Path $gameRootResolvedPath "OstranautsTranslator"
$workspaceSourcePath = Join-Path $toolSourcePath "workspace"
$workspaceDatabaseSourcePath = Join-Path $workspaceSourcePath "corpus.sqlite"
$workspaceReferenceSourcePath = Join-Path $workspaceSourcePath "reference"
$toolConfigExampleSourcePath = Join-Path $toolSourcePath "config-example.ini"
$toolGlossaryGeneratorSourcePath = Join-Path $toolSourcePath "generate_generic_glossary.py"
$modsRootPath = Join-Path $gameRootResolvedPath "Ostranauts_Data/Mods"
$loadingOrderSourcePath = Join-Path $modsRootPath "loading_order.json"
$modSourcePath = Join-Path $modsRootPath "OstranautsTranslate"
$modInfoPath = Join-Path $modSourcePath "mod_info.json"
$doorstopConfigSourcePath = Join-Path $gameRootResolvedPath "doorstop_config.ini"
$winHttpSourcePath = Join-Path $gameRootResolvedPath "winhttp.dll"

foreach( $requiredPath in @( $bepInExCoreSourcePath, $bepInExConfigSourcePath, $pluginSourcePath, $toolSourcePath, $workspaceDatabaseSourcePath, $workspaceReferenceSourcePath, $toolConfigExampleSourcePath, $toolGlossaryGeneratorSourcePath, $loadingOrderSourcePath, $modSourcePath, $modInfoPath, $doorstopConfigSourcePath, $winHttpSourcePath ) ) {
   if( -not ( Test-Path -LiteralPath $requiredPath ) ) {
      throw "Required release input was not found: '$requiredPath'."
   }
}

if( Test-Path -LiteralPath $outputRootPath ) {
   Remove-Item -LiteralPath $outputRootPath -Recurse -Force
}

New-Item -ItemType Directory -Path $outputRootPath -Force | Out-Null

Copy-SelectedFiles -SourcePath $bepInExCoreSourcePath -DestinationPath ( Join-Path $stageRootPath "BepInEx/core" ) -FileNames @(
   "0Harmony.dll",
   "0Harmony20.dll",
   "BepInEx.dll",
   "BepInEx.Harmony.dll",
   "BepInEx.Preloader.dll",
   "HarmonyXInterop.dll",
   "Mono.Cecil.dll",
   "Mono.Cecil.Mdb.dll",
   "Mono.Cecil.Pdb.dll",
   "Mono.Cecil.Rocks.dll",
   "MonoMod.RuntimeDetour.dll",
   "MonoMod.Utils.dll",
   "XUnity.Common.dll"
)
Copy-SelectedFiles -SourcePath $bepInExConfigSourcePath -DestinationPath ( Join-Path $stageRootPath "BepInEx/config" ) -FileNames @(
   "BepInEx.cfg",
   "OstranautsTranslator.cfg"
)
Copy-DirectoryContents -SourcePath $pluginSourcePath -DestinationPath ( Join-Path $stageRootPath "BepInEx/plugins/OstranautsTranslator" )
Copy-SelectedFiles -SourcePath $toolSourcePath -DestinationPath ( Join-Path $stageRootPath "OstranautsTranslator" ) -FileNames @(
   "config-example.ini",
   "generate_generic_glossary.py",
   "Microsoft.Data.Sqlite.dll",
   "Mono.Cecil.dll",
   "Mono.Cecil.Mdb.dll",
   "Mono.Cecil.Pdb.dll",
   "Mono.Cecil.Rocks.dll",
   "OstranautsTranslator.Core.dll",
   "OstranautsTranslator.deps.json",
   "OstranautsTranslator.dll",
   "OstranautsTranslator.exe",
   "OstranautsTranslator.runtimeconfig.json",
   "runtime-fixed-source.json",
   "SQLitePCLRaw.core.dll",
   "SQLitePCLRaw.provider.winsqlite3.dll"
)
New-Item -ItemType Directory -Path ( Join-Path $stageRootPath "OstranautsTranslator/workspace" ) -Force | Out-Null
Copy-Item -LiteralPath $workspaceDatabaseSourcePath -Destination ( Join-Path $stageRootPath "OstranautsTranslator/workspace/corpus.sqlite" ) -Force
Copy-FilesByPattern -SourcePath $workspaceReferenceSourcePath -DestinationPath ( Join-Path $stageRootPath "OstranautsTranslator/workspace/reference" ) -Filter "*.json"
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
- It includes only the required runtime files, config files, database file, translation mod, and DLLs.
- It also includes config-example.ini, the bundled glossary generator script, and the workspace/reference glossary JSON files used by the translation workflow.
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
- 包里只保留必需的运行时文件、配置文件、数据库文件、翻译 mod 和 DLL。
- 包内也包含 config-example.ini、术语表生成脚本，以及翻译流程会用到的 workspace/reference 术语 JSON 文件。
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
- It includes only the required runtime files, config files, database file, translation mod, and DLLs.
- It also includes config-example.ini, the bundled glossary generator script, and the workspace/reference glossary JSON files used by the translation workflow.
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
- 包里只保留必需的运行时文件、配置文件、数据库文件、翻译 mod 和 DLL。
- 包内也包含 config-example.ini、术语表生成脚本，以及翻译流程会用到的 workspace/reference 术语 JSON 文件。
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
