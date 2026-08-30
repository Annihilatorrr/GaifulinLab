# Raspberry Pi deployment

The compose stack runs PostgreSQL, the ASP.NET Core application and an Nginx reverse proxy. Both the database and uploaded images use named Docker volumes. The application waits for PostgreSQL, applies EF Core migrations and becomes ready only after the database is reachable.

The images used by the stack are multi-platform and can be built on a 64-bit Raspberry Pi OS (`linux/arm64`).

## First deployment

1. Install Docker Engine with the Compose plugin on the Raspberry Pi.
2. Clone the repository and enter its root directory.
3. Copy `.env.example` to `.env` and replace every secret value.
4. Build the image once, then generate the administrator password hash:

   ```sh
   docker compose build app
   read -rsp "Admin password: " ADMIN_PASSWORD; echo
   export ADMIN_PASSWORD
   docker compose run --rm --no-deps app --hash-admin-password
   unset ADMIN_PASSWORD
   ```

5. Put the printed value into `ADMIN_PASSWORD_HASH` in `.env`.
6. Deploy:

   ```sh
   chmod +x deployment/deploy.sh
   ./deployment/deploy.sh
   ```

By default Nginx listens on `127.0.0.1:8080`. This is intended for a host-level TLS reverse proxy. Set `HTTP_BIND_ADDRESS=0.0.0.0` only when the service must be reachable directly from the LAN. Do not expose the admin login over unencrypted public HTTP.

The host reverse proxy should forward requests to `http://127.0.0.1:8080` and preserve `Host`, `X-Forwarded-For` and `X-Forwarded-Proto`.

## Operations

Check the stack and health endpoints:

```sh
docker compose ps
curl --fail http://127.0.0.1:8080/health/live
curl --fail http://127.0.0.1:8080/health/ready
```

View application logs:

```sh
docker compose logs --follow app
```

Redeploy after pulling changes:

```sh
./deployment/deploy.sh
```

The `postgres_data` and `media_data` volumes survive container replacement. Back them up before destructive Docker maintenance or schema changes.
