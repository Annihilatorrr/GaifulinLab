# GaifulinLab deployment

Deployment повторяет схему NeonKickWeb: клиентские PowerShell-скрипты доставляют
checkout по SSH, серверные Bash-скрипты настраивают host PostgreSQL/nginx и
запускают приложение через Docker Compose.

## Target

- user: `ruslan`;
- host: `192.168.50.142`;
- checkout: `/home/ruslan/deployments/gaifulinlab`;
- SSH key: `C:\Users\pwrfl\.ssh\id_ed25519_192_168_50_142`;
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

## Как контейнеры подключаются к PostgreSQL на host

В production PostgreSQL установлен непосредственно на Linux-сервере, а API и
PDF-worker работают в отдельных Docker-контейнерах. У каждого контейнера свой
сетевой namespace, поэтому `localhost` внутри API означает сам API-контейнер, а
не сервер `192.168.50.142`. PostgreSQL внутри API-контейнера не запущен.

Compose добавляет API и PDF-worker дополнительную запись:

```yaml
extra_hosts:
  - "host.docker.internal:host-gateway"
```

`extra_hosts` дополняет автоматически созданный Docker файл `/etc/hosts` внутри
контейнера. Слева указано выбранное нами имя `host.docker.internal`, а справа —
специальное значение Docker `host-gateway`. При создании контейнера Docker
заменяет его реальным gateway IP хоста. Итоговая запись выглядит примерно так:

```text
172.17.0.1  host.docker.internal
```

Это имя не является публичным DNS и существует только внутри соответствующего
контейнера. Сам маршрут подключения выглядит так:

```text
API или PDF-worker
  -> host.docker.internal:5432
  -> gateway IP Linux-хоста в Docker-сети
  -> PostgreSQL, установленный на Linux-хосте
```

`API или PDF-worker` в этой схеме — только подпись источника подключения. Другие
контейнеры в одной Compose-сети используют имя сервиса `api`, например web-nginx
отправляет HTTP-запросы на `http://api:8080`; к адресу PostgreSQL это имя отношения
не имеет.

Доступ к host PostgreSQL регулируют два независимых уровня:

1. `listen_addresses` определяет, на каких интерфейсах PostgreSQL открывает порт
   `5432`. Значение `localhost` оставляет только loopback, а `'*'` включает также
   Docker bridge и LAN. Системный слушатель `0.0.0.0:5432` означает все IPv4-интерфейсы.
2. `pg_hba.conf` определяет, каким ролям, к каким базам и из каких сетей разрешена
   аутентификация. Поэтому `listen_addresses='*'` не предоставляет доступ само по
   себе: для GaifulinLab добавляется отдельное правило только для роли и базы
   `gaifulinlab` из приватного диапазона Docker.

`pg_hba.conf` можно перечитать командой `systemctl reload postgresql`: сервер не
останавливается, активные подключения соседних проектов сохраняются. Изменение
`listen_addresses` требует `systemctl restart postgresql`, потому что сетевые
listen-сокеты создаются при старте процесса. Поэтому `install-host.sh` сначала
выполняет `show listen_addresses`; если значение уже равно `'*'`, он не повторяет
`ALTER SYSTEM` и ограничивается reload. Restart выполняется только после реального
изменения этой startup-настройки.

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

### Ссылки входа и регистрации

Ссылки `Sign in` и `Register` управляются клиентскими настройками
`Features:SignInLinkEnabled` и `Features:RegistrationLinkEnabled` соответственно в
`src/GaifulinLab.Web/wwwroot/appsettings.json`. По умолчанию они выключены. Чтобы вернуть
нужную ссылку, задайте для неё значение `true` и пересоберите Web: ссылка появится только
для незалогиненных пользователей. Сами маршруты `/admin/login` и `/register`, а также API
аутентификации доступны по прямому URL независимо от этих настроек.

### Публичное имя автора

`Display name` задаётся при регистрации и показывается у опубликованных статей вместо
логина или email. Его можно изменить после входа в workspace на странице
`/admin/profile`; новое имя сразу применяется ко всем статьям автора. После миграции
существующие аккаунты получают безопасное нейтральное имя `Author`, которое следует
заменить через профиль.

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
   cd /home/ruslan/deployments/gaifulinlab
   ./deployment/server/install-host.sh
   ```

   На уже используемом сервере скрипт применяет правило доступа PostgreSQL через
   `reload`, не прерывая соединения соседних проектов. Полный restart PostgreSQL
   нужен только на новом host, если ему требуется включить приём подключений не
   только на localhost.

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
