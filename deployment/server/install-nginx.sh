#!/usr/bin/env bash

# Установить системный пакет nginx и подготовить стандартные каталоги сайтов.
# Этот вспомогательный скрипт намеренно не создаёт конфигурацию приложения, не
# включает её и не перезагружает nginx: это делает вызывающий скрипт деплоя или
# настройки сертификата. `-e` останавливает скрипт при необработанной ошибке,
# `-u` запрещает необъявленные переменные, а `pipefail` считает конвейер ошибочным,
# если завершилась ошибкой любая команда внутри него.
set -euo pipefail

# Найти общую библиотеку относительно этого файла, а не текущего каталога
# пользователя. `CDPATH=` не позволяет пользовательской настройке CDPATH изменить
# вывод команды `cd` и случайно испортить значение `SCRIPT_DIR`.
SCRIPT_DIR="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"

# `.` — команда Bash `source`: она определяет общую функцию `deployment_fail`
# в текущем процессе shell, не запуская ещё один процесс Bash.
. "$SCRIPT_DIR/common.sh"

# Необязательный флаг нужен, когда вызывающий скрипт сейчас заменит старую,
# заведомо нерабочую конфигурацию сайта. Например, временный HTTP-сайт заменяет
# TLS-сайт, сертификат для которого ещё не создан. `$#` содержит число аргументов,
# а `${1:-}` безопасно читает первый аргумент, даже если аргументов нет.
SKIP_CONFIG_TEST=0

(( $# <= 1 )) || deployment_fail "Usage: $0 [--skip-config-test]"

case "${1:-}" in
    "")
        ;;
    --skip-config-test)
        SKIP_CONFIG_TEST=1
        ;;
    *)
        deployment_fail "Usage: $0 [--skip-config-test]"
        ;;
esac

# Завершить работу с понятной ошибкой, если внешняя команда не найдена в PATH.
# `command -v` ищет программу, не запуская её. Вывод отбрасывается, потому что
# здесь важен только успешный или неуспешный код завершения поиска.
require_command() {
    local command_name="$1"

    command -v "$command_name" >/dev/null 2>&1 \
        || deployment_fail "Required command '$command_name' is unavailable."
}

# Сохранить существующую установку nginx. Если nginx отсутствует, обновить индекс
# пакетов apt и установить его без интерактивных вопросов, чтобы скрипт мог
# полностью выполняться через SSH.
ensure_nginx_installed() {
    if command -v nginx >/dev/null 2>&1; then
        return 0
    fi

    require_command apt-get
    sudo apt-get update

    # `env` передаёт переменную только процессу apt-get. Она отключает вопросы
    # установщика, но не экспортируется в остальную сессию деплоя.
    sudo env DEBIAN_FRONTEND=noninteractive apt-get install -y nginx
}

# `sudo` нужен для установки пакетов и работы с файлами внутри `/etc/nginx`.
require_command sudo
ensure_nginx_installed

# `mkdir -p` создаёт отсутствующие каталоги и не считает ошибкой их наличие,
# поэтому повторный запуск безопасен. Debian и Ubuntu загружают сайты из этих путей.
sudo mkdir -p /etc/nginx/sites-available /etc/nginx/sites-enabled

# Обычно проверить все включённые конфигурации nginx, не запуская и не перезагружая
# службу. Если вызывающий код явно пропустил проверку, он обязан установить и
# проверить заменяющую конфигурацию до перезагрузки nginx.
if (( SKIP_CONFIG_TEST )); then
    echo "Host nginx is installed; the caller will replace and validate its configuration."
else
    sudo nginx -t
    echo "Host nginx is installed and its current configuration is valid."
fi
