# Narzędzia IP - instrukcja obsługi

Program służy do wyszukiwania adresów IP komputerów w DHCP, porównywania wyniku z DNS, kopiowania adresów IP/MAC, zdalnego łączenia się z komputerami (MSRA, RDP, TacticalRMM), generowania list ACL dla Cisco oraz wykonywania cyklicznego pingowania.

## 1. Uruchomienie programu i serwer DHCP

Program startuje od razu - sprawdzanie serwera DHCP odbywa się w tle. W trakcie sprawdzania pasek tytułu pokazuje **- trwa sprawdzanie dhcp** . Gdy serwer odpowiada, napis znika. Gdy serwer jest niedostępny, tytuł pokazuje **- DHCP niedostępny** .

Domyślny serwer DHCP to **150.150.222.20** . Inny adres wpiszesz w zakładce **Ustawienia** i zweryfikujesz przyciskiem **Sprawdź** .

## 2. Zakładka: Hostname do DHCP

**Komunikaty w pasku statusu:** nad przyciskami program pokazuje, co w tej chwili robi. **Szukam w DHCP...** - trwa wyszukiwanie adresu, **Porównuję wynik z DNS...** - sprawdzanie, czy DNS zwraca ten sam adres, **Sprawdzam w TacticalRMM...** - szukanie agenta, **Łączę z komputerem...** - start połączenia, a na końcu np. **RDP uruchomione (10.0.0.5).** Dzięki temu widać, w której fazie jest program - komunikat zmienia się zamiast stać w miejscu.

**Pole Hostname:** W tym polu można wpisać nazwę komputera albo bezpośrednio adres IP.

**Przycisk Wyszukaj i połącz:**

- Jeśli wpisano **hostname** , program wyszukuje komputer w DHCP, pobiera aktywny adres IP i MAC, a następnie uruchamia Pomoc zdalną MSRA.
- Jeśli wpisano **adres IP** , program nie odpytuje DHCP i od razu łączy się wybranym sposobem (MSRA albo RDP) dla tego adresu.
- Naciśnięcie klawisza **Enter** w polu Hostname działa tak samo jak przycisk **Wyszukaj i połącz** .

**Przycisk Szukaj:**

- Jeśli wpisano **hostname** , program wyszukuje adres IP i MAC w DHCP.
- Jeśli wpisano **adres IP** , program nie wykonuje wyszukiwania w DHCP, tylko przyjmuje wpisany adres jako aktualny wynik.
- Podczas wyszukiwania wyświetlany jest pasek postępu oraz komunikat **Szukam w DHCP...** .

**Wynik DHCP:** W tym miejscu wyświetlany jest znaleziony adres IP oraz adres MAC. Poniżej jest lista wszystkich aktywnych wyników do wyboru - kopiowanie i połączenie (w wybranym sposobie) używają wybranego adresu (przy jednym wyniku wybierany jest automatycznie).

**Porównanie DNS z DHCP:** Po znalezieniu aktywnego adresu IP w DHCP program sprawdza również adres IP zwracany przez DNS. Jeśli adres IP w DNS jest inny niż adres IP znaleziony w DHCP, pod wynikiem pojawi się ostrzeżenie: *IP w DNS jest inny niż w DHCP* , razem z adresem IP z DNS.

**Przycisk Kopiuj IP:** Kopiuje znaleziony albo wpisany adres IP do schowka.

**Przycisk Kopiuj MAC:** Kopiuje znaleziony adres MAC do schowka. Format wybierzesz w zakładce **Ustawienia**: zwykły, z dwukropkami albo ciscowy.

**Sposób połączenia (MSRA / RDP / TacticalRMM):** przełączniki wybierają, czym program łączy się z komputerem. MSRA uruchamia Pomoc zdalną Microsoft Remote Assistance, RDP uruchamia Podłączanie pulpitu zdalnego (mstsc), TacticalRMM otwiera pulpit komputera w dashboardzie TacticalRMM (Mesh). Wybór zapamiętywany jest w ustawieniach, więc po zamknięciu i ponownym uruchomieniu programu pozostaje taki sam.

**Przycisk Połącz:** łączy z aktualnie wyświetlonym adresem IP sposobem wybranym powyżej. Gdy na komputerze nie ma pliku msra.exe, program zaproponuje połączenie przez RDP.

**Przycisk Pokaż w RMM:** otwiera stronę komputera w dashboardzie TacticalRMM (stan, historia, zadania) - bez przechodzenia do pulpitu. Do pulpitu (Mesh) program przechodzi przy wybranym sposobie połączenia *TacticalRMM*.

- Jeśli znaleziono więcej niż jeden aktywny adres IP, wybierz właściwy z listy wyników - połączenie i kopiowanie zadziałają dla wybranego adresu. Bez wyboru program poprosi o wskazanie adresu zamiast zgadywać.
- Jeśli aktualna wartość IP nie jest poprawnym adresem IPv4, program wyświetli ostrzeżenie.

**Przycisk Wyczyść:** Czyści pole Hostname, wynik IP/MAC oraz komunikat ostrzegawczy DNS/DHCP.

**Historia:** Pokazuje wykonane wyszukiwania wraz z datą i godziną. Historia może również zawierać informacje o różnicy między DNS i DHCP. Każde wyszukiwanie i połączenie zapisywane jest też na dysk (profil użytkownika, plik historia.csv) - przyciskiem **Eksportuj historię** pod listą zapiszesz go w wybranym miejscu.

## 3. Zakładka: Po adresie IP

Wyszukiwanie wsteczne: z adresu IP dowiesz się, jaki to komputer. Zgłoszenie brzmi „komputer na 10.202.130.55 nie wstał” - a nazwy hosta nie znasz.

Wpisz adres IPv4 i kliknij **Szukaj** (działa też **Enter**). Program sprawdza w DHCP:

- **Dzierżawa** - komputer aktualnie ma ten adres (jest włączony i zarejestrowany w DHCP). Widać też adres MAC.
- **Rezerwacja** - adres jest przypisany do komputera na stałe, a host może być wyłączony. Opis rezerwacji zwykle mówi, kto odbiera sprzęt.

Wyniki to tabela: adres IP, nazwa hosta, MAC, rodzaj (dzierżawa / rezerwacja) i opis. Wiersz z rezerwacją bywa cenniejszy niż dzierżawa - to jedyna informacja o właścicielu, gdy masz tylko adres.

## 4. Zakładka: Ping

**Pole Hostname:** Można wpisać jedną lub wiele pozycji naraz (przecinki albo nowe linie): nazwę komputera albo bezpośrednio adres IP. Enter to nowa linia, start to **Ctrl+Enter** albo przycisk Start.

**Tabela wyników:** każdy wiersz to jedno urządzenie. Wiersz pingujący jest zielony, niepingujący czerwony. Kolumny: wysłane, success i procent, ostatni wynik i jego TTL, średni TTL, czas ostatniego sukcesu i błędu oraz min/śr/max czasu.

**Przycisk Start:**

- Jeśli wpisano **hostname** , program najpierw wyszukuje aktywny adres IP w DHCP, a następnie rozpoczyna cykliczne pingowanie. Każdą pozycję z listy rozwiązuje osobno.
- Jeśli wpisano **adres IP** , program pomija wyszukiwanie w DHCP i od razu rozpoczyna pingowanie tego adresu.
- Podczas wyszukiwania hosta w DHCP wyświetlany jest pasek postępu oraz komunikat **Szukam hosta w DHCP...** .
- Gdy znaleziono więcej adresów, pingowanie rotuje po wszystkich po kolei (w logu widać, który adres odpowiada).

**Przycisk Stop:** Zatrzymuje cykliczne pingowanie i wypisuje podsumowanie (wysłane/odebrane, straty, min/śr/max). Interwał w sekundach (1-300) ustawisz obok przycisków, a podsumowanie cząstkowe pojawia się co 10 pomiarów.

**Opcje dźwięku:**

- **Bez dźwięku** - ping działa bez powiadomień dźwiękowych.
- **Dźwięk przy sukcesie** - program odtwarza dźwięk po udanym pingu.
- **Dźwięk przy błędzie** - program odtwarza dźwięk, gdy ping się nie powiedzie.

**Log zdarzeń (up/down):** w grupie *Log zdarzeń* włączamy **Zapisuj zmiany stanu (up/down)**. Program zapisuje do pliku *ping-wydarzenia.csv* (profil użytkownika) każdą zmianę: kiedy host przestał odpowiadać i kiedy wrócił, wraz z czasem trwania poprzedniego stanu. To gotowy materiał do zgłoszenia „sieć padała o 14:32” - do tej pory liczniki pokazywały tylko stan bieżący, a ten fakt ginął z ekranu. Przycisk **Otwórz log...** pokazuje plik.

## 5. Wymagania techniczne

Program korzysta z poleceń PowerShell DHCP:

- **Get-DhcpServerv4Scope**
- **Get-DhcpServerv4Lease**

Na komputerze musi być dostępny moduł DHCP / RSAT. Użytkownik musi mieć uprawnienia do odpytywania serwera DHCP.

Funkcja MSRA wymaga dostępności narzędzia Microsoft Remote Assistance **msra.exe** na komputerze, z którego uruchamiany jest program. Gdy go nie ma, program zaproponuje połączenie przez RDP.

Funkcja RDP nie wymaga niczego dodatkowego na Twoim komputerze, ale komputer docelowy musi mieć włączony pulpit zdalny.

Funkcja Tactical wymaga agenta TacticalRMM na komputerze docelowym oraz klucza API (patrz rozdział 8).

## 6. Typowe komunikaty

**Brak hostname!** Nie wpisano nazwy hosta ani adresu IP.

**Brak adresu IP w DHCP** Nie znaleziono wpisu DHCP dla podanej nazwy.

**Nie znaleziono aktywnego IP** Adres istnieje w DHCP, ale nie odpowiada na ping.

**Błąd DHCP** Wystąpił problem z odpytaniem serwera DHCP, uprawnieniami albo modułem RSAT.

**IP w DNS jest inny niż w DHCP** DNS zwraca inny adres IP niż aktywny adres znaleziony w DHCP. W komunikacie wyświetlany jest adres IP zwrócony przez DNS.

**Znaleziono więcej niż jeden adres IP** Dla podanego hostname znaleziono więcej niż jeden aktywny adres IP. Wybierz jeden z listy wyników - dopiero wtedy MSRA, RDP i kopiowanie zadziałają.

**Aktualna wartość IP nie wygląda jak poprawny adres IPv4** Program nie uruchomi MSRA, ponieważ wynik nie jest pojedynczym poprawnym adresem IPv4.

## 7. Zakładka: ACL (obecnie ukryta)

Zakładka ACL służy do generowania list dostępu (ACL) dla urządzeń Cisco na podstawie podanych adresów IP. Podsieci, nazwy list oraz startowe numery sekwencji zmienisz w zakładce **Ustawienia** .

**Pole IP źródłowe:** W tym polu należy wpisać adres IP hosta, dla którego ma zostać wygenerowana reguła ACL.

Program automatycznie sprawdza, czy podany adres posiada rezerwację w DHCP. Wynik wyświetlany jest obok pola:

- **✅ Rezerwacja DHCP OK** – adres posiada rezerwację.
- **❌ Brak rezerwacji DHCP** – adres nie posiada rezerwacji.

Podczas sprawdzania wyświetlany jest status: *⏳ Sprawdzanie DHCP...* .

Brak rezerwacji nie blokuje działania – program wygeneruje ACL mimo ostrzeżenia.

**Pole Lista IP:** W tym polu należy wpisać adresy docelowe (ENTER lub przecinki). Niepoprawne wpisy są zgłaszane z liczbą i pomijane.

Program automatycznie:

- dzieli adresy na odpowiednie podsieci,
- łączy je w zakresy (wildcard),
- generuje reguły ACL IN oraz OUT,
- pomija numery sekwencji podzielne przez 10.

**Przycisk Generuj ACL:** generuje pełną konfigurację Cisco (conf t, ACL, resequence, zapis).

**Pole Osoba (remark):** opcjonalne imię i nazwisko do opisania bloku wpisów (jak w cisco-acl-helper). Przed wpisami permit pojawi się *remark Osoba* , a za nimi *remark Osoba (koniec)* . Puste pole oznacza brak remarków.

**Przycisk Kopiuj do schowka:** kopiuje wygenerowaną konfigurację do schowka.

## 8. Aktualizacje

Program sam sprawdza aktualizacje w GitHub Releases (**blem5000/Narzedzia-IP-2**) przy każdym uruchomieniu. Gdy znajdzie nowszą wersję, pobiera paczkę **NarzedziaIP2-windows.zip** i przekazuje instalację osobnemu programowi **NarzedziaIP.Updater.exe**, który podmienia pliki i uruchamia nową wersję. Gdy instalator GUI jest niedostępny, używany jest awaryjny skrypt .bat.

**Sprawdzanie pobranej paczki:** przed instalacją program liczy SHA-256 pobranego pliku i porównuje go z sumą zapisaną w opisie wydania. Przy niezgodności aktualizacja jest przerwana i nic nie jest instalowane - komunikat **Niezgodny SHA-256 paczki aktualizacji** oznacza, że pobrany plik nie jest tym, który opublikowano.

W zakładce **Ustawienia** widać numer wersji, przełącznik automatycznego sprawdzania oraz przycisk **Sprawdź aktualizacje** do ręcznej kontroli. Przycisk **Pomiń tę wersję** wycisza powiadomienie o danym wydaniu. Numer wersji musi zgadzać się z tagiem wydania (np. wersja 1.1.0 to tag v1.1.0). Instalator NarzedziaIP.Updater.exe można też uruchomić podwójnym kliknięciem: sam wykryje program obok siebie, sprawdzi wersję i zaproponuje instalację.

## 9. TacticalRMM

**Sposób połączenia TacticalRMM** otwiera pulpit zdalny komputera przez TacticalRMM (Mesh) - bez zabierania sesji jak RDP: użytkownik widzi na ekranie, co robisz. Program znajduje agenta po nazwie hosta przez API TacticalRMM i otwiera stronę przejcia do pulpitu w przeglądarce.

W zakładce **Ustawienia** wpisz adres API TacticalRMM, klucz API (klucz to dane wrażliwe - chroń je jak hasło) oraz adres dashboardu. Przycisk **Testuj** sprawdza połączenie i liczy agentów. Wpisanie samego adresu IP nie wystarczy - potrzebna jest nazwa hosta (wpisz ją albo wyszukaj komputer). Przy wyszukiwaniu w DHCP program od razu sprawdza też TacticalRMM: gdy agenta nie ma, przycisk **Pokaż w RMM** jest wyszarzony z podpowiedzią.

Adresy można też rozdać centralnie przez rejestr (**HKCU\\Software\\NarzedziaIP2**): wartości *TacticalApiUrl*, *TacticalApiKey* i *TacticalDashboardUrl* wygrywają z ustawieniami z programu. Gotowy szablon to plik **TacticalRMM-config-TEMPLATE.reg** obok programu - uzupełnij, połóż na share i kliknij dwukrotnie (uprawnienia admina zbędne).

**Bezpieczeństwo połączenia:** klucz API to dane wrażliwe, więc program domyślnie odmawia łączenia z API Tactical pod adresem *http://* - klucz leciałby wtedy otwartym tekstem. Dopuszczone są *https://* oraz adresy lokalne (localhost, 127.0.0.1). Jeśli w Twojej sieci API działa bez TLS, zaznacz w Ustawieniach opcję *„Zezwól na nieszyfrowane HTTP do API Tactical"* (w rejestrze: *TacticalAllowInsecureHttp=1*).

Program może działać wprost z sharea sieciowego: przy aktualizacji działający plik jest odsuwany na bok (*\*.old*), więc druga osoba może w tym czasie pracować na starej wersji, a resztki sprzątane są przy starcie.

## 10. Zakładka: Ustawienia

**Serwer DHCP:** adres używany do wszystkich wyszukiwań. Przycisk **Sprawdź** weryfikuje połączenie i po sukcesie zapamiętuje adres na stałe (również między uruchomieniami).

**TacticalRMM:** adres API, klucz API oraz adres dashboardu (klucz możesz sprawdzić przyciskiem **Testuj** ). Pola mogą przychodzić z rejestru firmowego - wtedy edycja w programie nie ma znaczenia, bo rejestr wygrywa.

**ACL:** podsieci z maskami, nazwy list IN/OUT oraz startowe numery sekwencji używane przy generowaniu. Bledne wartości blokują generowanie z komunikatem.

**MAC:** format kopiowanego adresu MAC - zwykły, z dwukropkami albo ciscowy.

Na dole zakładki jest panel aktualizacji (wersja, automatyczne sprawdzanie, ręczne **Sprawdź aktualizacje** ).

**Przycisk Log błędów:** otwiera plik z zapisanymi błędami programu (**%LOCALAPPDATA%\\NarzedziaIP2\\bledy.log**). Trafiają tam sytuacje, które program przechwycił, ale nie mógł obsłużyć sam - np. problem z zapisem ustawień albo brak uprawnień do serwera DHCP. Dzięki temu zamiast zgadywać przyczynę dziwnego zachowania można zajrzeć do logu. Ten sam błąd powtarzany w krótkim czasie jest zapisywany raz, z licznikiem powtórzeń.

Okno programu jest skalowalne, a jego rozmiar zapamiętuje się przy zamknięciu. Ustawienia użytkownika (w tym powyższe) przenoszą się automatycznie na nowe wersje programu po aktualizacji.
