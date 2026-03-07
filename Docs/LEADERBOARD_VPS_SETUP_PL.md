# Leaderboard na Oracle VPS - instrukcja krok po kroku

Ten plik jest napisany "lopatologicznie".
Robilismy po naszej stronie kod i konfiguracje repo.
Ty musisz tylko wykonac kroki nizej, dokladnie po kolei.

## 0. Co jest juz gotowe po naszej stronie

- Backend API leaderboard: `Backend/LeaderboardApi`
- Infra do odpalenia na VPS: `Infra/leaderboard`
- Klient Unity:
  - submit runu po Game Over
  - pobieranie topki online
  - pobieranie "moja pozycja"
  - fallback lokalny, gdy online nie dziala
- Smoke test API: `Tools/leaderboard/smoke_test.ps1`

## 1. Co przygotowac zanim zaczniesz

- Oracle VPS (Ubuntu)
- Domena lub subdomena, np. `leaderboard.twojadomena.pl`
- Dostep SSH do VPS
- GitHub repo z projektem

## 2. Kroki na VPS

## Krok 1: Zaloguj sie na serwer

```bash
ssh ubuntu@TWOJ_IP_VPS
```

## Krok 2: Zainstaluj Docker + Compose

```bash
sudo apt update
sudo apt install -y ca-certificates curl gnupg
sudo install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg | sudo gpg --dearmor -o /etc/apt/keyrings/docker.gpg
sudo chmod a+r /etc/apt/keyrings/docker.gpg
echo \
  "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/ubuntu \
  $(. /etc/os-release && echo $VERSION_CODENAME) stable" | \
  sudo tee /etc/apt/sources.list.d/docker.list > /dev/null
sudo apt update
sudo apt install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
sudo usermod -aG docker $USER
```

Wyloguj sie i zaloguj ponownie na SSH (zeby grupa `docker` zaczela dzialac).

## Krok 3: Skopiuj repo

```bash
git clone https://github.com/SorontarX10/necroforge.git
cd necroforge/Infra/leaderboard
```

## Krok 4: Zrob plik `.env`

```bash
cp .env.example .env
nano .env
```

Ustaw minimum:

- `POSTGRES_PASSWORD` -> bardzo mocne haslo
- `LEADERBOARD_DOMAIN` -> np. `leaderboard.twojadomena.pl`
- `LEADERBOARD_ADMIN_EMAIL` -> Twoj mail
- `LEADERBOARD_VERSION_LOCK` -> wersja gry, np. `0.7.2`

Reszte zostaw jak jest na start.

## Krok 5: Ustaw DNS

W panelu domeny dodaj rekord `A`:

- host: `leaderboard` (lub pelna nazwa subdomeny)
- value: `IP_TWOJEGO_VPS`

Poczekaj az DNS sie propaguje (czasem 5 min, czasem 1h+).

## Krok 6: Otworz porty w Oracle

W Oracle Cloud (Security List albo NSG) otworz:

- `80/tcp`
- `443/tcp`

Bez tego HTTPS nie ruszy.

## Krok 7: Odpal uslugi

```bash
cd ~/necroforge/Infra/leaderboard
docker compose up -d --build
docker compose ps
```

## Krok 8: Sprawdz czy API zyje

```bash
curl -s https://leaderboard.twojadomena.pl/health
```

Masz dostac JSON ze `status: "ok"`.

## Krok 9: Jak cos nie dziala, sprawdz logi

```bash
docker compose logs -f api
docker compose logs -f caddy
docker compose logs -f db
```

## 3. Kroki w Unity

## Krok 1: Ustaw URL backendu

Edytuj plik:

- `Assets/StreamingAssets/leaderboard_config.json`

Wstaw:

```json
{
  "base_url": "https://leaderboard.twojadomena.pl",
  "season": "global_all_time",
  "timeout_seconds": 8
}
```

## Krok 2: Podepnij pola UI (Main Menu i Game Over)

W skryptach sa nowe pola:

- `leaderboardStatusText`
- `leaderboardMyRankText`
- `leaderboardRetryButton`

Musisz je podpiac w Inspectorze na obiektach:

- `MainMenuController`
- `GameOverUIController`

Jesli nie podepniesz, ranking nadal bedzie dzialal, ale bez czytelnego statusu i przycisku retry.

## Krok 3: Zrob build demo

Normalny build demo jak do tej pory.

## 4. Szybki test API z Twojego komputera

W lokalnym repo:

```powershell
pwsh Tools/leaderboard/smoke_test.ps1 -BaseUrl "https://leaderboard.twojadomena.pl"
```

Powinienes zobaczyc:

- `submit validation_state: accepted` (albo inny stan walidacji)
- `my rank found: True`

## 5. Czego nie moge zrobic za Ciebie

- Nie moge kliknac DNS w panelu rejestratora.
- Nie moge kliknac reguly sieci w panelu Oracle.
- Nie moge zalogowac sie na Twoje konto VPS.

To musisz zrobic recznie.
