# Blokowanie i zgłoszenia

## Aplikacja

Menu `…` na profilu pozwala zablokować użytkownika; blokowanie jest dostępne wyłącznie na profilu osoby. Przytrzymanie cudzej oceny miejsca lub dania, wewnątrz szczegółów restauracji, otwiera menu `Report Review`. Na karcie nie ma przycisku kropek ani flagi. Zgłaszanie nie jest dostępne w feedzie ani na listach ocen profilu. Zgłoszenie wymaga jedynie potwierdzenia; nie ma formularza ani pola tekstowego. Kliknięcie nazwy autora oceny otwiera jego profil.

Lista `Settings → Privacy → Blocked Users` pozwala odblokować użytkownika. Blokada działa w obu kierunkach: ukrywa profil, oceny, zdjęcia, avatar, aktywność, powiadomienia i treści na mapie. Usuwa znajomość oraz anuluje oczekujące zaproszenia. Odblokowanie nie przywraca znajomości. Każdy użytkownik zarządza własnymi blokadami.

Serwer wymusza te zasady również dla bezpośrednich zapytań o recenzje i zdjęcia. Aplikacja po zablokowaniu usuwa pamięć podręczną zdjęć i odtwarza widoki, aby ukryć wcześniej wczytane treści.

## Wersja bez usługi pocztowej

Domyślna konfiguracja działa bez SMTP. Serwer przyjmuje zgłoszenie, zapisuje je w bazie i co około 30 sekund przygotowuje kompletną wiadomość MIME `.eml` dla `hubert.mar@icloud.com`.

**To nie dostarcza wiadomości do skrzynki iCloud.** Gotowe pliki trzeba pobrać przez dostęp administracyjny do hostingu i otworzyć w programie pocztowym, np. Mail. Pliki nie są udostępniane przez publiczne API. Nie powstaje osobny panel moderacyjny.

Domyślne położenie, względem katalogu aplikacji serwera:

```text
App_Data/moderation-mails/<ID-zgłoszenia-bez-myślników>/*.eml
```

Przy uruchomieniu bezpośrednio z repozytorium jest to `src/TrustMap.Api/App_Data/moderation-mails`. W Docker Compose katalog znajduje się na istniejącym trwałym wolumenie `trustmap_sqlite_data`, pod `/app/App_Data/moderation-mails`.

Każda wiadomość zawiera dane recenzji z chwili zgłoszenia, autora, miejsce, ocenę, treść, metadane zdjęć, kopie podglądowe zdjęć oraz załącznik `review.json`. Nie zawiera tokenów sesji ani haseł użytkowników. Podglądy pozostają dostępne nawet jeśli autor później zmieni lub usunie recenzję. Wiadomości większe niż limit zdjęć w mailu odsyłają do pozostałych podglądów na stronie potwierdzenia.

## Przyciski moderacji

W pliku `.eml` są trzy linki:

- **Ignoruj zgłoszenie** — zamyka zgłoszenie, pozostawia recenzję.
- **Usuń recenzję** — usuwa ją z widoków i API dla wszystkich, także autora, oraz blokuje dostęp do jej zdjęć. Serwer zachowuje rekord oznaczony jako usunięty przez moderację; autor nie może cofnąć tej decyzji przez edycję.
- **Zablokuj konto autora** — zawiesza konto, ukrywa jego treści i unieważnia sesje oraz rejestracje push. Wymaga osobnego hasła administratora.

Otwarcie linku pokazuje treść i przycisk potwierdzenia. Dopiero wysłanie formularza wykonuje akcję. Dzięki temu skanery poczty nie wykonają jej przez samo otwarcie linku. Linki są przypisane do konkretnego zgłoszenia i akcji, wygasają po 7 dniach i po rozpatrzeniu zgłoszenia nie pozwalają wykonać kolejnej decyzji. Zmiana adresu serwera lub utrata kluczy Data Protection unieważnia możliwość użycia istniejących linków.

## Konfiguracja hostingu

Ustawienia domyślne są w `src/TrustMap.Api/appsettings.json`. Nadpisuj je zmiennymi środowiskowymi na hostingu:

```text
Moderation__Enabled=true
Moderation__EmailProvider=file
Moderation__RecipientEmail=hubert.mar@icloud.com
Moderation__PublicBaseUrl=https://twoj-serwer.example
Moderation__EmailDirectory=/trwaly-katalog/moderation-mails
Moderation__DataProtectionKeyDirectory=/trwaly-katalog/moderation-keys
Moderation__MaxReportsPerUserPerDay=20
Moderation__EvidenceRetentionDays=30
```

`PublicBaseUrl` musi wskazywać rzeczywisty, publiczny adres HTTPS tego API, bez ścieżki. Domyślna wartość wskazuje produkcyjny adres TrustMap. Katalogi wiadomości i kluczy muszą być zapisywalne przez proces API oraz przetrwać restart i ponowne wdrożenie. Dla Azure App Service Linux można użyć `/home/data/trustmap/moderation-mails` i `/home/data/trustmap/moderation-keys`, przy włączonym trwałym storage. Nie udostępniaj tych katalogów jako plików statycznych. Na Unix serwer ustawia prywatne uprawnienia katalogów i plików wiadomości.

Do banowania ustaw `Moderation__AdminPasswordSha256` w ustawieniach hostingu. Użyj osobnego, losowego hasła administratora o dużej długości (np. co najmniej 24 znaki). Skrót można wygenerować bez zapisywania hasła w historii terminala:

```bash
python3 -c 'import getpass, hashlib; print(hashlib.sha256(getpass.getpass("Hasło administratora: ").encode("utf-8")).hexdigest())'
```

Wklej otrzymany skrót jako `Moderation__AdminPasswordSha256`, a oryginalne hasło wpisuj przy potwierdzaniu bana. Domyślnie skrót jest pusty: zgłaszanie, ignorowanie i usuwanie działają, ale banowanie zwraca błąd konfiguracji. Nie zapisuj hasła ani skrótu w repozytorium.

## Zapis, ponawianie i retencja

Zgłoszenie jest zapisane przed przygotowaniem wiadomości. Awaria zapisu pliku lub opcjonalnego SMTP nie gubi zgłoszenia; zadanie ponawia próbę z rosnącym odstępem. `EmailProcessedAtUtc` oznacza przygotowanie pliku w trybie `file` albo przyjęcie wiadomości przez SMTP w trybie `smtp`; nie jest potwierdzeniem odbioru przez adresata.

Po 30 dniach od przygotowania/wysłania serwer usuwa zgłoszenie, kopie zdjęć i lokalny katalog wiadomości. Dotyczy to także nierozpatrzonych zgłoszeń, dlatego należy regularnie sprawdzać pliki. Nierozpatrzone zgłoszenia z nieudaną próbą przygotowania/wysyłki pozostają do ponowienia. Zgłoszenia rozpatrzone przed przygotowaniem wiadomości są usuwane po 30 dniach od decyzji. Pobranie pliku na własny komputer tworzy kopię, której serwer nie może usunąć.

Ta sama osoba może zgłosić daną recenzję raz. Kolejne zgłoszenie tej samej recenzji zwraca poprzednie ID. Obowiązuje limit 20 nowych zgłoszeń na 24 godziny oraz limit częstotliwości HTTP.

## Opcjonalne wysyłanie w przyszłości

Automatyczną dostawę można włączyć bez zmiany aplikacji mobilnej, po uzyskaniu dostępu do usługi SMTP:

```text
Moderation__EmailProvider=smtp
Moderation__Smtp__Host=<host-uslugi>
Moderation__Smtp__Port=587
Moderation__Smtp__EnableSsl=true
Moderation__Smtp__Username=<konto-uslugi>
Moderation__Smtp__Password=<sekret-na-hostingu>
Moderation__Smtp__SenderEmail=<adres-nadawcy-uslugi>
```

W trybie `file` adres nadawcy `reports@trustmap.invalid` jest wyłącznie nagłówkiem przygotowanego pliku. W trybie SMTP ustaw prawdziwy adres obsługiwany przez dostawcę. Zmiana trybu nie wysyła automatycznie plików już przygotowanych w trybie `file`.

## API i wdrożenie

```text
GET    /me/blocks
PUT    /me/blocks/{userId}
DELETE /me/blocks/{userId}
POST   /reports                   { "reviewType": "placeReview" | "dishReview", "reviewId": "UUID" }
```

Wymagają uwierzytelnienia aktywnego użytkownika. `POST /reports` zwraca `202 { "id": "UUID" }`. Można zgłosić wyłącznie cudzą recenzję dostępną dla zgłaszającego.

Migracja `20261006192108_AddBlockingAndReporting` dodaje tabele blokad, zgłoszeń i kopii zdjęć oraz oznaczenia moderacji recenzji. Najpierw wdroż serwer z migracją i konfiguracją, następnie nową wersję aplikacji. Migracja jest uruchamiana przy starcie, jeśli `Database__ApplyMigrationsOnStartup=true`. Testy integracyjne obejmują blokady, chronione media, dowody zgłoszeń, prywatne pliki `.eml`, ponawianie, retencję i akcje moderacji.
