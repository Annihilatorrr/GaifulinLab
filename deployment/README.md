# GaifulinLab deployment

Deployment повторяет схему NeonKickWeb: клиентские PowerShell-скрипты доставляют
checkout по SSH, серверные Bash-скрипты настраивают host PostgreSQL/nginx и
запускают приложение через Docker Compose.

## Target

- user: `v3rt3x`;
- host: `192.168.50.11`;
- checkout: `/home/v3rt3x/deployments/gaifulinlab`;
- SSH key: `C:\Users\pwrfl\.ssh\rbpi0807`;
- production URL: `https://gaifulinlab.com`.

## Структура

```text
deployment/
├── client/
│   ├── common.ps1
│   ├── create_local_db_in_docker.ps1
│   ├── sync.ps1
│   ├── configure-env.ps1
│   ├── deploy.ps1
│   ├── apply-migration.ps1
│   └── setup-certificate.ps1
├── server/
│   ├── common.sh
│   ├── install-host.sh
│   ├── install-nginx.sh
│   ├── migrate.sh
│   ├── deploy.sh
│   ├── reset-database.sh
│   └── setup-certificate.sh
├── nginx-host/host-reverse-proxy.conf
├── nginx-web/gaifulinlab.conf
├── Dockerfile.api
├── Dockerfile.web
└── docker-compose.prod-host-nginx.yml
```

`deployment/.env` содержит безопасные placeholders. Перед первым sync замените
пароль БД, hash пароля администратора, JWT key и TLS email реальными значениями.

## Локальная PostgreSQL

Создать локальный PostgreSQL-контейнер:

```powershell
.\deployment\client\create_local_db_in_docker.ps1
```

Скрипт выполняет один `docker run` и сразу сообщает об ошибке. Параметры совпадают
с `appsettings.Development.json`: база, пользователь и пароль — `gaifulinlab`,
endpoint — `localhost:5435`. Данные сохраняются в Docker volume
`gaifulinlab-postgres-dev-data`.

Локальная учётная запись администратора задаётся только в
`appsettings.Development.json`:

- login: `admin`;
- password: `gaifulinlab-dev-admin`.

Применить migrations, используя connection string из
`appsettings.Development.json`:

```powershell
dotnet tool restore
dotnet ef database update `
  --project src/GaifulinLab.Infrastructure/GaifulinLab.Infrastructure.csproj `
  --startup-project src/GaifulinLab.Api/GaifulinLab.Api.csproj
```

## Первый production setup

DNS `gaifulinlab.com` и `www.gaifulinlab.com` должен указывать на сервер, а
TCP 80/443 должны быть доступны из интернета.

1. Настройте `deployment/.env` и доставьте checkout:

   ```powershell
   .\deployment\client\sync.ps1
   ```

2. Один раз подготовьте host:

   ```bash
   cd /home/v3rt3x/deployments/gaifulinlab
   ./deployment/server/install-host.sh
   ```

3. Получите сертификат:

   ```powershell
   .\deployment\client\setup-certificate.ps1 -SkipSync
   ```

4. Примените migration и разверните приложение:

   ```powershell
   .\deployment\client\apply-migration.ps1
   .\deployment\client\deploy.ps1 -SkipSync
   ```

## Обычный выпуск

Без изменений схемы БД:

```powershell
.\deployment\client\deploy.ps1
```

С migrations:

```powershell
.\deployment\client\apply-migration.ps1
.\deployment\client\deploy.ps1 -SkipSync
```

`server/deploy.sh` при каждом deploy выполняет
`ALTER ROLE ... WITH LOGIN PASSWORD ...`, используя
`GAIFULINLAB_DB_PASSWORD` из `deployment/.env`, и проверяет подключение из
одноразового PostgreSQL-контейнера до пересборки приложения. Данные PostgreSQL
при этом не удаляются.

`server/reset-database.sh --confirm-reset-gaifulinlab` — отдельная разрушительная
аварийная команда. Обычный deploy её никогда не вызывает.

## Диагностика

```bash
docker compose -p gaifulinlab \
  --env-file deployment/.env \
  -f deployment/docker-compose.prod-host-nginx.yml ps

curl --resolve gaifulinlab.com:443:127.0.0.1 https://gaifulinlab.com/health/ready
sudo nginx -t
```
