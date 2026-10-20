# Narzedzia IP 2

Narzędzie dla helpdesku: po wpisaniu hostname'u znajduje jego adres IP i MAC
w DHCP, pokazuje statystyki pinga, uruchamia zdalny pulpit (MSRA / RDP) oraz
otwiera pulpit komputera w TacticalRMM. Aplikacja aktualizuje się sama z
GitHub Releases.

Aplikacja desktopowa **WPF / .NET Framework 4.8** — uruchamia się tylko
na Windows.

---

## Funkcje

- **Wyszukiwanie w DHCP** — hostname → IP/MAC przez `Get-DhcpServerv4Lease`.
  Zakresy odpytywane są paczkami (4 procesy PowerShell równolegle), wynik
  jest cache'owany na 10 minut.
- **Weryfikacja aktywności** — po odnaleziu lease'a robiony jest ping;
  nieaktywne adresy trafiają na listę „nieaktywne".
- **Ostrzeżenie DNS vs DHCP** — wykrywa rozjazd adresu w DNS i w DHCP.
- **Ping wielourządzeniowy** — tabela z kolorowanymi wierszami, min/średnia/max,
  TTL, procent powodzeń; dźwięk przy zmianie stanu.
- **MSRA / RDP** — uruchamia zdalny pulpit na znalezionym adresie.
- **TacticalRMM** — rozwiązuje hostname do `agent_id` i otwiera deep-link
  do pulpitu Mesh (`{dashboard}/takecontrol/{agent_id}`). Pokazuje status
  offline agenta.
- **Historia** — zapisywana na dysku w profilu użytkownika (CSV, Excel PL),
  z rotacją i eksportem.
- **Auto-aktualizacja** — sprawdzanie przy starcie, instalator w osobnym
  procesie (bez okien konsoli), weryfikacja SHA-256 pobranej paczki.
- **Generowanie ACL Cisco** — zakładka ukryta (`Visibility="Collapsed"`),
  kod jest utrzymywany.

---

## Wymagania

| Element | Wymaganie |
|---|---|
| System | Windows 10 / 11 / Server 2016+ (x64 lub x86) |
| Runtime | .NET Framework 4.8 (wbudowany w Windows 10 1903+) |
| Uprawnienia | konto z uprawnieniami do odpytywania DHCP na serwerze |
| PowerShell | wbudowany w Windows; moduł `DhcpServer` na **serwerze** DHCP |

Aplikacja uruchamia `powershell.exe` z `-EncodedCommand` (Base64 UTF-16LE),
więc ścieżki z polskimi znakami i spacjami nie psują argumentów.

---

## Struktura repozytorium

```
Narzedzia IP 2.sln
Narzedzia IP 2/            aplikacja główna (WPF)
  Instrukcja.xaml          treść instrukcji obsługi (FlowDocument) - osobny plik
NarzedziaIP.Updater/       osobny instalator aktualizacji (okno z paskiem postępu)
NarzedziaIP.Tests/         testy MSTest (v2)
.github/workflows/         build-test.yml (push/PR), release.yml (tag v*)
build_release.ps1          lokalne pakowanie do dist/ (CI robi to samo)
TacticalRMM-config-TEMPLATE.reg   szablon konfiguracji firmowej (HKCU)
```

### Gdzie co jest

| Plik | Odpowiada za |
|---|---|
| `App.xaml.cs` | start, migracja ustawień, sprzątanie `*.old`, cicha aktualizacja |
| `App.xaml` | merge `Instrukcja.xaml` do zasobów aplikacji |
| `Instrukcja.xaml` | **instrukcja obsługi** — `FlowDocument` w osobnym pliku (klucz `InstrukcjaDoc`) |
| `App.Update.cs` | decyzja „aktualizować czy nie" na starcie (`BootUpdate.EvaluateBootAction`) |
| `MainWindow.xaml` / `.xaml.cs` | całe GUI i obsługa zdarzeń |
| `MainWindow.Dhcp.cs` | test łączności z serwerem DHCP w tle |
| `MainWindow.Update.cs` | przycisk „sprawdź aktualizacje", okno aktualizacji |
| `DhcpClient.cs` | budowa komend PowerShell, parsowanie odpowiedzi, cache zakresów |
| `PingHelper.cs` / `PingMonitor.cs` | ping i statystyki (`PingStats`, `PingRow`) |
| `TacticalRmm.cs` | REST API TacticalRMM, parsowanie, cache agentów |
| `CompanyConfig.cs` | konfiguracja z rejestru `HKCU\Software\NarzedziaIP2` + DPAPI |
| `Updater.cs` | API GitHub, pobieranie, walidacja i rozpakowywanie paczki |
| `HistoryLog.cs` | historia CSV w `%LOCALAPPDATA%\NarzedziaIP2` |
| `AclInput/AclWildcard/AclRemark.cs` | agregacja wildcardów i remarki ACL |

---

## Budowanie i testy

```powershell
# Release
msbuild "Narzedzia IP 2.sln" -p:Configuration=Release -v:minimal -nologo

# Testy (MSTest)
vstest.console.exe "NarzedziaIP.Tests\bin\Release\NarzedziaIP.Tests.dll"
```

Albo lokalnie do paczki (to samo, co robi CI):

```powershell
powershell -ExecutionPolicy Bypass -File build_release.ps1
```

 Wynik: `dist\NarzedziaIP2-windows.zip`.

---

## Wydanie

1. Podbij wersję w **dwóch** plikach:
   - `Narzedzia IP 2/Properties/AssemblyInfo.cs`
   - `NarzedziaIP.Updater/Properties/AssemblyInfo.cs`

   `AssemblyVersion` **musi** odpowiadać tagowi — `release.yml` sprawdza to
   i przerywa build, jeśli się nie zgadza.

2. Zataguj i wypchnij:

```powershell
git tag -a v1.1.26 -m "v1.1.26 - opis zmian"
git push origin main
git push origin v1.1.26
```

3. `release.yml` zbuduje, odpali testy, spakuje i opublikuje release
   z assetem `NarzedziaIP2-windows.zip` (plus `zip.sha256`).
   Notatki release'a zawierają linię `SHA256: <hash>` — aplikacja porównuje
   ją z hashem pobranego pliku i przerywa instalację przy niezgodności.

---

## Wdrożenie

Program można uruchamiać z katalogu na **udziale sieciowym** — aktualizacja
obsługuje ten scenariusz (rename-swap przy zablokowanym pliku, wyczyszczenie
`*.old` przy starcie).

### Konfiguracja firmowa (rejestr, opcjonalnie)

Plik `TacticalRMM-config-TEMPLATE.reg` pokazuje format. Dwuklik importuje
wpisy do `HKCU\Software\NarzedziaIP2`:

| Wartość | Znaczenie |
|---|---|
| `TacticalApiUrl` | adres API, np. `https://api.twoja-domena.local` |
| `TacticalApiKey` | klucz API (w rejestrze **jawnym tekstem**) |
| `TacticalDashboardUrl` | adres dashboardu |
| `TacticalAllowInsecureHttp` | `1` = zezwól na `http://` do API |

**Rejestr ma pierwszeństwo** przed ustawieniami użytkownika — to polityka
centralna. Wartości z rejestru wygrywają też wtedy, gdy użytkownik zmieni
coś w GUI.

> **Bezpieczeństwo:** klucz API w rejestrze jest jawnym tekstem. Rejestr
> `HKCU` jest czytelny przez każdy proces działający w koncie użytkownika.
> Klucz w ustawieniach użytkownika jest szyfrowany DPAPI (tylko to konto
> Windows go odczyta) — ale DPAPI nie przenosi się między maszynami, dlatego
> konfiguracja centralna zostaje jawna.

### Adres API po HTTP

Domyślnie aplikacja **odmawia** połączenia z API Tactical po `http://`
(klucz API leciałby wtedy otwartym tekstem). Dopuszczamy `https://` oraz
adresy loopback. Instalacje z API pod `http://` muszą włączyć opcję
„Zezwól na nieszyfrowane HTTP do API Tactical" w zakładce Ustawienia
(wyłącza ją wartość `TacticalAllowInsecureHttp=1` w rejestrze).

---

## Bezpieczeństwo — co jest zrobione

- **Zip Slip** — nazwy wpisów w pobranej paczce są walidowane; plik nie może
  wypisać się poza katalog rozpakowywania (`Updater.ResolveSafeTarget`).
- **Weryfikacja SHA-256** — pobrana paczka jest porównywana z hashem
  z notatek release'a (`Updater.VerifyAssetSha256`).
- **Walidacja zawartości paczki** — dozwolone tylko `.exe` o znanych nazwach
  oraz `.dll/.config/.pdb/.xml/.txt` (`Updater.ValidatePackageFiles`).
- **Blokada HTTP** — patrz wyżej, z jawną opt-out.
- **DPAPI** — klucz API w ustawieniach użytkownika.
- **Bez konsoli** — aktualizacja uruchamia osobny proces okienkowy, więc
  nigdzie nie wyskakuje okno `cmd`.

### Czego **nie** ma

Pobrana paczka **nie jest podpisana kryptograficznie** (brak Authenticode
i brak podpisu GPG). SHA-256 z tego samego release'a chroni przed
uszkodzoną paczką i pomyłką w mirrorze, ale **nie** przed przejęciem konta
na GitHubie — kto taki dostanie, może podmienić i paczkę, i hash. Pełne
zabezpieczenie wymaga podpisu (Authenticode albo GPG).

---

## Konwencje

- Nazwy plików i typów po angielsku, komentarze i teksty UI po polsku.
- Kod domenowy jest wydzielony do klas statycznych z czystymi metodami
  (testowalnymi bez sieci i bez serwera DHCP) — patrz `DhcpClient`,
  `PingHelper`, `IpSelection`, `RemoteConnect`, `Acl*`.
- Testy w `NarzedziaIP.Tests`, nazwy metod opisują zachowanie
  (`Given_When_Then` w formie opisowej, np. `ExtractZipRejectsPathTraversalEntry`).
- Numeracja sekwencji (`NextSeq`) i agregacja wildcardów działają na
  listach posortowanych `IPToUint`.
