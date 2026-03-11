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

## Krok 2: Zainstaluj Git + Docker + Compose + cron

```bash
sudo apt update
sudo apt install -y ca-certificates curl gnupg cron git openssh-client
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

## Krok 3: Skonfiguruj SSH do GitHub na VPS

Wygeneruj nowy klucz tylko dla tego VPS:

```bash
mkdir -p ~/.ssh
chmod 700 ~/.ssh
ssh-keygen -t ed25519 -C "necroforge-vps-2026-03-08" -f ~/.ssh/necroforge_github -N ""
cat ~/.ssh/necroforge_github.pub
```

Skopiuj output `cat ~/.ssh/necroforge_github.pub`, potem dodaj go na GitHub:

1. Wejdz w repo `SorontarX10/necroforge`.
2. `Settings` -> `Deploy keys` -> `Add deploy key`.
3. Title: np. `necroforge-vps`.
4. Key: wklej caly public key.
5. `Allow write access` wlacz tylko jesli chcesz robic `git push` z VPS.

Dodaj konfiguracje SSH i zaufany host GitHuba:

```bash
cat > ~/.ssh/config <<'EOF'
Host github.com
  HostName github.com
  User git
  IdentityFile ~/.ssh/necroforge_github
  IdentitiesOnly yes
EOF
chmod 600 ~/.ssh/config
ssh-keyscan github.com >> ~/.ssh/known_hosts
chmod 644 ~/.ssh/known_hosts
```

Test polaczenia:

```bash
ssh -T git@github.com
```

Poprawny wynik bedzie w stylu:
- `Hi USERNAME! You've successfully authenticated, but GitHub does not provide shell access.`
- albo dla deploy key: `Hi USERNAME/REPO! You've successfully authenticated, but GitHub does not provide shell access.`

## 3. DuckDNS (darmowa subdomena)

## Krok 1: Utworz subdomene

1. Wejdz na `https://www.duckdns.org/` i zaloguj sie.
2. Wpisz nazwe subdomeny, np. `necroforge-lb`.
3. Kliknij add domain.
4. Skopiuj `token` z panelu DuckDNS.

Twoja domena bedzie: `necroforge-lb.duckdns.org`

## Krok 2: Skonfiguruj auto-update IP na VPS

```bash
git clone git@github.com:SorontarX10/necroforge.git
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
- `LEADERBOARD_ADMIN_API_KEY` -> dlugi losowy token do endpointow admin (`/admin/*`)

Uwaga:
- Jesli haslo zawiera znaki specjalne typu `;`, obecny deploy juz to obsluguje poprawnie.
- Jesli w `.env` haslo zawiera `$` albo `#`, wpisz je w pojedynczych cudzyslowach, np. `POSTGRES_PASSWORD='abc$123#xyz'`.
- Jesli zmienisz `POSTGRES_DB` / `POSTGRES_USER` / `POSTGRES_PASSWORD` po pierwszym starcie, sam `.env` nie zaktualizuje danych w istniejacym wolumenie Postgresa.

## Krok 1b: Konfiguracja brokera OAuth (Google/Microsoft/Facebook/Steam)

W tym samym `Infra/leaderboard/.env` ustaw:

- `LEADERBOARD_AUTH_BROKER_ENABLED=true`
- `LEADERBOARD_AUTH_PUBLIC_BASE_URL=https://necroforge-lb.duckdns.org`
- `LEADERBOARD_AUTH_STATE_SECRET=<dlugi_losowy_sekret>`

Nastepnie wlacz i uzupelnij minimum jednego dostawce:

- Google:
  - `LEADERBOARD_AUTH_GOOGLE_ENABLED=true`
  - `LEADERBOARD_AUTH_GOOGLE_CLIENT_ID=...`
  - `LEADERBOARD_AUTH_GOOGLE_CLIENT_SECRET=...`
  - callback w panelu Google: `https://necroforge-lb.duckdns.org/auth/external/google/callback`
- Microsoft:
  - `LEADERBOARD_AUTH_MICROSOFT_ENABLED=true`
  - `LEADERBOARD_AUTH_MICROSOFT_TENANT=common` (albo tenant id)
  - `LEADERBOARD_AUTH_MICROSOFT_CLIENT_ID=...`
  - `LEADERBOARD_AUTH_MICROSOFT_CLIENT_SECRET=...`
  - callback: `https://necroforge-lb.duckdns.org/auth/external/microsoft/callback`
- Facebook:
  - `LEADERBOARD_AUTH_FACEBOOK_ENABLED=true`
  - `LEADERBOARD_AUTH_FACEBOOK_CLIENT_ID=...`
  - `LEADERBOARD_AUTH_FACEBOOK_CLIENT_SECRET=...`
  - callback: `https://necroforge-lb.duckdns.org/auth/external/facebook/callback`
- Steam (auto-login z klienta Steam + walidacja ticketu po stronie API):
  - `LEADERBOARD_AUTH_STEAM_ENABLED=true`
  - `LEADERBOARD_AUTH_STEAM_APP_ID=<twoje_steam_app_id>`
  - `LEADERBOARD_AUTH_STEAM_WEB_API_KEY=<publisher_web_api_key>`

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

## Krok 4: Health check legal docs (statyczne HTML)

Po deployu sprawdz, czy Caddy serwuje legal strony:

```bash
curl -I https://necroforge-lb.duckdns.org/legal/privacy-policy.html
curl -I https://necroforge-lb.duckdns.org/legal/eula.html
curl -I https://necroforge-lb.duckdns.org/legal/third-party-licenses.html
```

Kazdy URL powinien zwrocic `HTTP/2 200`.

## 5. Unity i build demo

## Krok 1: Ustaw backend URL w grze

Edytuj:
- `Assets/StreamingAssets/leaderboard_config.json`

Wstaw:

```json
{
  "base_url": "https://necroforge-lb.duckdns.org",
  "season": "global_all_time",
  "version_lock": "0.7.2",
  "timeout_seconds": 8,
  "read_retry_count": 1,
  "submit_retry_count": 0,
  "retry_budget_seconds": 6,
  "retry_backoff_seconds": 0.35
}
```

Po tej konfiguracji klient uzywa flow automatycznego (`flow/start` + polling `flow/{id}/session`), bez recznego kopiowania URL callback z przegladarki.

`version_lock` ustaw na te sama wartosc co `LEADERBOARD_VERSION_LOCK` w `.env` backendu.

## Krok 1b: Ustaw URL brokera auth w grze

Edytuj:
- `Assets/StreamingAssets/auth_config.json`

Ustaw:

```json
{
  "enabled": true,
  "broker_base_url": "https://necroforge-lb.duckdns.org",
  "google_login_enabled": true,
  "microsoft_login_enabled": false,
  "facebook_login_enabled": false,
  "provider_start_path_template": "/auth/external/{provider}/start",
  "flow_start_path_template": "/auth/external/{provider}/flow/start",
  "flow_session_path_template": "/auth/external/flow/{flow_id}/session",
  "steam_session_exchange_path": "/auth/external/steam/session",
  "exchange_path": "/auth/external/exchange",
  "refresh_path": "/auth/external/refresh",
  "logout_path": "/auth/external/logout",
  "steam_auto_sign_in_enabled": false,
  "flow_poll_interval_seconds": 0.5
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

Rozszerzony security smoke (w tym stale nonce po przekroczeniu TTL sesji runu):

```powershell
.\Tools\leaderboard\security_smoke_test.ps1 -BaseUrl "https://necroforge-lb.duckdns.org" -BuildVersion "0.7.2" -StaleNonceDelaySeconds 305 -RequireStaleNonce
```

Jesli masz ustawione `LEADERBOARD_VERSION_LOCK`, uruchom smoke test z pasujacym buildem:

```powershell
.\Tools\leaderboard\smoke_test.ps1 -BaseUrl "https://necroforge-lb.duckdns.org" -BuildVersion "0.7.2"
```

Domyslne `BuildVersion = "smoke"` moze wpasc w `manual_review` przez `build_version_mismatch`, co jest oczekiwane zachowanie backendu.

Test integracyjny API (`start -> submit -> leaderboard/me -> leaderboard top`):

```powershell
.\Tools\leaderboard\integration_api_test.ps1 -BaseUrl "https://necroforge-lb.duckdns.org" -BuildVersion "0.7.2"
```

Skrypt konczy sie bledem, jesli submit nie jest `accepted` albo ranking nie zwroci wpisu.

Legal URL smoke (`HTTPS + redirect + brak 404`):

```powershell
.\Tools\leaderboard\legal_links_check.ps1 -BaseUrl "https://necroforge-lb.duckdns.org"
```

Komendy admin do recznego review flagowanych runow:

```powershell
$token = "TU_WSTAW_LEADERBOARD_ADMIN_API_KEY"
.\Tools\leaderboard\admin_review.ps1 -BaseUrl "https://necroforge-lb.duckdns.org" -AdminToken $token -Mode list -State all_flagged
.\Tools\leaderboard\admin_review.ps1 -BaseUrl "https://necroforge-lb.duckdns.org" -AdminToken $token -Mode review -RunId "RUN_UUID" -Action accept -Note "manual review approved"
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

## Problem: `password authentication failed for user "leaderboard"`

Najczestsze przyczyny:
1. Haslo w `.env` zostalo zmienione po pierwszym starcie DB, ale wolumen `leaderboard_pgdata` nadal trzyma stare konto/haslo.
2. Wczesniejszy deploy skladal connection string z jednego stringa i mogl sie wysypac przy niektorych znakach specjalnych w hasle.

Szybka naprawa przy swiezej instalacji:

```bash
cd ~/necroforge/Infra/leaderboard
docker compose down -v
docker compose up -d --build
```

Jesli chcesz zachowac dane, nie usuwaj wolumenu. Zamiast tego ustaw haslo wewnatrz Postgresa na wartosc z `.env`.

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
