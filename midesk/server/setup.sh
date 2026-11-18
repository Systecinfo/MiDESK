#!/usr/bin/env bash
# Instala o servidor do MiDESK (hbbs + hbbr) numa VPS Linux com Docker.
# Uso (como root):  bash setup.sh
# Ao final imprime a chave pública -> variável MIDESK_KEY no GitHub.
set -euo pipefail

DIR=/opt/midesk-server
SSH_PORT="$(ss -tlnp 2>/dev/null | awk '/sshd/ {split($4,a,":"); print a[length(a)]; exit}')"
SSH_PORT="${SSH_PORT:-22}"

echo "== Sistema: $(. /etc/os-release && echo "$PRETTY_NAME")"

if ! command -v docker >/dev/null 2>&1; then
  echo "== Instalando Docker (script oficial get.docker.com)"
  curl -fsSL https://get.docker.com -o /tmp/get-docker.sh
  sh /tmp/get-docker.sh
fi
systemctl enable --now docker >/dev/null 2>&1 || true
docker compose version >/dev/null

mkdir -p "$DIR"
cat > "$DIR/docker-compose.yml" <<'EOF'
services:
  hbbs:
    container_name: hbbs
    image: rustdesk/rustdesk-server:latest
    command: hbbs
    volumes:
      - ./data:/root
    network_mode: host
    depends_on:
      - hbbr
    restart: unless-stopped

  hbbr:
    container_name: hbbr
    image: rustdesk/rustdesk-server:latest
    command: hbbr
    volumes:
      - ./data:/root
    network_mode: host
    restart: unless-stopped
EOF

cd "$DIR"
docker compose pull -q
docker compose up -d

echo "== Firewall (porta SSH detectada: $SSH_PORT)"
if command -v ufw >/dev/null 2>&1 && ufw status | grep -q "Status: active"; then
  ufw allow "$SSH_PORT/tcp"
  ufw allow 21115:21117/tcp
  ufw allow 21116/udp
  ufw allow 21118:21119/tcp
elif command -v firewall-cmd >/dev/null 2>&1 && firewall-cmd --state >/dev/null 2>&1; then
  firewall-cmd --permanent --add-port="$SSH_PORT/tcp"
  firewall-cmd --permanent --add-port=21115-21119/tcp
  firewall-cmd --permanent --add-port=21116/udp
  firewall-cmd --reload
else
  echo "   nenhum firewall local ativo (ufw/firewalld); confira o firewall do painel da hospedagem"
fi

for i in $(seq 1 20); do [ -s data/id_ed25519.pub ] && break; sleep 1; done
echo "== Containers"
docker compose ps
echo "== Portas"
ss -tulnp | grep -E ':2111[5-9]\b' || true
echo "MIDESK_KEY=$(cat data/id_ed25519.pub)"
