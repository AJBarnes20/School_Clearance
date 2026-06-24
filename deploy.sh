#!/usr/bin/env bash
set -euo pipefail

GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
NC='\033[0m'
CHECK="${GREEN}[✓]${NC}"
CROSS="${RED}[✗]${NC}"
ARROW="${YELLOW}[▶]${NC}"
ok()   { echo -e "$CHECK $1"; }
fail() { echo -e "$CROSS $1"; }
info() { echo -e "$ARROW $1"; }
die()  { fail "$1"; exit 1; }
echo ""
echo "  PHASE 0 — LOAD CONFIGURATION"

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
cd "$SCRIPT_DIR"

if [ ! -f ".env" ]; then
  die ".env file not found. Create it from .env.example:\n  cp .env.example .env\n  nano .env"
fi
source .env
: "${GITHUB_REPO_URL:?}" "${GITHUB_TOKEN:?GITHUB_TOKEN is empty — add it to .env}"
: "${DB_USER:?}" "${DB_PASSWORD:?}" "${DB_NAME:?}"
: "${APP_PORT:=8086}"

ok "Configuration loaded from .env"

echo ""
echo "  PHASE 1 — SYSTEM DEPENDENCIES"

if dotnet --version 2>/dev/null | grep -q "^10"; then
  ok ".NET 10 SDK already installed ($(dotnet --version))"
else
  info "Installing .NET 10 SDK..."
  sudo apt update -qq
  sudo apt install -y -qq dotnet-sdk-10.0
  ok ".NET 10 SDK installed"
fi

if command -v mysql &>/dev/null; then
  ok "MySQL client found ($(mysql --version | head -1))"
else
  info "Installing MySQL server..."
  sudo apt install -y -qq mysql-server
  sudo systemctl start mysql
  ok "MySQL installed"
fi

if sudo systemctl is-active mysql &>/dev/null; then
  ok "MySQL service is running"
else
  info "Starting MySQL..."
  sudo systemctl start mysql
  ok "MySQL service started"
fi

if mysql -u"$DB_USER" -p"$DB_PASSWORD" "$DB_NAME" -e "SELECT 1" &>/dev/null; then
  ok "MySQL user '$DB_USER' can access database '$DB_NAME'"
else
  echo ""
  fail "MySQL user '$DB_USER' or database '$DB_NAME' is not accessible."
  echo ""
  echo "  Run these commands as root (sudo mysql):"
  echo ""
  echo "    CREATE USER '$DB_USER'@'localhost' IDENTIFIED BY '$DB_PASSWORD';"
  echo "    CREATE DATABASE $DB_NAME CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;"
  echo "    GRANT ALL PRIVILEGES ON $DB_NAME.* TO '$DB_USER'@'localhost';"
  echo "    FLUSH PRIVILEGES;"
  echo ""
  die "Fix MySQL setup above, then re-run ./deploy.sh"
fi

if ss -tlnp 2>/dev/null | grep -q ":$APP_PORT "; then
  die "Port $APP_PORT is already in use"
fi
ok "Port $APP_PORT is free"
echo ""
echo "  PHASE 2 — SYNC CODE FROM GITHUB"

if [ ! -d ".git" ]; then
  info "Not a git repository — cloning..."
  REPO_AUTH="https://${GITHUB_TOKEN}@${GITHUB_REPO_URL#https://}"
  cd "$(dirname "$SCRIPT_DIR")"
  git clone "$REPO_AUTH" "$(basename "$SCRIPT_DIR")"
  cd "$SCRIPT_DIR"
  ok "Repository cloned"
else
  REPO_AUTH="https://${GITHUB_TOKEN}@${GITHUB_REPO_URL#https://}"
  git remote set-url origin "$REPO_AUTH"
  git fetch origin main
  git checkout main
  git reset --hard origin/main
  git clean -fd
  git remote set-url origin "$GITHUB_REPO_URL"
  ok "Code synced to origin/main ($(git log -1 --format='%h %s' 2>/dev/null || echo 'n/a'))"
fi
echo ""
echo "  PHASE 3 — DATABASE IMPORT"

TABLE_COUNT=$(mysql -u"$DB_USER" -p"$DB_PASSWORD" -NBe \
  "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='$DB_NAME'" 2>/dev/null || echo "0")

if [ "$TABLE_COUNT" -gt 0 ]; then
  ok "Database '$DB_NAME' has $TABLE_COUNT tables — skipping import (data preserved)"
else
  if [ -d "Dump20260619" ]; then
    SQL_FILES=(Dump20260619/*.sql)
    if [ ${#SQL_FILES[@]} -gt 0 ]; then
      info "Importing ${#SQL_FILES[@]} SQL dump files..."
      for f in "${SQL_FILES[@]}"; do
        echo "    -> $f"
        mysql -u"$DB_USER" -p"$DB_PASSWORD" "$DB_NAME" < "$f"
      done
      ok "SQL import complete"
    else
      info "No .sql files found in Dump20260619/ — skipping"
    fi
  else
    info "Dump20260619/ directory not found — skipping import"
  fi
fi
echo ""
echo "  PHASE 4 — BUILD & PUBLISH"

info "Restoring NuGet packages..."
dotnet restore

info "Generating appsettings.json..."
cat > appsettings.json <<JSONEOF
{
  "ConnectionStrings": {
    "DefaultConnection": "server=${DB_HOST:-localhost};port=${DB_PORT:-3306};database=${DB_NAME};user=${DB_USER};password=${DB_PASSWORD};"
  },
  "Email": {
    "SmtpHost": "${SMTP_HOST:-smtp.gmail.com}",
    "SmtpPort": ${SMTP_PORT:-587},
    "SenderEmail": "${SMTP_USER}",
    "SenderName": "Online Clearance",
    "Password": "${SMTP_PASS}"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
JSONEOF

PUBLISH_DIR="$HOME/publish"
info "Publishing to $PUBLISH_DIR..."
dotnet publish -c Release -o "$PUBLISH_DIR" --nologo
cp appsettings.json "$PUBLISH_DIR/"
ok "Build & publish complete"
echo ""
echo "  PHASE 5 — SYSTEMD SERVICE"

SERVICE_FILE="/etc/systemd/system/clearance.service"

sudo tee "$SERVICE_FILE" > /dev/null <<SERVICEEOF
[Unit]
Description=Online Clearance System
After=network.target mysql.service

[Service]
WorkingDirectory=$PUBLISH_DIR
ExecStart=/usr/bin/dotnet $PUBLISH_DIR/OnlineClearanceSystem.dll
Restart=always
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=clearance
User=$(whoami)
Environment=ASPNETCORE_URLS=http://0.0.0.0:${APP_PORT}
Environment=ASPNETCORE_ENVIRONMENT=Production

[Install]
WantedBy=multi-user.target
SERVICEEOF

sudo systemctl daemon-reload
sudo systemctl enable clearance
sudo systemctl restart clearance

ok "Service 'clearance' started on port $APP_PORT"
echo ""
echo -e "  ${GREEN}[✓] DEPLOYMENT COMPLETE${NC}"
echo ""
echo "  LAN Access:  http://<this-server-ip>:${APP_PORT}"
echo ""
echo "  Useful commands:"
echo "    sudo systemctl status clearance"
echo "    sudo journalctl -u clearance -f"
echo ""
IP=$(ip -4 addr show scope global 2>/dev/null | grep -oP 'inet \K[\d.]+' | head -1)
if [ -n "$IP" ]; then
  echo "  Server IP:   http://${IP}:${APP_PORT}"
fi
echo ""
