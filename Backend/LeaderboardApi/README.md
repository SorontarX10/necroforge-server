# Leaderboard API (MVP)

## Endpoints

- `POST /runs/start`
- `POST /runs/submit`
- `GET /leaderboard?season=global_all_time&page=1&page_size=20`
- `GET /leaderboard/me?season=global_all_time&player_id=...`
- `GET /health`
- `GET /metrics`

## Local Run

```bash
cd Backend/LeaderboardApi
dotnet run
```

API default URL: `http://localhost:8080`

## Docker Run

Use compose from `Infra/leaderboard`.
