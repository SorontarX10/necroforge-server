# Leaderboard API (MVP)

## Endpoints

- `POST /runs/start`
- `POST /runs/submit`
- `GET /leaderboard?season=global_all_time&page=1&page_size=20`
- `GET /leaderboard/me?season=global_all_time&player_id=...`
- `GET /health`
- `GET /metrics`
- `GET /auth/external/{provider}/start`
- `GET /auth/external/{provider}/callback`
- `POST /auth/external/exchange`
- `POST /auth/external/refresh`
- `POST /auth/external/logout`

## External Account Identity (optional fields)

`POST /runs/start` and `POST /runs/submit` accept optional account identity fields:

- `account_id`
- `account_provider` (for example `google`, `microsoft`, `facebook`, `oidc`)
- `account_provider_user_id`

When provided, backend upserts `user_accounts` and links identity metadata to `players`.

## Local Run

```bash
cd Backend/LeaderboardApi
dotnet run
```

API default URL: `http://localhost:8080`

## Docker Run

Use compose from `Infra/leaderboard`.

## External OAuth Broker Configuration

Set these variables in `Infra/leaderboard/.env` when enabling social login:

- `LEADERBOARD_AUTH_BROKER_ENABLED=true`
- `LEADERBOARD_AUTH_PUBLIC_BASE_URL=https://<your-domain>`
- `LEADERBOARD_AUTH_STATE_SECRET=<long-random-secret>`

Then configure one or more providers:

- Google:
  - `LEADERBOARD_AUTH_GOOGLE_ENABLED=true`
  - `LEADERBOARD_AUTH_GOOGLE_CLIENT_ID=...`
  - `LEADERBOARD_AUTH_GOOGLE_CLIENT_SECRET=...`
  - callback URL in provider console: `https://<your-domain>/auth/external/google/callback`
- Microsoft:
  - `LEADERBOARD_AUTH_MICROSOFT_ENABLED=true`
  - `LEADERBOARD_AUTH_MICROSOFT_TENANT=common` (or tenant id)
  - `LEADERBOARD_AUTH_MICROSOFT_CLIENT_ID=...`
  - `LEADERBOARD_AUTH_MICROSOFT_CLIENT_SECRET=...`
  - callback URL: `https://<your-domain>/auth/external/microsoft/callback`
- Facebook:
  - `LEADERBOARD_AUTH_FACEBOOK_ENABLED=true`
  - `LEADERBOARD_AUTH_FACEBOOK_CLIENT_ID=...`
  - `LEADERBOARD_AUTH_FACEBOOK_CLIENT_SECRET=...`
  - callback URL: `https://<your-domain>/auth/external/facebook/callback`
