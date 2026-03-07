# Oracle + DuckDNS + Leaderboard (od zera)

Ten runbook jest dla przypadku:
- nie masz juz VPS (usuniety),
- chcesz nowy VPS na Oracle Always Free,
- chcesz darmowa subdomene DuckDNS.

Robilismy wszystko po stronie kodu.
Ty wykonujesz tylko kroki operacyjne.

## 0. Co jest juz gotowe w repo

- Backend API: `Backend/LeaderboardApi`
- Deploy Docker: `Infra/leaderboard/docker-compose.yml`
- Reverse proxy + HTTPS: `Infra/leaderboard/Caddyfile`
- Smoke testy API:
  - `Tools/leaderboard/smoke_test.ps1`
  - `Tools/leaderboard/security_smoke_test.ps1`
- Skrypt do DuckDNS (cron co 5 min):
  - `Infra/leaderboard/scripts/install_duckdns_cron.sh`

## 1. Oracle Cloud - nowa instancja

## Krok 1: Zaloz/zaloguj konto Oracle

1. Wejdz na `https://www.oracle.com/cloud/free/`.
2. Zaloguj sie do panelu OCI.
3. Upewnij sie, ze dzialasz w tym samym regionie przez caly proces.

## Krok 2: Stworz VM (Compute Instance)

1. OCI -> `Compute` -> `Instances` -> `Create instance`.
2. Obraz: Ubuntu (LTS).
3. Shape: wybierz `Always Free Eligible` (taki, ktory OCI pozwala utworzyc).
4. Network:
  - przypisz Public IPv4,
  - zostaw domyslna VCN/subnet jesli nie masz wlasnej.
5. SSH keys:
  - wygeneruj nowy klucz albo wgraj swoj public key.
6. Kliknij `Create`.
7. Poczekaj, az status instancji bedzie `Running`.

## Krok 3: Otworz porty w OCI (to jest krytyczne)

1. OCI -> `Networking` -> `Virtual cloud networks`.
2. Otworz VCN przypisana do instancji.
3. Wejdz w `Security Lists` (lub NSG, jesli tego uzywasz).
4. Dodaj Ingress rules:
  - TCP `80` z `0.0.0.0/0`
  - TCP `443` z `0.0.0.0/0`
5. (Opcjonalnie, ale zalecane) SSH `22` ogranicz do swojego IP, nie do calego swiata.

## 2. SSH i przygotowanie systemu

## Krok 1: Polacz sie przez SSH

```bash
ssh -i /sciezka/do/klucza ubuntu@PUBLIC_IP_VPS
```

## Krok 2: Zainstaluj Docker + Compose + cron

```bash
sudo apt update
sudo apt install -y ca-certificates curl gnupg cron
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
sudo systemctl enable --now cron
```

Wyloguj i zaloguj SSH ponownie (zeby grupa `docker` zaczela dzialac).

## 3. DuckDNS (darmowa subdomena)

## Krok 1: Utworz subdomene

1. Wejdz na `https://www.duckdns.org/` i zaloguj sie.
2. Wpisz nazwe subdomeny, np. `necroforge-lb`.
3. Kliknij add domain.
4. Skopiuj `token` z panelu DuckDNS.

Twoja domena bedzie: `necroforge-lb.duckdns.org`

## Krok 2: Skonfiguruj auto-update IP na VPS

```bash
git clone https://github.com/SorontarX10/necroforge.git
cd necroforge
chmod +x Infra/leaderboard/scripts/install_duckdns_cron.sh
./Infra/leaderboard/scripts/install_duckdns_cron.sh necroforge-lb TWOJ_DUCKDNS_TOKEN
```

Sprawdz odpowiedz:
- `OK` znaczy, ze DuckDNS przyjal update.

## Krok 3: Sprawdz czy DNS wskazuje na VPS

```bash
dig +short necroforge-lb.duckdns.org
curl "https://www.duckdns.org/update?domains=necroforge-lb&token=TWOJ_DUCKDNS_TOKEN&ip="
```

Pierwsza komenda ma zwrocic IP VPS.

## 4. Deploy leaderboarda

## Krok 1: Ustaw `.env`

```bash
cd ~/necroforge/Infra/leaderboard
cp .env.example .env
nano .env
```

Ustaw minimum:

- `POSTGRES_PASSWORD` -> mocne haslo
- `LEADERBOARD_DOMAIN` -> `necroforge-lb.duckdns.org`
- `LEADERBOARD_ADMIN_EMAIL` -> twoj email
- `LEADERBOARD_VERSION_LOCK` -> wersja gry, np. `0.7.2`

## Krok 2: Start kontenerow

```bash
cd ~/necroforge/Infra/leaderboard
docker compose up -d --build
docker compose ps
```

## Krok 3: Health check

```bash
curl -s https://necroforge-lb.duckdns.org/health
```

Ma byc JSON ze `status: "ok"`.

## 5. Unity i build demo

## Krok 1: Ustaw backend URL w grze

Edytuj:
- `Assets/StreamingAssets/leaderboard_config.json`

Wstaw:

```json
{
  "base_url": "https://necroforge-lb.duckdns.org",
  "season": "global_all_time",
  "timeout_seconds": 8
}
```

## Krok 2: Podepnij nowe pola UI w Inspectorze

Na komponentach:
- `MainMenuController`
- `GameOverUIController`

podepnij:
- `leaderboardStatusText`
- `leaderboardMyRankText`
- `leaderboardRetryButton`

## Krok 3: Zrob build demo

Normalny build demo.

## 6. Testy koncowe (lokalnie)

```powershell
pwsh Tools/leaderboard/smoke_test.ps1 -BaseUrl "https://necroforge-lb.duckdns.org"
pwsh Tools/leaderboard/security_smoke_test.ps1 -BaseUrl "https://necroforge-lb.duckdns.org"
```

## 7. Najczestsze problemy i szybkie naprawy

## Problem: HTTPS nie wstaje

Sprawdz:
1. Czy `LEADERBOARD_DOMAIN` to dokladnie twoja subdomena DuckDNS.
2. Czy DNS pokazuje IP VPS (`dig +short`).
3. Czy porty `80` i `443` sa otwarte w OCI.
4. Logi:

```bash
docker compose logs -f caddy
```

## Problem: API dziala lokalnie, ale nie przez internet

Sprawdz:
1. `docker compose ps`
2. Logi:

```bash
docker compose logs -f api
docker compose logs -f db
```

## Problem: DuckDNS nie aktualizuje IP

Sprawdz:

```bash
crontab -l
cat ~/duckdns/duck.log
```

Wymus aktualizacje recznie:

```bash
~/duckdns/duck.sh
```

## 8. Czego nie moge zrobic za Ciebie

- Nie moge kliknac panelu OCI i DuckDNS na twoim koncie.
- Nie moge uruchomic SSH na twoim VPS.
- Nie moge ustawic twoich kluczy SSH i hasel.
