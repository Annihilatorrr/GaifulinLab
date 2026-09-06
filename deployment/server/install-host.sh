#!/usr/bin/env bash

# Подготовить Linux-сервер для приложения: установить нужные пакеты, настроить
# Docker, создать или исправить роль PostgreSQL, базу данных и правило доступа из
# контейнеров. Операции идемпотентны: повторный запуск сохраняет имеющиеся данные.
# `-e` останавливает скрипт при необработанной ошибке, `-u` запрещает необъявленные
# переменные, а `pipefail` считает конвейер ошибочным при сбое любой его команды.
set -euo pipefail

# Вычислить пути относительно самого скрипта, а не текущего каталога пользователя.
# `CDPATH=` не позволяет пользовательской настройке CDPATH добавить неожиданный
# текст в вывод `cd` и тем самым испортить вычисленный путь.
SCRIPT_DIR="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
DEPLOYMENT_DIR="$(dirname -- "$SCRIPT_DIR")"
ENV_FILE="$DEPLOYMENT_DIR/.env"

# Хранить специфичные для проекта имена в одном месте. Код ниже использует эти
# переменные и поэтому может оставаться одинаковым в обоих проектах.
APPLICATION_NAME="GaifulinLab"
ENV_TEMPLATE_FILE="$DEPLOYMENT_DIR/.env.example"
DB_NAME_ENV_KEY="GAIFULINLAB_DB_NAME"
DB_USER_ENV_KEY="GAIFULINLAB_DB_USER"
DB_PASSWORD_ENV_KEY="GAIFULINLAB_DB_PASSWORD"
DB_PORT_ENV_KEY="GAIFULINLAB_DB_PORT"

# `.` — команда Bash `source`: она загружает общие функции `deployment_*`
# в текущий shell, не запуская отдельный процесс.
. "$SCRIPT_DIR/common.sh"

# Массивы сохраняют каждое имя пакета отдельным аргументом при раскрытии через
# `"${ARRAY[@]}"`. Пакеты Docker вынесены отдельно, потому что уже установленный
# Docker нельзя без необходимости заменять пакетами из дистрибутива.
BASE_PACKAGES=(git openssl curl nginx postgresql postgresql-client)
DOCKER_PACKAGES=(docker.io docker-compose-v2)
POSTGRES_HBA_RANGE="172.16.0.0/12"
POSTGRES_RESTART_REQUIRED=false

# Потребовать серверный dotenv-файл до изменения установленных пакетов.
# Проект с файлом-примером может создать первоначальную копию. Пустое значение
# шаблона означает, что `.env` нужно загрузить через client/configure-env.ps1.
ensure_env_file() {
    if [[ -f "$ENV_FILE" ]]; then
        return 0
    fi

    if [[ -n "$ENV_TEMPLATE_FILE" ]]; then
        [[ -f "$ENV_TEMPLATE_FILE" ]] \
            || deployment_fail "Required environment template '$ENV_TEMPLATE_FILE' is missing."

        cp "$ENV_TEMPLATE_FILE" "$ENV_FILE"
        chmod 600 "$ENV_FILE"
        deployment_fail "Created $ENV_FILE. Replace placeholders, then rerun install-host.sh."
    fi

    deployment_fail "Configure $ENV_FILE with deployment/client/configure-env.ps1, then rerun install-host.sh."
}

# Установить только те пакеты, которые dpkg ещё не считает установленными.
# `"$@"` означает все аргументы функции. Копирование в массив сохраняет границы
# имён и позволяет одной командой apt установить все отсутствующие пакеты.
ensure_package_list() {
    local requested_packages=("$@")
    local missing_packages=()
    local package_name

    for package_name in "${requested_packages[@]}"; do
        if ! dpkg -s "$package_name" >/dev/null 2>&1; then
            missing_packages+=("$package_name")
        fi
    done

    # `${#array[@]}` возвращает число элементов массива. Если массив пуст, apt не
    # запускается: повторный запуск быстрее и не обращается к репозиториям зря.
    if (( ${#missing_packages[@]} == 0 )); then
        return 0
    fi

    sudo apt-get update
    sudo DEBIAN_FRONTEND=noninteractive apt-get install -y "${missing_packages[@]}"
}

# Установить и запустить Docker, только если команда `docker` ещё недоступна.
# Так Docker CE или другая установка администратора не будет заменена пакетом
# `docker.io` из Debian или Ubuntu.
ensure_docker_runtime() {
    local current_user

    if command -v docker >/dev/null 2>&1; then
        return 0
    fi

    ensure_package_list "${DOCKER_PACKAGES[@]}"
    sudo systemctl enable --now docker

    # `id -un` получает имя запустившей скрипт учётной записи без зависимости от
    # переменной `$USER`. Новое членство в группе действует со следующего входа,
    # но не изменяет уже открытый shell.
    current_user="$(id -un)"
    sudo usermod -aG docker "$current_user"
    echo "Docker group membership added for $current_user. Log out and in before deployment."
}

# Прочитать настройки БД из `.env` как обычные данные и проверить их до изменений
# apt, PostgreSQL или systemd. Имена ключей берутся из блока проекта выше, поэтому
# эту функцию не приходится дублировать для каждого приложения.
load_and_validate_database_config() {
    DB_NAME="$(deployment_require_env "$DB_NAME_ENV_KEY" "$ENV_FILE")"
    DB_USER="$(deployment_require_env "$DB_USER_ENV_KEY" "$ENV_FILE")"
    DB_PASSWORD="$(deployment_require_env "$DB_PASSWORD_ENV_KEY" "$ENV_FILE")"
    DB_PORT="$(deployment_require_env "$DB_PORT_ENV_KEY" "$ENV_FILE")"

    # Идентификаторы PostgreSQL ограничиваются до попадания в динамический SQL.
    # Пароль здесь не ограничивается: ниже psql безопасно заключает его как литерал.
    [[ "$DB_NAME" =~ ^[A-Za-z0-9_]+$ ]] \
        || deployment_fail "$DB_NAME_ENV_KEY may contain only letters, numbers, and underscores."
    [[ "$DB_USER" =~ ^[A-Za-z0-9_]+$ ]] \
        || deployment_fail "$DB_USER_ENV_KEY may contain only letters, numbers, and underscores."
    [[ "$DB_PORT" =~ ^[0-9]+$ ]] \
        || deployment_fail "Invalid database port in $DB_PORT_ENV_KEY."

    # Префикс `10#` заставляет считать значение вроде 05432 десятичным, а не
    # восьмеричным числом Bash.
    (( 10#$DB_PORT >= 1 && 10#$DB_PORT <= 65535 )) \
        || deployment_fail "Invalid database port in $DB_PORT_ENV_KEY."
}

# Создать отсутствующие объекты PostgreSQL и при каждом запуске обновить их
# изменяемые настройки. Кавычки в heredoc `<<'SQL'` запрещают подстановки Bash
# внутри SQL. Значения передаются как переменные psql; `format` в PostgreSQL
# экранирует идентификаторы через `%I`, литералы через `%L`, после чего `\gexec`
# выполняет сгенерированную команду.
ensure_postgres_objects() {
    sudo -u postgres psql --set=ON_ERROR_STOP=1 \
        --set=db_name="$DB_NAME" \
        --set=db_user="$DB_USER" \
        --set=db_password="$DB_PASSWORD" <<'SQL'
-- Создать роль с правом входа, только если её ещё нет.
SELECT format('CREATE ROLE %I LOGIN PASSWORD %L', :'db_user', :'db_password')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = :'db_user') \gexec

-- Синхронизировать пароль роли с серверным dotenv-файлом.
SELECT format('ALTER ROLE %I WITH LOGIN PASSWORD %L', :'db_user', :'db_password') \gexec

-- Создать отсутствующую базу и назначить ожидаемого владельца.
SELECT format('CREATE DATABASE %I OWNER %I', :'db_name', :'db_user')
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = :'db_name') \gexec
SELECT format('ALTER DATABASE %I OWNER TO %I', :'db_name', :'db_user') \gexec

SQL
}

# Контейнеры обращаются к PostgreSQL через адрес хоста, поэтому localhost
# недостаточно. Изменение listen_addresses применяется только после restart;
# на shared-host не выполняем его, если PostgreSQL уже принимает подключения
# со всех адресов.
ensure_postgres_listen_addresses() {
    local current_listen_addresses

    current_listen_addresses="$(sudo -u postgres psql --tuples-only --no-align --command='show listen_addresses;')"
    if [[ "$current_listen_addresses" = '*' ]]; then
        return 0
    fi

    sudo -u postgres psql --set=ON_ERROR_STOP=1 \
        --command="ALTER SYSTEM SET listen_addresses = '*';"
    POSTGRES_RESTART_REQUIRED=true
}

# Разрешить доступ из стандартного приватного диапазона Docker только этой роли
# и к этой базе. PostgreSQL сам сообщает путь активного pg_hba.conf, поэтому в
# файловом пути не нужно жёстко фиксировать версию, например 15 или 17.
ensure_hba_bridge_access() {
    local pg_hba_file
    local rule

    pg_hba_file="$(sudo -u postgres psql --tuples-only --no-align --command='show hba_file;')"
    rule="host ${DB_NAME} ${DB_USER} ${POSTGRES_HBA_RANGE} scram-sha-256"

    # `grep -Fqx` без вывода ищет точное совпадение всей строки как обычного текста.
    # Если правила нет, `sudo tee -a` дописывает его с правами администратора.
    if ! sudo grep -Fqx "$rule" "$pg_hba_file"; then
        printf '\n%s\n' "$rule" | sudo tee -a "$pg_hba_file" >/dev/null
    fi
}

# После перезапуска PostgreSQL проверить новые реквизиты через TCP.
# Запись `PGPASSWORD=...` непосредственно перед psql передаёт пароль только этому
# процессу и не экспортирует его для всех последующих команд текущего shell.
verify_database_connection() {
    PGPASSWORD="$DB_PASSWORD" psql --no-password \
        --host=127.0.0.1 \
        --port="$DB_PORT" \
        --dbname="$DB_NAME" \
        --username="$DB_USER" \
        --set=ON_ERROR_STOP=1 \
        --command='select 1;'
}

# Проверить конфигурацию до внесения системных изменений.
echo "Preparing $APPLICATION_NAME host setup..."
ensure_env_file
load_and_validate_database_config

# Установить системные инструменты, сохранив имеющуюся сборку Docker.
echo "Installing host packages..."
ensure_package_list "${BASE_PACKAGES[@]}"
ensure_docker_runtime
docker compose version >/dev/null 2>&1 \
    || deployment_fail "Docker Compose plugin is unavailable."

# Идемпотентно подготовить PostgreSQL. Изменение pg_hba.conf применяется через
# reload и не разрывает подключения соседних проектов. Restart нужен только
# новому host, на котором listen_addresses ещё не равен '*'.
echo "Creating or updating the $APPLICATION_NAME PostgreSQL role and database..."
ensure_postgres_objects
ensure_postgres_listen_addresses
ensure_hba_bridge_access
sudo systemctl enable --now postgresql

if [[ "$POSTGRES_RESTART_REQUIRED" = true ]]; then
    sudo systemctl restart postgresql
else
    sudo systemctl reload postgresql
fi

verify_database_connection

echo "$APPLICATION_NAME host dependencies and database are ready."
