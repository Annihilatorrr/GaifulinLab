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
scripts/
└── start-local.ps1                    # единственная команда локального запуска

docker-compose.local.yml               # отдельный Chromium PDF-worker для Visual Studio

deployment/
├── client/                            # PowerShell-команды для управления production
│   ├── common.ps1
│   ├── sync.ps1
│   ├── configure-env.ps1
│   ├── deploy.ps1
│   ├── apply-migration.ps1
│   ├── setup-certificate.ps1
│   └── backup-*.ps1
├── server/                            # Bash-команды, выполняемые на production host
│   ├── common.sh
│   ├── install-host.sh
│   ├── install-nginx.sh
│   ├── migrate.sh
│   ├── deploy.sh
│   ├── reset-database.sh
│   ├── setup-certificate.sh
│   └── backup-*.sh
├── nginx-host/host-reverse-proxy.conf
├── nginx-web/gaifulinlab.conf
├── Dockerfile.api
├── Dockerfile.web
├── Dockerfile.pdf-worker
└── docker-compose.prod-host-nginx.yml
```

`deployment/.env` содержит безопасные placeholders. Перед первым sync замените
пароль БД, JWT key и TLS email реальными значениями. Пользователей и их роли
приложение всегда читает только из Identity-таблиц БД.

## Локальная разработка

API, Web и PostgreSQL запускаются обычным способом из Visual Studio. Docker локально
нужен только для Chromium PDF-worker:

```powershell
.\scripts\start-local.ps1
```

Команда собирает и запускает `pdf-worker`. Он подключается через
`host.docker.internal:5435` к той же локальной PostgreSQL, что API из Visual Studio,
и разделяет с ним каталог `runtime/media`. Вторую БД, migrations, API или Web она не
создаёт и не запускает. Chromium на Windows не требуется: он содержится в Docker image
worker.

### Ссылка регистрации

Ссылка `Register` управляется клиентской настройкой
`Features:RegistrationLinkEnabled` в `src/GaifulinLab.Web/wwwroot/appsettings.json`.
По умолчанию она выключена. Чтобы вернуть её, задайте значение `true` и пересоберите
Web: ссылка появится только для незалогиненных пользователей. Сам маршрут `/register`
и API регистрации доступны по прямому URL независимо от этой настройки.

Миграции локальной БД применяются отдельно, когда это требуется:

```powershell
dotnet tool run dotnet-ef database update `
  --project src/GaifulinLab.Infrastructure/GaifulinLab.Infrastructure.csproj `
  --startup-project src/GaifulinLab.Api/GaifulinLab.Api.csproj
```

Чтобы назначить зарегистрированному пользователю роль `Admin`, выполните
идемпотентный SQL ниже и замените `YOUR-EMAIL@EXAMPLE.COM` на его логин.

```sql
-- Run this once in PostgreSQL after registering the account that should be an administrator.
INSERT INTO "AspNetRoles" ("Id", "Name", "NormalizedName", "ConcurrencyStamp")
SELECT 'admin-role', 'Admin', 'ADMIN', 'admin-role'
WHERE NOT EXISTS (SELECT 1 FROM "AspNetRoles" WHERE "NormalizedName" = 'ADMIN');

INSERT INTO "AspNetUserRoles" ("UserId", "RoleId")
SELECT u."Id", r."Id"
FROM "AspNetUsers" u
JOIN "AspNetRoles" r ON r."NormalizedName" = 'ADMIN'
WHERE u."NormalizedUserName" = UPPER('YOUR-EMAIL@EXAMPLE.COM')
  AND NOT EXISTS (
      SELECT 1 FROM "AspNetUserRoles" ur
      WHERE ur."UserId" = u."Id" AND ur."RoleId" = r."Id");
```

Обычный запуск API не выполняет миграции, сидирование или изменение ролей.

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
   .\deployment\client\apply-migration.ps1 -SkipSync
   .\deployment\client\deploy.ps1 -SkipSync
   ```

### Зачем нужен каждый шаг

- `sync.ps1` только доставляет текущие исходники и production `.env` на сервер.
  Он не собирает и не запускает приложение.
- `server/install-host.sh` устанавливает и настраивает host-зависимости, создаёт
  роль и базу PostgreSQL. Контейнеры приложения этот шаг не запускает.
- `setup-certificate.ps1` получает сертификат Let's Encrypt. Во время первого
  выпуска он использует временную HTTP-конфигурацию nginx для ACME-проверки и
  обновляет серверный `deployment/.env` путями к выданному сертификату.
- `apply-migration.ps1` запускает одноразовый .NET SDK-контейнер и применяет EF
  Core migrations. После завершения контейнер удаляется; API и Web не запускаются.
- `deploy.ps1` выполняет финальный выпуск: собирает образы API, Web и PDF-worker,
  запускает их через Docker Compose, устанавливает постоянную HTTPS-конфигурацию host nginx
  и проверяет `/health/live` и `/health/ready`.

Поэтому финальный `deploy.ps1` обязателен даже после успешной migration: без него
схема БД будет готова, но сайт и API не будут собраны и запущены, а временная
конфигурация nginx не будет заменена production-конфигурацией.

### Что означает `-SkipSync`

По умолчанию `apply-migration.ps1`, `setup-certificate.ps1` и `deploy.ps1` перед
своей основной операцией повторно запускают синхронизацию checkout и локального
`deployment/.env`. Флаг `-SkipSync` пропускает только эту повторную доставку; сама
настройка сертификата, migration или deploy выполняется полностью.

При первом setup исходники уже доставлены командой `sync.ps1`, а настройка
сертификата может обновить серверный `.env`. Поэтому все последующие команды в
этой последовательности запускаются с `-SkipSync`: это быстрее и сохраняет
серверные изменения сертификата.

При обычном выпуске первая команда запускается без `-SkipSync`, чтобы доставить
новый checkout. Следующая команда получает `-SkipSync` и использует уже
синхронизированные файлы:

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
