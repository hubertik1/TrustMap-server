# Weryfikacja konta demo — 25 października 2026

Konto demo sprawdzono lokalnie. Dostęp przez produkcyjny adres API nie jest jeszcze
gotowy dla Apple: `POST /auth/demo` zwrócił HTTP 404.

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
| Produkcyjne API demo | **Niedostępne: HTTP 404**. Trzeba wdrożyć backend i włączyć wygenerowaną konfigurację, a potem powtórzyć logowanie z docelowego buildu. |
| Polityka prywatności | Otwiera się w przeglądarce pod `https://trustmap.hubertik.com/privacy/`. |
| Pomoc i kontakt | Otwierają się pod `https://trustmap.hubertik.com/support/`; widoczny adres kontaktowy. |
| Regulamin/EULA | Aktualizacja 26 października: regulamin i zasady społeczności są opublikowane pod `https://trustmap.hubertik.com/terms/`, z linkiem do standardowej EULA Apple. W kodzie aplikacji podpięto `TrustMapTermsOfServiceURL` dla Local i Prod: ekran powitalny, formularz demo i Settings → About. Wymaga dystrybucji nowego buildu aplikacji. |

Weryfikacja zgłoszeń potwierdza lokalny tryb plików `.eml`. Nie potwierdza doręczenia
do produkcyjnej skrzynki ani czasu reakcji operatora. Trzeba zapewnić regularny
odbiór tych plików albo skonfigurować i sprawdzić SMTP.

Skrypty HTTP otrzymały 403 ze stron marketingowych, ale przeglądarka otworzyła
politykę i pomoc poprawnie. Wyniku narzędzi HTTP nie potraktowano jako awarii tych
stron dla użytkownika.
