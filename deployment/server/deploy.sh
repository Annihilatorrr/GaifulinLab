#!/usr/bin/env bash

# Развернуть контейнеры GaifulinLab и опубликовать их через nginx на сервере.
# `-e` останавливает скрипт при необработанной ошибке, `-u` запрещает необъявленные
# переменные, а `pipefail` считает конвейер ошибочным при сбое любой его команды.
set -euo pipefail

# Вычислить пути относительно скрипта, а не текущего каталога пользователя.
# `dirname "$0"` получает каталог скрипта, `cd` переходит в него, а `pwd` возвращает
# абсолютный путь. `CDPATH=` не позволяет настройке CDPATH изменить вывод `cd`.
SCRIPT_DIR="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
DEPLOYMENT_DIR="$(dirname -- "$SCRIPT_DIR")"
ENV_FILE="$DEPLOYMENT_DIR/.env"
COMPOSE_FILE="$DEPLOYMENT_DIR/docker-compose.prod-host-nginx.yml"
NGINX_TEMPLATE_FILE="$DEPLOYMENT_DIR/nginx-host/host-reverse-proxy.conf"
NGINX_SITE_FILE="/etc/nginx/sites-available/gaifulinlab.conf"
NGINX_ENABLED_LINK="/etc/nginx/sites-enabled/gaifulinlab.conf"
COMPOSE_PROJECT="gaifulinlab"
APPLICATION_NAME="GaifulinLab"

# `.` — команда Bash `source`: она загружает общие функции `deployment_*` в текущий
# shell, чтобы ошибки и dotenv обрабатывались одинаково во всех точках входа.
. "$SCRIPT_DIR/common.sh"

# Сразу завершить работу, если нужная далее внешняя программа недоступна.
# `command -v` ищет программу в PATH без запуска; stdout и stderr перенаправляются
# в `/dev/null`, потому что важен только успешный статус поиска.
require_command() {
    local command_name="$1"

    command -v "$command_name" >/dev/null 2>&1 || deployment_fail "Required command '$command_name' is unavailable."
}

# Сразу завершить работу, если отсутствует ожидаемый обычный файл.
# `[[ -f ... ]]` принимает файл, но не каталог. Такая ошибка понятнее, чем более
# поздний сбой Docker Compose, sed или чтения dotenv.
require_file() {
    local file="$1"

    [[ -f "$file" ]] || deployment_fail "Required file '$file' is missing."
}

# Проверить TCP-порт из `.env` до передачи другим инструментам.
# Первый аргумент — значение, второй — понятное пользователю название для ошибки.
require_port() {
    local port="$1"
    local label="$2"

    [[ "$port" =~ ^[0-9]+$ ]] || deployment_fail "Invalid $label."

    # `10#` явно считает значения вроде 080 десятичными, а не восьмеричными числами
    # старого синтаксиса Bash. Допустимые TCP- и UDP-порты: от 1 до 65535.
    (( 10#$port >= 1 && 10#$port <= 65535 )) || deployment_fail "Invalid $label."
}

# Обновить пароль существующей роли PostgreSQL значением из deployment/.env.
# Heredoc SQL заключён как `<<'SQL'`, поэтому Bash не подставляет внутри `$` и
# обратные кавычки. Значения передаются psql через именованные переменные.
sync_database_role_password() {
    sudo -u postgres psql --set=ON_ERROR_STOP=1 \
        --set=db_user="$DB_USER" \
        --set=db_password="$DB_PASSWORD" <<'SQL'
SELECT format('ALTER ROLE %I WITH LOGIN PASSWORD %L', :'db_user', :'db_password') \gexec
SQL
}

# Проверить тот же путь подключения к БД, который использует контейнер приложения.
# `docker run --rm` запускает одноразовый клиент PostgreSQL и затем удаляет его.
# Псевдоним host-gateway даёт Linux-контейнеру доступ к PostgreSQL на хосте.
check_database() {
    docker run --rm \
        --add-host host.docker.internal:host-gateway \
        -e "PGPASSWORD=$DB_PASSWORD" \
        postgres:17-alpine \
        psql -w \
            -h "$DB_HOST" \
            -p "$DB_PORT" \
            -d "$DB_NAME" \
            -U "$DB_USER" \
            -v ON_ERROR_STOP=1 \
            -c 'select 1;' \
        >/dev/null \
        || deployment_fail "Host PostgreSQL is unreachable from Docker."
}

# Создать полную конфигурацию nginx во временном файле.
# Каждое выражение `sed -e` заменяет один placeholder шаблона. Значения, которые
# могут содержать специальные символы sed, проходят через `deployment_escape_sed`;
# уже проверенные числовые порты можно вставлять напрямую.
render_nginx_config() {

    # `sed` читает шаблон из `$NGINX_TEMPLATE_FILE` и последовательно применяет
    # к нему все выражения, переданные через `-e`. Каждое выражение имеет форму
    # `s|ИСКОМЫЙ_ТЕКСТ|НОВОЕ_ЗНАЧЕНИЕ|g`:
    # - `s` означает substitute, то есть «заменить»;
    # - `|` выбран разделителем вместо обычного `/`, потому что пути сертификатов
    #   содержат много символов `/`;
    # - `g` заменяет все вхождения placeholder на строке, а не только первое.
    #
    # Подстановки ниже превращают шаблон в готовый конфиг nginx:
    # - список доменов записывается в `server_name`;
    # - пути сертификата и private key записываются в TLS-директивы;
    # - порты подставляются в адреса web- и API-маршрутов приложения.
    #
    # `${SERVER_NAMES//,/ }` — Bash-замена всех запятых пробелами: nginx ожидает
    # домены через пробел. `$(...)` запускает `deployment_escape_sed`, который
    # экранирует специальные для replacement символы `\`, `&` и `|`.
    # Последняя строка указывает входной шаблон, а `>` записывает итог целиком
    # в новый временный файл `$NGINX_TEMP_FILE`.
    sed \
        -e "s|__GAIFULINLAB_HOST_NGINX_SERVER_NAMES__|$(deployment_escape_sed "${SERVER_NAMES//,/ }")|g" \
        -e "s|__GAIFULINLAB_HOST_NGINX_CERT_FILE__|$(deployment_escape_sed "$CERT_FILE")|g" \
        -e "s|__GAIFULINLAB_HOST_NGINX_CERT_KEY__|$(deployment_escape_sed "$CERT_KEY")|g" \
        -e "s|__GAIFULINLAB_HOST_NGINX_BACKEND_HTTP_PORT__|$WEB_PORT|g" \
        "$NGINX_TEMPLATE_FILE" > "$NGINX_TEMP_FILE"
}

# Убедиться, что оба TLS-файла существуют и сертификат сейчас действителен.
# `openssl ... -checkend 0` возвращает ошибку для уже истёкшего сертификата,
# но ничего не обновляет и не изменяет.
validate_tls_certificate() {
    sudo test -f "$CERT_FILE" && sudo test -f "$CERT_KEY" \
        || deployment_fail "TLS certificate pair is missing. Run deployment/client/setup-certificate.ps1 first."

    sudo openssl x509 -in "$CERT_FILE" -noout -checkend 0 >/dev/null \
        || deployment_fail "Configured TLS certificate is expired."
}

# Установить сгенерированный сайт только после запуска контейнеров приложения.
# `install -m 644` копирует файл с явными правами, `ln -sfn` создаёт или перенаправляет
# ссылку включённого сайта, а `nginx -t` проверяет всё до reload.
install_nginx_site() {
    sudo install -m 644 "$NGINX_TEMP_FILE" "$NGINX_SITE_FILE"
    sudo ln -sfn "$NGINX_SITE_FILE" "$NGINX_ENABLED_LINK"
    sudo nginx -t
}

# Применить проверенную конфигурацию nginx.
# Reload сохраняет активные соединения. При первой установке `enable --now`
# запускает nginx сейчас и включает автозапуск после будущих перезагрузок.
reload_nginx() {
    if sudo systemctl is-active --quiet nginx; then
        sudo systemctl reload nginx
    else
        sudo systemctl enable --now nginx
    fi
}

# Опросить один публичный путь до успешного ответа или истечения примерно двух минут.
# Параметры curl хранятся в массиве, чтобы каждый оставался отдельным аргументом
# при раскрытии через `"${HEALTHCHECK_CURL_ARGUMENTS[@]}"`.
wait_for_path() {
    local path="$1"
    local attempt

    for (( attempt = 1; attempt <= 60; attempt++ )); do
        if curl "${HEALTHCHECK_CURL_ARGUMENTS[@]}" "$HEALTHCHECK_BASE_URL$path" >/dev/null; then
            return 0
        fi

        sleep 2
    done

    # Последние логи контейнеров упрощают диагностику healthcheck. `|| true` не даёт
    # вторичной ошибке чтения логов скрыть исходную проблему.
    docker compose -p "$COMPOSE_PROJECT" --env-file "$ENV_FILE" -f "$COMPOSE_FILE" logs --tail 120 >&2 || true
    deployment_fail "Health check failed for $path."
}

wait_for_pdf_renderer() {
    local attempt

    for (( attempt = 1; attempt <= 30; attempt++ )); do
        if docker compose -p "$COMPOSE_PROJECT" --env-file "$ENV_FILE" -f "$COMPOSE_FILE" \
            exec -T gotenberg curl --silent --fail http://localhost:3000/health >/dev/null; then
            return 0
        fi

        sleep 2
    done

    docker compose -p "$COMPOSE_PROJECT" --env-file "$ENV_FILE" -f "$COMPOSE_FILE" \
        logs --tail 120 gotenberg >&2 || true
    deployment_fail "Gotenberg PDF renderer health check failed."
}

# Проверить неизменяемые условия деплоя до изменений PostgreSQL, контейнеров и nginx.
# Цикл одинаково проверяет наличие каждой внешней программы в PATH.
for command_name in docker curl sed sudo openssl psql; do
    require_command "$command_name"
done

require_file "$ENV_FILE"
require_file "$COMPOSE_FILE"
require_file "$NGINX_TEMPLATE_FILE"

# Docker может быть установлен при недоступном daemon или Compose-плагине, поэтому
# они проверяются отдельно и дают понятную для деплоя ошибку.
docker info >/dev/null 2>&1 || deployment_fail "Docker daemon is unavailable."
docker compose version >/dev/null 2>&1 || deployment_fail "Docker Compose plugin is unavailable."

# Прочитать настройки деплоя как данные, не подключая `.env` как исполняемый Bash.
# Обязательные функции отвергают пустые и демонстрационные значения, а необязательные
# дают описанные defaults, не записывая серверные значения обратно в файл.
DB_HOST="$(deployment_require_env GAIFULINLAB_DB_HOST "$ENV_FILE")"
DB_PORT="$(deployment_require_env GAIFULINLAB_DB_PORT "$ENV_FILE")"
DB_NAME="$(deployment_require_env GAIFULINLAB_DB_NAME "$ENV_FILE")"
DB_USER="$(deployment_require_env GAIFULINLAB_DB_USER "$ENV_FILE")"
DB_PASSWORD="$(deployment_require_env GAIFULINLAB_DB_PASSWORD "$ENV_FILE")"
PUBLIC_ORIGIN="$(deployment_require_env GAIFULINLAB_PUBLIC_ORIGIN "$ENV_FILE")"
SERVER_NAMES="$(deployment_require_env GAIFULINLAB_HOST_NGINX_SERVER_NAMES "$ENV_FILE")"
WEB_PORT="$(deployment_optional_env GAIFULINLAB_HOST_NGINX_BACKEND_HTTP_PORT 38190 "$ENV_FILE")"
CERT_FILE="$(deployment_require_env GAIFULINLAB_HOST_NGINX_CERT_FILE "$ENV_FILE")"
CERT_KEY="$(deployment_require_env GAIFULINLAB_HOST_NGINX_CERT_KEY "$ENV_FILE")"

MEDIA_HOST_PATH="$(deployment_optional_env GAIFULINLAB_MEDIA_HOST_PATH "$DEPLOYMENT_DIR/runtime/media" "$ENV_FILE")"

# Проверить значения до превращения в SQL-идентификаторы, URL, файловые пути,
# сопоставления портов Docker или элементы конфигурации nginx.
[[ "$DB_NAME" =~ ^[A-Za-z0-9_]+$ && "$DB_USER" =~ ^[A-Za-z0-9_]+$ ]] \
    || deployment_fail "Invalid PostgreSQL database or role name."
[[ "$PUBLIC_ORIGIN" =~ ^https:// ]] \
    || deployment_fail "GAIFULINLAB_PUBLIC_ORIGIN must use HTTPS."
deployment_validate_server_names "$SERVER_NAMES"
require_port "$DB_PORT" "database port"
require_port "$WEB_PORT" "web backend port"
[[ "$CERT_FILE" = /* && "$CERT_KEY" = /* ]] \
    || deployment_fail "Certificate paths must be absolute."
[[ "$MEDIA_HOST_PATH" = /* ]] \
    || deployment_fail "GAIFULINLAB_MEDIA_HOST_PATH must be absolute."

# Первое имя сервера — канонический hostname для локальных HTTPS-проверок.
PRIMARY_DOMAIN="$(deployment_first_server_name "$SERVER_NAMES")"
HEALTHCHECK_BASE_URL="https://$PRIMARY_DOMAIN"
HEALTHCHECK_CURL_ARGUMENTS=(
    --silent
    --fail
    --resolve "$PRIMARY_DOMAIN:443:127.0.0.1"
)

# Хранить загруженные изображения вне Docker-образов в постоянном серверном каталоге.
# Экспорт вычисленного пути даёт ему приоритет при подстановке Compose, в том числе
# если настройка отсутствовала в `.env` и было взято значение по умолчанию.
mkdir -p "$MEDIA_HOST_PATH"
export GAIFULINLAB_MEDIA_HOST_PATH="$MEDIA_HOST_PATH"

# Синхронизировать роль PostgreSQL на хосте, затем до пересборки приложения доказать,
# что одноразовый контейнер может подключиться с теми же настройками.
sync_database_role_password
check_database

# Установщик готовит пакет nginx и стандартные каталоги. Проверка сертификата
# остаётся здесь: deploy.sh — последний рубеж перед публикацией HTTPS.
"$SCRIPT_DIR/install-nginx.sh"
validate_tls_certificate

# `mktemp` создаёт уникальный файл. Ловушка EXIT удаляет его при успехе и ошибке;
# одинарные кавычки откладывают подстановку `$NGINX_TEMP_FILE` до запуска ловушки.
NGINX_TEMP_FILE="$(mktemp)"
trap 'rm -f "$NGINX_TEMP_FILE"' EXIT
render_nginx_config

# `up -d` пересоздаёт сервисы в фоне, `--build` обновляет их образы, а
# `--remove-orphans` удаляет устаревшие сервисы этого Compose-проекта.
docker compose -p "$COMPOSE_PROJECT" --env-file "$ENV_FILE" -f "$COMPOSE_FILE" \
    up -d --build --remove-orphans

echo "Checking deployment health: Gotenberg PDF renderer"
wait_for_pdf_renderer

# Publish the host nginx route only after both private API and Web services start.
install_nginx_site
reload_nginx

# Массив явно перечисляет health endpoints и сохраняет каждый путь одним значением.
HEALTHCHECK_PATHS=(/health/live /health/ready)

for health_path in "${HEALTHCHECK_PATHS[@]}"; do
    echo "Checking deployment health: $health_path"
    wait_for_path "$health_path"
done

DEPLOYMENT_URL="https://$PRIMARY_DOMAIN"
echo "$APPLICATION_NAME deployed at $DEPLOYMENT_URL."
