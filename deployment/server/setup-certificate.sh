#!/usr/bin/env bash

# Запросить или обновить публичный сертификат Let's Encrypt через nginx-плагин
# Certbot. DNS и входящие порты 80/443 уже должны вести на этот сервер.
# `-e` останавливает скрипт при необработанной ошибке, `-u` запрещает необъявленные
# переменные, а `pipefail` считает конвейер ошибочным при сбое любой его команды.
set -euo pipefail

# Вычислить пути относительно скрипта, а не текущего каталога пользователя.
# `CDPATH=` не позволяет пользовательской настройке CDPATH изменить вывод `cd`.
SCRIPT_DIR="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
DEPLOYMENT_DIR="$(dirname -- "$SCRIPT_DIR")"
ENV_FILE="$DEPLOYMENT_DIR/.env"

# Хранить специфичные для проекта имена в одном блоке, чтобы основной сценарий
# работы с сертификатом оставался одинаковым. Пустой `TLS_EMAIL_ENV_KEY` означает,
# что Certbot может интерактивно запросить данные; клиентский wrapper выделяет TTY.
APPLICATION_NAME="GaifulinLab"
NGINX_SITE_FILE="/etc/nginx/sites-available/gaifulinlab.conf"
NGINX_ENABLED_LINK="/etc/nginx/sites-enabled/gaifulinlab.conf"
SERVER_NAMES_ENV_KEY="GAIFULINLAB_HOST_NGINX_SERVER_NAMES"
TLS_EMAIL_ENV_KEY="GAIFULINLAB_TLS_EMAIL"
CERT_FILE_ENV_KEY="GAIFULINLAB_HOST_NGINX_CERT_FILE"
CERT_KEY_ENV_KEY="GAIFULINLAB_HOST_NGINX_CERT_KEY"
BOOTSTRAP_TLS_ENV_KEY="GAIFULINLAB_HOST_NGINX_BOOTSTRAP_SELF_SIGNED"

# `.` — команда Bash `source`: она загружает в текущий shell общую валидацию и
# атомарное обновление dotenv, не выполняя deployment/.env как Bash-код.
. "$SCRIPT_DIR/common.sh"

# Сразу завершить работу, если нужная внешняя команда недоступна.
# `command -v` ищет программу в PATH без запуска; вывод отбрасывается, потому что
# важен только успешный или неуспешный статус поиска.
require_command() {
    local command_name="$1"

    command -v "$command_name" >/dev/null 2>&1 \
        || deployment_fail "Required command '$command_name' is unavailable."
}

# Добавить проверенный домен одновременно в список сертификата и аргументы Certbot.
# Массивы изменяются вместе, а дубликаты игнорируются: одинаковое имя из `.env`
# и командной строки не создаст повторяющиеся параметры `-d`.
append_domain() {
    local domain="$1"
    local existing_domain
    local domain_index

    deployment_validate_hostname "$domain"

    for (( domain_index = 0; domain_index < DOMAIN_COUNT; domain_index++ )); do
        existing_domain="${DOMAINS[$domain_index]}"

        if [[ "$existing_domain" == "$domain" ]]; then
            return 0
        fi
    done

    DOMAINS+=("$domain")
    CERTBOT_ARGUMENTS+=(-d "$domain")
    DOMAIN_COUNT=$((DOMAIN_COUNT + 1))
}

# Прочитать настроенные имена серверов и необязательный email учётной записи Certbot.
# `read -r -a` разбивает нормализованные имена по пробелам в массив Bash; каждое
# имя проходит через одну функцию проверки и удаления дубликатов.
load_certificate_config() {
    local configured_server_names
    local configured_domains=()
    local domain

    configured_server_names="$(deployment_require_env "$SERVER_NAMES_ENV_KEY" "$ENV_FILE")"
    deployment_validate_server_names "$configured_server_names"
    read -r -a configured_domains <<< "${configured_server_names//,/ }"

    for domain in "${configured_domains[@]}"; do
        append_domain "$domain"
    done

    TLS_EMAIL=""

    if [[ -n "$TLS_EMAIL_ENV_KEY" ]]; then
        TLS_EMAIL="$(deployment_require_env "$TLS_EMAIL_ENV_KEY" "$ENV_FILE")"
        [[ "$TLS_EMAIL" =~ ^[^[:space:]@]+@[^[:space:]@]+\.[^[:space:]@]+$ ]] \
            || deployment_fail "Invalid email address in $TLS_EMAIL_ENV_KEY."
    fi
}

# Вернуть успех, только если live-каталог и renewal-файл Certbot образуют полную
# запись сертификата. Используется `sudo test`, потому что обычному пользователю
# часто запрещён просмотр закрытых каталогов `/etc/letsencrypt`.
certificate_metadata_is_valid() {
    local certificate_name="$1"
    local renewal_file="/etc/letsencrypt/renewal/$certificate_name.conf"
    local metadata_key

    sudo test -d "/etc/letsencrypt/live/$certificate_name" || return 1
    sudo test -f "$renewal_file" || return 1

    for metadata_key in cert privkey chain fullchain; do
        sudo grep -q "^$metadata_key = /" "$renewal_file" || return 1
    done
}

# Выбрать стабильное имя сертификата Certbot. Сначала предпочесть корректные
# метаданные основного домена, затем ранее созданное резервное имя `-letsencrypt`.
# При повреждённых основных метаданных использовать резервное имя, не затирая их.
select_certificate_name() {
    local primary_domain="$1"
    local recovery_name="${primary_domain}-letsencrypt"

    if certificate_metadata_is_valid "$primary_domain"; then
        printf '%s' "$primary_domain"
        return 0
    fi

    if certificate_metadata_is_valid "$recovery_name"; then
        printf '%s' "$recovery_name"
        return 0
    fi

    if sudo test -d "/etc/letsencrypt/live/$primary_domain" \
        || sudo test -f "/etc/letsencrypt/renewal/$primary_domain.conf"; then
        printf '%s' "$recovery_name"
    else
        printf '%s' "$primary_domain"
    fi
}

# Установить Certbot и его nginx-плагин, только если команды `certbot` ещё нет.
# `sudo env` передаёт DEBIAN_FRONTEND только apt-get и не экспортирует переменную
# последующим командам интерактивной SSH-сессии.
ensure_certbot_installed() {
    if command -v certbot >/dev/null 2>&1; then
        return 0
    fi

    require_command apt-get
    sudo apt-get update
    sudo env DEBIAN_FRONTEND=noninteractive apt-get install -y certbot python3-certbot-nginx
}

# Создать минимальный HTTP-сайт, предоставляющий Certbot блок nginx для всех имён.
# Подставляется только проверенный список доменов. location с ответом 404 намеренно
# не открывает приложение наружу во время выполнения ACME-проверки.
render_acme_nginx_config() {
    {
        echo 'server {'
        echo '    listen 80;'
        printf '    server_name %s;\n' "$CERTIFICATE_SERVER_NAMES"
        echo '    location / { return 404; }'
        echo '}'
    } > "$NGINX_TEMP_FILE"
}

# Запустить nginx при первом запросе сертификата или перечитать конфигурацию, если
# он уже работает. Reload сохраняет активные соединения; `enable --now` запускает
# службу сейчас и включает её автоматический запуск после перезагрузки сервера.
reload_nginx() {
    if sudo systemctl is-active --quiet nginx; then
        sudo systemctl reload nginx
    else
        sudo systemctl enable --now nginx
    fi
}

# The ACME bootstrap site has no HTTPS listener. Preserve the prior working
# configuration and restore it whenever certificate issuance fails, rather
# than leaving nginx to serve another site's default certificate.
NGINX_BACKUP_FILE="$(mktemp)"
NGINX_SITE_EXISTED=0
NGINX_CONFIG_REPLACED=0

restore_nginx_config_on_failure() {
    local exit_code=$?

    trap - EXIT

    if (( exit_code != 0 && NGINX_CONFIG_REPLACED )); then
        if (( NGINX_SITE_EXISTED )); then
            sudo install -m 644 "$NGINX_BACKUP_FILE" "$NGINX_SITE_FILE"
        else
            sudo rm -f "$NGINX_SITE_FILE" "$NGINX_ENABLED_LINK"
        fi

        if sudo nginx -t; then
            reload_nginx
        else
            echo "WARNING: could not validate nginx while restoring its previous configuration." >&2
        fi
    fi

    rm -f "$NGINX_BACKUP_FILE"
    exit "$exit_code"
}

trap restore_nginx_config_on_failure EXIT

# Проверить неизменяемые предварительные условия до установки пакетов и замены сайта.
require_command sudo
require_command grep
[[ -f "$ENV_FILE" ]] \
    || deployment_fail "Configure $ENV_FILE with deployment/client/configure-env.ps1 first."

DOMAINS=()
DOMAIN_COUNT=0
CERTBOT_ARGUMENTS=(--nginx --redirect)

load_certificate_config

# Дополнительные домены из командной строки позволяют разово добавить псевдонимы.
# `"$@"` точно сохраняет каждый аргумент; все значения всё равно проверяются и
# очищаются от дубликатов.
for additional_domain in "$@"; do
    append_domain "$additional_domain"
done

(( DOMAIN_COUNT > 0 )) || deployment_fail "No certificate domains were configured."
PRIMARY_DOMAIN="${DOMAINS[0]}"
CERTIFICATE_SERVER_NAMES="${DOMAINS[*]}"
CERTIFICATE_NAME="$(select_certificate_name "$PRIMARY_DOMAIN")"
PRIMARY_RENEWAL_FILE="/etc/letsencrypt/renewal/$PRIMARY_DOMAIN.conf"

# Настроенный email делает Certbot полностью неинтерактивным. Без него Certbot
# может запросить данные через TTY, выделенный client/setup-certificate.ps1.
if [[ -n "$TLS_EMAIL" ]]; then
    CERTBOT_ARGUMENTS+=(--non-interactive --agree-tos --email "$TLS_EMAIL")
fi

ensure_certbot_installed

if sudo test -f "$NGINX_SITE_FILE"; then
    sudo cp "$NGINX_SITE_FILE" "$NGINX_BACKUP_FILE"
    NGINX_SITE_EXISTED=1
fi

# Текущий сайт приложения может ссылаться на ещё не созданный сертификат, поэтому
# установщик намеренно откладывает первую проверку nginx. Этот скрипт сразу заменяет
# сайт временной HTTP-конфигурацией и проверяет уже её.
"$SCRIPT_DIR/install-nginx.sh" --skip-config-test

NGINX_TEMP_FILE="$(mktemp)"
render_acme_nginx_config

sudo install -m 644 "$NGINX_TEMP_FILE" "$NGINX_SITE_FILE"
NGINX_CONFIG_REPLACED=1
sudo ln -sfn "$NGINX_SITE_FILE" "$NGINX_ENABLED_LINK"
sudo nginx -t
reload_nginx
rm -f "$NGINX_TEMP_FILE"

# `--nginx` позволяет Certbot опубликовать ACME-проверку и установить результат во
# временный сайт. После выдачи сертификата `--redirect` переводит HTTP-сайт на HTTPS.
sudo certbot "${CERTBOT_ARGUMENTS[@]}" --cert-name "$CERTIFICATE_NAME"

CERT_FILE="/etc/letsencrypt/live/$CERTIFICATE_NAME/fullchain.pem"
CERT_KEY="/etc/letsencrypt/live/$CERTIFICATE_NAME/privkey.pem"

sudo test -f "$CERT_FILE" && sudo test -f "$CERT_KEY" \
    || deployment_fail "Certbot did not create the expected certificate pair."

# Атомарно обновить серверные настройки, чтобы следующий деплой создал конфигурацию
# nginx с выданным сертификатом и отключил начальный самоподписанный режим.
deployment_update_env_value "$CERT_FILE_ENV_KEY" "$CERT_FILE" "$ENV_FILE"
deployment_update_env_value "$CERT_KEY_ENV_KEY" "$CERT_KEY" "$ENV_FILE"
deployment_update_env_value "$BOOTSTRAP_TLS_ENV_KEY" 0 "$ENV_FILE"

# После успешного сертификата с резервным именем сохранить повреждённые основные
# renewal-метаданные для диагностики. Корректные резервные данные останутся на месте.
if [[ "$CERTIFICATE_NAME" != "$PRIMARY_DOMAIN" ]] && sudo test -f "$PRIMARY_RENEWAL_FILE"; then
    sudo mv "$PRIMARY_RENEWAL_FILE" "$PRIMARY_RENEWAL_FILE.invalid-backup"
fi

# Проверить изменения Certbot до reload, затем испытать автоматическое обновление
# через тестовый сервис Let's Encrypt, не выпуская ещё один настоящий сертификат.
sudo nginx -t
sudo systemctl reload nginx
sudo certbot renew --dry-run

NGINX_CONFIG_REPLACED=0

echo "$APPLICATION_NAME certificate is ready for $CERTIFICATE_SERVER_NAMES. Run deployment/client/deploy.ps1 next."
