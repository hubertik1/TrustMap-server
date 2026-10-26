# Weryfikacja konta demo — aktualizacja 26 października 2026

Backend z obsługą demo wdrożono 26 października na istniejący produkcyjny App Service
`app-trust-map` w Azure Poland Central. Włączono pięć ustawień `DemoAccess__*`.
`POST /auth/demo` zwraca HTTP 200 i pozwala korzystać z przygotowanej zawartości.
Wcześniejsze testy funkcjonalne opisane poniżej wykonano lokalnie; zakres dodatkowego
testu produkcyjnego podano osobno.

## Zakres i wyniki

- Backend: **238 testów integracyjnych, 0 błędów**, w tym 8 scenariuszy dotyczących
  bezpośrednio konta demo.
- Klient Swift: **94 testy, 0 błędów**, w tym zapis, odnowienie i odrzucenie sesji demo.
- Katalog tekstów: **558 kluczy w 8 językach**, walidacja zakończona powodzeniem.
- Interfejs natywny na **iPadzie Air 11″ (M3), iPadOS 26.5, symulator**: potwierdzono
  położenie przycisku demo w tym samym rzędzie co linki, otwarcie formularza,
  odrzucenie złego hasła, poprawne logowanie i dostęp do Places. Końcowy test
  interfejsu potwierdził także otwarcie Sunday Table, przytrzymanie recenzji,
  wysłanie zgłoszenia, wejście na profil Eli, blokadę i zachowanie sesji po restarcie.
  Wynik potwierdzono niezależnie w API: jedna blokada Eli, dwóch pozostałych
  znajomych oraz zapisane zgłoszenie z przygotowanym plikiem moderacyjnym.
  Przerwa w pracy komputera wydłużyła czas testu; nie jest to pomiar wydajności.
- Osobno zalogowano się na przygotowane konto przy użyciu danych z prywatnego pliku,
  przez działający lokalny serwer, bez modyfikowania jego recenzji i znajomości.

| Sprawdzenie | Wynik i zakres |
|---|---|
| Logowanie i nieprawidłowe dane | Poprawne dane tworzą zwykłą sesję; niepoprawne są odrzucane bez tworzenia konta. |
| Zawartość | 3 znajomych, 6 miejsc, 7 recenzji miejsc, 6 recenzji dań, 6 pinezek mapy; zaproszenie przychodzące i wychodzące. |
| Miejsca bez lokalizacji | Lista i szczegóły są dostępne bez współrzędnych urządzenia. Dane są w Londynie; reviewer powinien wejść w Places. |
| Zdjęcia i awatary | Pobieranie oryginału i dostępnych wariantów zdjęć oraz awatarów działa z uwierzytelnieniem. |
| Zgłaszanie | Zgłoszenie recenzji miejsca i dania trafia do bazy; zgłoszenie dania ze zdjęciem tworzy kopię dowodową oraz prywatny plik `.eml` z `review.json` i obrazem. |
| Blokowanie | Ukrywa profil, recenzję, zdjęcia i warianty, awatar, aktywność na mapie i w feedzie oraz powiadomienia. Stan utrzymuje się po ponownym logowaniu. |
| Odblokowanie | Lista blokad i usunięcie blokady działają. Nie przywraca to znajomości ani dostępu do recenzji dostępnych wyłącznie znajomym. |
| Własne treści | Zmiana oceny, tekstu recenzji, nazwy dania i ceny; dodanie i usunięcie zdjęcia; utworzenie i usunięcie recenzji — testy API zakończone powodzeniem. |
| Znajomości | Przyjęcie zaproszenia Sama, anulowanie wychodzącego oraz wyszukanie Zoe i wysłanie zaproszenia działają. |
| Powiadomienia w aplikacji | Odczyt i oznaczenie jako przeczytane działają. Nie jest to test dostarczenia push na urządzenie fizyczne. |
| Sesja | Odnowienie tokenów działa; wylogowanie unieważnia refresh token. |
| Usunięcie konta | Usunięcie działa i unieważnia sesje. Kolejne logowanie nie odtwarza usuniętego konta. Test wykonano na jednorazowym koncie QA. |
| Prywatność danych demo | Zwykły użytkownik spoza znajomych nie może odczytać recenzji dostępnej znajomym. |

## Poprawki wykonane podczas weryfikacji

1. Nieprawidłowy login lub hasło demo otrzymują właściwy, przetłumaczony komunikat,
   zamiast komunikatu dotyczącego przywracania sesji.
2. Generator danych ustawia nazwy wyświetlane przykładowych pinezek. Wcześniej
   interfejs pokazywał wspólny adres zamiast nazw takich jak `Sunday Table · Demo`.
   Poprawiono też sześć nazw w istniejącym lokalnym zestawie przez zwykłe API.
   Dodano sprawdzenie nazw do testów integracyjnych.

Końcowa kontrola właściwego konta demonstracyjnego: 3 znajomych, 6 miejsc
z prawidłowymi nazwami i brak blokad. Testy zgłaszania i blokowania w interfejsie
wykonano na oddzielnym koncie QA.

## Strony i blokady przed ponownym review

| Element | Wynik |
|---|---|
| Produkcyjne API demo | **Działa: HTTP 200**. Wdrożone i skonfigurowane 26 października. Test API obejmuje logowanie, treści, media, odnowienie i wylogowanie. Pozostaje użyć docelowego buildu aplikacji w App Store Connect. |
| Polityka prywatności | Otwiera się w przeglądarce pod `https://trustmap.hubertik.com/privacy/`. |
| Pomoc i kontakt | Otwierają się pod `https://trustmap.hubertik.com/support/`; widoczny adres kontaktowy. |
| Regulamin/EULA | Aktualizacja 26 października: regulamin i zasady społeczności są opublikowane pod `https://trustmap.hubertik.com/terms/`, z linkiem do standardowej EULA Apple. W kodzie aplikacji podpięto `TrustMapTermsOfServiceURL` dla Local i Prod: ekran powitalny i Settings → About. Wymaga dystrybucji nowego buildu aplikacji. |

Weryfikacja zgłoszeń potwierdza lokalny tryb plików `.eml`. Nie potwierdza doręczenia
do produkcyjnej skrzynki ani czasu reakcji operatora. Trzeba zapewnić regularny
odbiór tych plików albo skonfigurować i sprawdzić SMTP.

Skrypty HTTP otrzymały 403 ze stron marketingowych, ale przeglądarka otworzyła
politykę i pomoc poprawnie. Wyniku narzędzi HTTP nie potraktowano jako awarii tych
stron dla użytkownika.

## Produkcja — 26 października 2026

- Publikacja Release, linux-x64, framework-dependent; istniejący plan Free F1 i .NET 10. Wdrożenie ZIP zakończyło się sukcesem.
- Przed wdrożeniem wykonano spójną kopię SQLite i poprzednich plików aplikacji. Po wdrożeniu `PRAGMA quick_check` potwierdził integralność bazy; wszystkie wcześniejsze identyfikatory rekordów oraz pliki mediów pozostały zachowane. Istniejące ustawienia produkcji pozostały bez zmian. Kopie i dane dostępowe są prywatne, poza Gitem.
- `/health/live` i `/health/ready`: HTTP 200.
- Logowanie demo: HTTP 200; 3 znajomych, 1 zaproszenie przychodzące i 1 wychodzące, 7 widocznych recenzji miejsc, 6 recenzji dań, 13 pozycji feedu, 5 powiadomień, brak blokad. Publiczny katalog zawiera również wcześniejsze miejsca; demo dodaje 6 fikcyjnych miejsc.
- Zdjęcie recenzji i awatar: HTTP 200 z autoryzacją. Odnowienie sesji i wylogowanie zakończyły się powodzeniem.
- Produkcyjny test nie usuwał treści ani konta i nie zmieniał znajomości czy blokad. Scenariusze modyfikacji, zgłaszania i blokowania pozostają potwierdzone wcześniejszymi testami lokalnymi.
- Moderacja na produkcji używa prywatnych plików `.eml`, bez SMTP. Operator musi je regularnie przeglądać; samo zapisanie pliku nie oznacza doręczenia do skrzynki ani reakcji moderatora.
