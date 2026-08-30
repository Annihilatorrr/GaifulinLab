#!/usr/bin/env bash

# ОПАСНО: безвозвратно удалить и заново создать явно указанную production-базу.
# Обычные скрипты установки, миграции и деплоя никогда не вызывают этот файл.
# Он предназначен только для осознанного аварийного сброса с последующим повторным
# применением миграций. `-e` останавливает скрипт при необработанной ошибке, `-u`
# запрещает необъявленные переменные, а `pipefail` учитывает сбой команды в конвейере.
set -euo pipefail

# Вычислить путь dotenv относительно скрипта. Его намеренно нельзя переопределить
# через ENV_FILE: переменная окружения не должна перенаправить опасную команду на
# конфигурацию другого checkout.
SCRIPT_DIR="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
DEPLOYMENT_DIR="$(dirname -- "$SCRIPT_DIR")"
ENV_FILE="$DEPLOYMENT_DIR/.env"

# Хранить все защитные проектные значения в одном видимом блоке. Значения из `.env`
# должны точно совпасть с ожидаемыми базой, ролью и портом PostgreSQL до разрешения
# любой разрушающей команды.
APPLICATION_NAME="GaifulinLab"
CONFIRMATION_ARGUMENT="--confirm-reset-gaifulinlab"
DB_NAME_ENV_KEY="GAIFULINLAB_DB_NAME"
DB_USER_ENV_KEY="GAIFULINLAB_DB_USER"
DB_PORT_ENV_KEY="GAIFULINLAB_DB_PORT"
EXPECTED_DB_NAME="gaifulinlab"
EXPECTED_DB_USER="gaifulinlab"
EXPECTED_DB_PORT=5432

# `.` — команда Bash `source`: она загружает функции ошибок и dotenv в текущий shell,
# не выполняя deployment/.env как Bash-код.
. "$SCRIPT_DIR/common.sh"

# Сразу завершить работу, если внешняя команда для сброса не найдена.
# `command -v` проверяет PATH без запуска программы, а перенаправления скрывают
# вывод поиска, потому что важен только успешный или неуспешный статус.
require_command() {
    local command_name="$1"

    command -v "$command_name" >/dev/null 2>&1 \
        || deployment_fail "Required command '$command_name' is unavailable."
}

# Потребовать ровно один явный аргумент подтверждения.
# `$#` — число полученных аргументов. `${1:-}` безопасно читает первый аргумент
# при `set -u`, даже если пользователь ничего не передал.
validate_confirmation() {
    if (( $# != 1 )) || [[ "${1:-}" != "$CONFIRMATION_ARGUMENT" ]]; then
        deployment_fail "Refusing to reset the database. Run $0 $CONFIRMATION_ARGUMENT"
    fi
}

# Прочитать цель из серверного dotenv-файла и сравнить с жёстко заданными защитными
# значениями. Изменённый `.env` не сможет превратить проектную аварийную команду
# в универсальный инструмент удаления произвольных баз.
load_and_validate_target() {
    DB_NAME="$(deployment_require_env "$DB_NAME_ENV_KEY" "$ENV_FILE")"
    DB_USER="$(deployment_require_env "$DB_USER_ENV_KEY" "$ENV_FILE")"
    DB_PORT="$(deployment_require_env "$DB_PORT_ENV_KEY" "$ENV_FILE")"

    [[ "$DB_NAME" == "$EXPECTED_DB_NAME" ]] \
        || deployment_fail "Refusing to reset unexpected database '$DB_NAME'. Expected '$EXPECTED_DB_NAME'."
    [[ "$DB_USER" == "$EXPECTED_DB_USER" ]] \
        || deployment_fail "Refusing to use unexpected database role '$DB_USER'. Expected '$EXPECTED_DB_USER'."
    [[ "$DB_PORT" =~ ^[0-9]+$ ]] \
        || deployment_fail "Invalid PostgreSQL port in $DB_PORT_ENV_KEY."

    # `10#` считает значения с ведущим нулём десятичными. Проверка известного
    # production-порта не даёт перенаправить команду на другой локальный кластер.
    (( 10#$DB_PORT == EXPECTED_DB_PORT )) \
        || deployment_fail "Refusing to use PostgreSQL port '$DB_PORT'. Expected '$EXPECTED_DB_PORT'."
}

# Удалить базу и сразу создать пустую с ожидаемым владельцем.
# `sudo -u postgres` выполняет администрирование от системной учётной записи PostgreSQL.
# `dropdb --force` завершает активные подключения перед удалением, а `--if-exists`
# не считает ошибкой случай, когда предыдущий сброс уже удалил базу.
reset_database() {
    sudo -u postgres dropdb \
        --port="$DB_PORT" \
        --if-exists \
        --force \
        "$DB_NAME"

    # Точная проверка выше отклоняет имена, похожие на параметры командной строки,
    # а кавычки сохраняют каждое проверенное имя базы и роли одним аргументом Bash.
    sudo -u postgres createdb \
        --port="$DB_PORT" \
        --owner="$DB_USER" \
        "$DB_NAME"
}

# Подтверждение проверяется до обращения к файлам и PostgreSQL, чтобы случайный
# запуск остановился сразу и не выполнил даже часть подготовительных действий.
validate_confirmation "$@"
require_command sudo
require_command dropdb
require_command createdb

[[ -f "$ENV_FILE" ]] \
    || deployment_fail "Configure $ENV_FILE with deployment/client/configure-env.ps1 first."

load_and_validate_target

echo "Dropping and recreating $APPLICATION_NAME database '$DB_NAME' on local PostgreSQL port $DB_PORT..."
reset_database

# После пересоздания схема базы пуста. Миграции остаются отдельной явной операцией,
# чтобы этот разрушающий скрипт не мог незаметно развернуть новый код.
echo "$APPLICATION_NAME production database was recreated. Apply migrations next."
