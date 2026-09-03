#!/usr/bin/env bash

# Применить production-миграции EF Core из одноразового контейнера .NET SDK.
# Серверу нужен Docker, но не требуются установленные .NET SDK и dotnet-ef.
# `-e` останавливает скрипт при необработанной ошибке, `-u` запрещает необъявленные
# переменные, а `pipefail` считает конвейер ошибочным при сбое любой его команды.
set -euo pipefail

# Вычислить все пути относительно скрипта, а не текущего каталога пользователя.
# `CDPATH=` не позволяет пользовательской настройке CDPATH изменить вывод `cd`.
SCRIPT_DIR="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
DEPLOYMENT_DIR="$(dirname -- "$SCRIPT_DIR")"
REPOSITORY_DIR="$(dirname -- "$DEPLOYMENT_DIR")"
ENV_FILE="$DEPLOYMENT_DIR/.env"

# Хранить специфичные для проекта значения вместе. Код ниже работает только с
# этими общими настройками и поэтому может оставаться одинаковым в приложениях.
APPLICATION_NAME="GaifulinLab"
MIGRATION_STARTUP_PROJECT_PATH="src/GaifulinLab.Api/GaifulinLab.Api.csproj"
DOTNET_SDK_IMAGE="mcr.microsoft.com/dotnet/sdk:10.0"
DB_HOST_ENV_KEY="GAIFULINLAB_DB_HOST"
DB_PORT_ENV_KEY="GAIFULINLAB_DB_PORT"
DB_NAME_ENV_KEY="GAIFULINLAB_DB_NAME"
DB_USER_ENV_KEY="GAIFULINLAB_DB_USER"
DB_PASSWORD_ENV_KEY="GAIFULINLAB_DB_PASSWORD"
REQUIRES_LICENSE_KEY=0
LICENSE_PRIVATE_KEY_ENV_KEY=""
LICENSE_ACTIVE_KEY_ID_ENV_KEY=""
LICENSE_VALIDITY_DAYS_ENV_KEY=""
DEFAULT_LICENSE_ACTIVE_KEY_ID=""
DEFAULT_LICENSE_VALIDITY_DAYS=0

# `.` — команда Bash `source`: она загружает общую обработку ошибок и безопасные
# функции чтения dotenv в текущий shell, не выполняя deployment/.env как Bash-код.
. "$SCRIPT_DIR/common.sh"

# Сразу завершить работу, если обязательная внешняя команда не найдена в PATH.
# `command -v` ищет программу без запуска; вывод отбрасывается, потому что нужен
# только успешный или неуспешный код завершения.
require_command() {
    local command_name="$1"

    command -v "$command_name" >/dev/null 2>&1 \
        || deployment_fail "Required command '$command_name' is unavailable."
}

# Сразу завершить работу, если отсутствует обязательный обычный файл.
# Первый аргумент — путь, второй — пояснение назначения файла.
require_file() {
    local file="$1"
    local description="$2"

    [[ -f "$file" ]] \
        || deployment_fail "$description is missing: $file"
}

# Прочитать проектные ключи БД в общие переменные миграции. Они экспортируются
# только дочерним процессам скрипта: запуск migrate.sh не может изменить окружение
# родительской SSH-сессии.
load_database_config() {
    MIGRATION_DB_HOST="$(deployment_require_env "$DB_HOST_ENV_KEY" "$ENV_FILE")"
    MIGRATION_DB_PORT="$(deployment_require_env "$DB_PORT_ENV_KEY" "$ENV_FILE")"
    MIGRATION_DB_NAME="$(deployment_require_env "$DB_NAME_ENV_KEY" "$ENV_FILE")"
    MIGRATION_DB_USER="$(deployment_require_env "$DB_USER_ENV_KEY" "$ENV_FILE")"
    MIGRATION_DB_PASSWORD="$(deployment_require_env "$DB_PASSWORD_ENV_KEY" "$ENV_FILE")"

    # Проверить идентификаторы и порт до построения строки подключения Npgsql.
    # `10#` заставляет считать значения с ведущим нулём десятичными.
    [[ "$MIGRATION_DB_NAME" =~ ^[A-Za-z0-9_]+$ ]] \
        || deployment_fail "$DB_NAME_ENV_KEY may contain only letters, numbers, and underscores."
    [[ "$MIGRATION_DB_USER" =~ ^[A-Za-z0-9_]+$ ]] \
        || deployment_fail "$DB_USER_ENV_KEY may contain only letters, numbers, and underscores."
    [[ "$MIGRATION_DB_PORT" =~ ^[0-9]+$ ]] \
        || deployment_fail "Invalid database port in $DB_PORT_ENV_KEY."
    (( 10#$MIGRATION_DB_PORT >= 1 && 10#$MIGRATION_DB_PORT <= 65535 )) \
        || deployment_fail "Invalid database port in $DB_PORT_ENV_KEY."

    export MIGRATION_DB_HOST
    export MIGRATION_DB_PORT
    export MIGRATION_DB_NAME
    export MIGRATION_DB_USER
    export MIGRATION_DB_PASSWORD
}

# Добавить настройки, нужные только приложению, чей startup-проект инициализирует
# лицензирование при создании design-time-сервисов EF Core. Другие проекты оставляют
# `REQUIRES_LICENSE_KEY` равным нулю и полностью пропускают эту ветку.
add_license_arguments() {
    if (( ! REQUIRES_LICENSE_KEY )); then
        return 0
    fi

    MIGRATION_LICENSE_PRIVATE_KEY_PATH="$(deployment_require_env "$LICENSE_PRIVATE_KEY_ENV_KEY" "$ENV_FILE")"
    MIGRATION_LICENSE_ACTIVE_KEY_ID="$(deployment_optional_env "$LICENSE_ACTIVE_KEY_ID_ENV_KEY" "$DEFAULT_LICENSE_ACTIVE_KEY_ID" "$ENV_FILE")"
    MIGRATION_LICENSE_VALIDITY_DAYS="$(deployment_optional_env "$LICENSE_VALIDITY_DAYS_ENV_KEY" "$DEFAULT_LICENSE_VALIDITY_DAYS" "$ENV_FILE")"

    [[ "$MIGRATION_LICENSE_PRIVATE_KEY_PATH" = /* ]] \
        || deployment_fail "$LICENSE_PRIVATE_KEY_ENV_KEY must be an absolute path."
    require_file "$MIGRATION_LICENSE_PRIVATE_KEY_PATH" "Licensing private key"
    [[ "$MIGRATION_LICENSE_VALIDITY_DAYS" =~ ^[0-9]+$ ]] \
        || deployment_fail "Invalid value in $LICENSE_VALIDITY_DAYS_ENV_KEY."
    (( 10#$MIGRATION_LICENSE_VALIDITY_DAYS > 0 )) \
        || deployment_fail "Invalid value in $LICENSE_VALIDITY_DAYS_ENV_KEY."

    export MIGRATION_LICENSE_PRIVATE_KEY_PATH
    export MIGRATION_LICENSE_ACTIVE_KEY_ID
    export MIGRATION_LICENSE_VALIDITY_DAYS

    # Добавление через `+=` сохраняет уже находящиеся в массиве общие аргументы
    # Docker. `--env NAME` передаёт экспортированное значение, не помещая сам
    # секрет в аргументы командной строки Docker.
    DOCKER_ARGUMENTS+=(
        --env MIGRATION_LICENSE_PRIVATE_KEY_PATH
        --env MIGRATION_LICENSE_ACTIVE_KEY_ID
        --env MIGRATION_LICENSE_VALIDITY_DAYS
        --volume "$MIGRATION_LICENSE_PRIVATE_KEY_PATH:$MIGRATION_LICENSE_PRIVATE_KEY_PATH:ro"
    )
}

# Проверить локальные условия до скачивания Docker-образа и записи результатов
# сборки в подключённый репозиторий. Путь проекта считается от `/workspace` внутри
# контейнера и от корня репозитория на сервере.
require_command docker
require_command id
require_file "$ENV_FILE" "Production environment file"
require_file "$REPOSITORY_DIR/$MIGRATION_STARTUP_PROJECT_PATH" "Migration startup project"
docker info >/dev/null 2>&1 \
    || deployment_fail "Docker daemon is unavailable."

load_database_config

# Экспортировать несекретные настройки проекта, чтобы Docker передал их по имени.
# Версии SDK-образа и EF-инструмента зафиксированы для воспроизводимых миграций.
MIGRATION_REQUIRES_LICENSE_KEY="$REQUIRES_LICENSE_KEY"
export MIGRATION_STARTUP_PROJECT_PATH
export MIGRATION_REQUIRES_LICENSE_KEY

# Массив Bash хранит каждый параметр Docker и его значение отдельным аргументом.
# Основные параметры:
# --rm — удалить одноразовый контейнер после завершения;
# --add-host — дать контейнеру доступ к PostgreSQL на Docker-хосте;
# --env-file — передать полную серверную конфигурацию приложения;
# --user — создавать bin/obj с числовым владельцем текущего пользователя сервера;
# --volume/--workdir — подключить checkout как `/workspace` контейнера.
DOCKER_ARGUMENTS=(
    --rm
    --add-host host.docker.internal:host-gateway
    --env-file "$ENV_FILE"
    --user "$(id -u):$(id -g)"
    --env DOTNET_CLI_HOME=/tmp/dotnet-home
    --env NUGET_PACKAGES=/tmp/nuget
    --env MIGRATION_STARTUP_PROJECT_PATH
    --env MIGRATION_REQUIRES_LICENSE_KEY
    --env MIGRATION_DB_HOST
    --env MIGRATION_DB_PORT
    --env MIGRATION_DB_NAME
    --env MIGRATION_DB_USER
    --env MIGRATION_DB_PASSWORD
    --volume "$REPOSITORY_DIR:/workspace"
    --workdir /workspace
)

add_license_arguments

echo "Applying $APPLICATION_NAME production migrations..."

# `"${DOCKER_ARGUMENTS[@]}"` раскрывает массив, не объединяя аргументы с пробелами.
# Программа после `sh -c` заключена в одинарные кавычки, поэтому Bash на сервере
# её не подставляет: переменные будут вычислены позже shell-процессом контейнера.
docker run "${DOCKER_ARGUMENTS[@]}" "$DOTNET_SDK_IMAGE" sh -c '
    set -eu

    : "${MIGRATION_DB_HOST:?required}"
    : "${MIGRATION_DB_PORT:?required}"
    : "${MIGRATION_DB_NAME:?required}"
    : "${MIGRATION_DB_USER:?required}"
    : "${MIGRATION_DB_PASSWORD:?required}"
    : "${MIGRATION_STARTUP_PROJECT_PATH:?required}"

    # ASP.NET заменяет двойное подчёркивание двоеточием конфигурации, поэтому эта
    # переменная становится `ConnectionStrings:Postgres` для startup-проекта EF Core.
    export ConnectionStrings__Postgres="Host=${MIGRATION_DB_HOST};Port=${MIGRATION_DB_PORT};Database=${MIGRATION_DB_NAME};Username=${MIGRATION_DB_USER};Password=${MIGRATION_DB_PASSWORD}"
    export IDENTITY_BOOTSTRAP_ADMIN_LOGIN="${GAIFULINLAB_IDENTITY_BOOTSTRAP_ADMIN_LOGIN:-}"
    export IDENTITY_BOOTSTRAP_ADMIN_PASSWORD_HASH="${GAIFULINLAB_IDENTITY_BOOTSTRAP_ADMIN_PASSWORD_HASH:-}"
    TAXONOMY_BOOTSTRAP_TOPICS_JSON="${GAIFULINLAB_TAXONOMY_BOOTSTRAP_TOPICS_JSON:-}"

    if [ -n "${GAIFULINLAB_TAXONOMY_BOOTSTRAP_TOPICS_FILE:-}" ]; then
        case "$GAIFULINLAB_TAXONOMY_BOOTSTRAP_TOPICS_FILE" in
            /*|*..*)
                echo "GAIFULINLAB_TAXONOMY_BOOTSTRAP_TOPICS_FILE must be a relative path inside the repository." >&2
                exit 1
                ;;
        esac

        if [ -n "$TAXONOMY_BOOTSTRAP_TOPICS_JSON" ]; then
            echo "Configure either GAIFULINLAB_TAXONOMY_BOOTSTRAP_TOPICS_JSON or GAIFULINLAB_TAXONOMY_BOOTSTRAP_TOPICS_FILE, not both." >&2
            exit 1
        fi

        if [ ! -f "$GAIFULINLAB_TAXONOMY_BOOTSTRAP_TOPICS_FILE" ]; then
            echo "Taxonomy bootstrap file was not found: $GAIFULINLAB_TAXONOMY_BOOTSTRAP_TOPICS_FILE" >&2
            exit 1
        fi

        export TAXONOMY_BOOTSTRAP_TOPICS_JSON="$(cat "$GAIFULINLAB_TAXONOMY_BOOTSTRAP_TOPICS_FILE")"
    else
        export TAXONOMY_BOOTSTRAP_TOPICS_JSON
    fi

    if [ "${MIGRATION_REQUIRES_LICENSE_KEY:-0}" = 1 ]; then
        : "${MIGRATION_LICENSE_PRIVATE_KEY_PATH:?required}"
        : "${MIGRATION_LICENSE_ACTIVE_KEY_ID:?required}"
        : "${MIGRATION_LICENSE_VALIDITY_DAYS:?required}"
        export Licensing__PrivateKeyPath="$MIGRATION_LICENSE_PRIVATE_KEY_PATH"
        export Licensing__ActiveKeyId="$MIGRATION_LICENSE_ACTIVE_KEY_ID"
        export Licensing__CertificateValidityDays="$MIGRATION_LICENSE_VALIDITY_DAYS"
    fi

    # Установить зафиксированную версию инструмента во временную файловую систему.
    # Restore и build выполняются явно один раз; затем `--no-build` заставляет EF
    # использовать именно полученный результат сборки.
    dotnet restore "$MIGRATION_STARTUP_PROJECT_PATH"
    dotnet build "$MIGRATION_STARTUP_PROJECT_PATH" --no-restore
    dotnet run \
        --no-build \
        --project "$MIGRATION_STARTUP_PROJECT_PATH" \
        -- --migrate-and-bootstrap
'

echo "$APPLICATION_NAME production migrations completed."
