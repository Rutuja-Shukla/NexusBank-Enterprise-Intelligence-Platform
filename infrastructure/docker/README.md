# Container assets

The runnable stack lives at the repository root:

- `docker-compose.yml` — SQL Server 2022, Redis 7, Kafka 3.7 (KRaft), API, console.
- `backend/Dockerfile` — multi-stage .NET 8 build; runtime image runs as the non-root `APP_UID`
  with a `/health/live` healthcheck.
- `frontend/Dockerfile` — Node 22 build, nginx 1.27 runtime serving the immutable assets.
- `frontend/nginx.conf` — SPA fallback, gzip, security headers and reverse proxy for `/api`,
  `/health`, `/metrics` and `/swagger`.

## Run

```bash
cp .env.example .env      # MSSQL_SA_PASSWORD + JWT_SIGNING_KEY
docker compose up --build
```

- Console: http://localhost:8088
- API/Swagger: http://localhost:8080/swagger
- Health: http://localhost:8080/health

## Notes

- SQL Server needs ~2 GB RAM; give Docker Desktop at least 6 GB.
- Kafka takes ~30 s to become ready; the API starts without it and reports the in-process fallback
  until the broker is reachable.
- Data lives in named volumes (`sqlserver-data`, `redis-data`, `kafka-data`). `docker compose down -v`
  removes them and reseeds on the next start.
- These files were authored but **not executed** in the development environment because Docker was
  unavailable; see `docs/DEPLOYMENT.md` for the verification steps to run first.
