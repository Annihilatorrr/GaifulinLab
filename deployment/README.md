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

## Локальный экспорт PDF

Chromium устанавливать на Windows не требуется. Запустите отдельный Gotenberg-контейнер:

```powershell
docker compose -f deployment/docker-compose.pdf-dev.yml up -d
```

После этого запускайте API и Web с HTTP-профилями. API обращается к Gotenberg на
`http://localhost:3000`, а контейнер открывает Web через `http://host.docker.internal:5172`.
В production тот же контейнер запускается основным Compose-файлом без публикации порта наружу.

Локальная учётная запись администратора задаётся только в
`appsettings.Development.json`:

- login: `admin`;
- password: configured separately; only its hash is stored in `appsettings.Development.json`.

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
- `deploy.ps1` выполняет финальный выпуск: собирает образы API и Web, запускает
  их через Docker Compose, устанавливает постоянную HTTPS-конфигурацию host nginx
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
