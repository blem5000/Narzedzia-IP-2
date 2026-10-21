# Pakowanie Narzedzia IP 2 do GitHub Release (Windows PowerShell).
# Uruchom z katalogu repo:  powershell -ExecutionPolicy Bypass -File build_release.ps1
#
# Buduje aplikacje MSBuild (Release), kopiuje exe obok siebie do dist\NarzedziaIP2
# i zipuje do NarzedziaIP2-windows.zip - ten zip wgrywasz jako asset wydania.
#
# Ten skrypt jest JEDNYM ZRODLEM PRAWDY dla zawartosci paczki - CI
# (.github/workflows/release.yml) tez go uzywa, wiec lokalnie i na CI paczka
# jest identyczna. Wczesniej logika kopiowania byla zduplikowana i rozjechała
# sie (CI wkładało instrukcje jako .md, skrypt lokalny takze, a walidacja
# starego updatera takiego rozszerzenia nie dopuszczala).
#
# Parametry:
#   -SkipBuild   nie buduj (CI buduje juz w osobnym kroku)
#   -OutDir      gdzie zapisac paczke (domyslnie <repo>\dist)
#
# Wazne: numer wersji (AssemblyVersion) musi zgadzac sie z tagiem wydania,
# np. AssemblyVersion 1.1.0.0 -> tag v1.1.0. Aplikacja porownuje tag z API
# https://api.github.com/repos/blem5000/Narzedzia-IP-2/releases/latest
# z wlasna wersja i proponuje aktualizacje tylko gdy tag jest nowszy.

[CmdletBinding()]
param(
    [switch] $SkipBuild,
    [string] $OutDir
)

$ErrorActionPreference = "Stop"

$Root = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Root)) { $Root = (Get-Location).Path }

if ([string]::IsNullOrWhiteSpace($OutDir)) { $OutDir = Join-Path $Root "dist" }

# ---- build (opcjonalny - CI buduje juz w osobnym kroku) ----
if (-not $SkipBuild) {
    $Msbuild = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
    if (-not (Test-Path $Msbuild)) {
        $Msbuild = "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
    }
    if (-not (Test-Path $Msbuild)) {
        # CI (GitHub runner) ma msbuild w PATH
        $cmd = Get-Command msbuild -ErrorAction SilentlyContinue
        if ($cmd) {
            $Msbuild = $cmd.Source
        } else {
            throw "Nie znaleziono MSBuild (VS 18 Community, BuildTools 2022 ani msbuild w PATH)."
        }
    }

    & $Msbuild (Join-Path $Root "Narzedzia IP 2.sln") /p:Configuration=Release /v:minimal /nologo
    if ($LASTEXITCODE -ne 0) { throw "MSBuild failed ($LASTEXITCODE)." }
}

# ---- zawartosc paczki ----
$Dist = Join-Path $OutDir "NarzedziaIP2"
if (Test-Path $Dist) { Remove-Item $Dist -Recurse -Force }
New-Item -ItemType Directory -Path $Dist -Force | Out-Null

$MainBin = Join-Path $Root "Narzedzia IP 2\bin\Release"
$UpdBin = Join-Path $Root "NarzedziaIP.Updater\bin\Release"

# Pliki w paczce MUSZA przechodzic walidacje najstarszego updatera
# (dopuszczone: .dll .config .pdb .xml .txt + dwa .exe o znanych nazwach).
# Test: NarzedziaIP.Tests/UpdaterTests.cs -> PackagePassesValidationOfOlderUpdaters
Copy-Item (Join-Path $MainBin "Narzedzia IP 2.exe") $Dist -Force
Copy-Item (Join-Path $MainBin "Narzedzia IP 2.exe.config") $Dist -Force
Copy-Item (Join-Path $UpdBin "NarzedziaIP.Updater.exe") $Dist -Force
# instrukcja jako .txt (nie .md) - stary updater odrzucilby .md
Copy-Item (Join-Path $Root "Narzedzia IP 2\Instrukcja.md") (Join-Path $Dist "Instrukcja.txt") -Force

$Zip = Join-Path $OutDir "NarzedziaIP2-windows.zip"
if (Test-Path $Zip) { Remove-Item $Zip -Force }
Compress-Archive -Path $Dist -DestinationPath $Zip

Write-Host ""
Write-Host "Gotowe."
Write-Host "Folder: $Dist"
Write-Host "Asset do wydania: $Zip"
Write-Host ""
Write-Host "Kolejne kroki:"
Write-Host "1. Utworz tag vX.Y.Z zgodny z AssemblyVersion (np. v1.1.0)."
Write-Host "2. Utworz GitHub Release z tego tagu i wgraj NarzedziaIP2-windows.zip."
