#!/usr/bin/env bash
# =====================================================================================================================
# KUNLIK TO'LIQ ZAXIRA → TELEGRAM (bitta shifrlangan fayl) — egasining talabi, 2026-11-04.
#
# Server yo'qolsa: GitHub (kod) + shu fayl + BITTA parol (BACKUP_ENCRYPT_PASSPHRASE) = hammasi tiklanadi.
# Ichida: asosiy markaz VA har bir filial (/root/wlc-branches/<slug>) uchun — baza (pg_dump), yuklangan fayllar,
# .env; server qismi — Cloudflare Origin sertifikati (/root/certs) va filiallar nginx xaritalari; TIKLASH.txt.
#
# ⚠️ XAVFSIZLIK:
#   - Fayl gpg AES-256 bilan SHIFRLANADI; parolsiz HECH NARSA yuborilmaydi (fail-closed).
#   - Ichida .env (barcha kalitlar) bor — kanal YOPIQ bo'lsin va unda faqat egasi bo'lsin.
#   - Token/parol logga, buyruq qatoriga (ps) tushmaydi: parol gpg'ga fd orqali, token faqat curl URL ichida.
#   - Ataylab KIRMAYDI: uploads/face (biometrik selfilar), uploads/messaging (Telegram'da file_id bilan saqlanadi).
#
# Ishga tushirish: /etc/cron.d/wunderkind-tg-backup (har kuni 02:30 Toshkent). Qo'lda: bash infra/backup/telegram-bundle.sh
# Kalitlar asosiy /root/WunderkindLC/.env dan: BACKUP_BOT_TOKEN, BACKUP_CHAT_ID, BACKUP_ENCRYPT_PASSPHRASE.
# =====================================================================================================================
set -uo pipefail

MAIN_DIR="${MAIN_DIR:-/root/WunderkindLC}"
BRANCHES_DIR="${BRANCHES_DIR:-/root/wlc-branches}"
CERTS_DIR="${CERTS_DIR:-/root/certs}"
MAIN_PG="${MAIN_PG:-wunderkindlc-postgres}"
MAIN_UPLOADS_VOL="${MAIN_UPLOADS_VOL:-wunderkindlc_uploads}"
TOOL_IMAGE="${TOOL_IMAGE:-postgres:16-alpine}"   # serverda allaqachon bor — tar uchun (volume ichini o'qish)
MAX_PART=$((45 * 1024 * 1024))                   # Telegram bot hujjat chegarasi ~50 MB — 45 MB bo'laklar

log() { echo "[$(date -u '+%F %T') UTC] $*"; }
envget() { grep -E "^$1=" "$2" 2>/dev/null | tail -1 | cut -d= -f2- | tr -d '\r'; }

TOKEN="$(envget BACKUP_BOT_TOKEN "$MAIN_DIR/.env")"
CHAT="$(envget BACKUP_CHAT_ID "$MAIN_DIR/.env")"
PASS="$(envget BACKUP_ENCRYPT_PASSPHRASE "$MAIN_DIR/.env")"

if [ -z "$TOKEN" ] || [ -z "$CHAT" ] || [ -z "$PASS" ]; then
  log "TO'XTADI: BACKUP_BOT_TOKEN / BACKUP_CHAT_ID / BACKUP_ENCRYPT_PASSPHRASE dan biri yo'q — hech narsa yuborilmadi"
  exit 1
fi

tg_msg() { curl -s -m 60 "https://api.telegram.org/bot$TOKEN/sendMessage" -d chat_id="$CHAT" --data-urlencode text="$1" >/dev/null 2>&1 || true; }
tg_doc() {  # $1 fayl, $2 izoh
  local r
  r="$(curl -s -m 900 -F chat_id="$CHAT" -F caption="$2" -F document=@"$1" "https://api.telegram.org/bot$TOKEN/sendDocument")"
  case "$r" in *'"ok":true'*) log "Telegram OK: $(basename "$1")"; return 0;; esac
  log "XATO: Telegram $(basename "$1"): $(echo "$r" | head -c 200)"; return 1
}

# Bir vaqtda ikki nusxa ishlamasin (qo'lda + cron).
exec 9>/tmp/wunderkind-tg-backup.lock
if ! flock -n 9; then log "boshqa nusxa ishlayapti — chiqildi"; exit 0; fi

TS="$(TZ=Asia/Tashkent date '+%Y%m%d_%H%M')"
WHEN="$(TZ=Asia/Tashkent date '+%Y-%m-%d %H:%M')"
WORK="$(mktemp -d /root/.tg-backup.XXXXXX)"; chmod 700 "$WORK"
trap 'rm -rf "$WORK"' EXIT
ROOT="$WORK/wunderkind_$TS"; mkdir -p "$ROOT"
FAILS=()

# --- bitta nusxa (markaz yoki filial): baza + fayllar + .env -------------------------------------------------------
dump_db() {  # $1 postgres konteyner, $2 chiqish fayli
  # Konteyner ichida o'z POSTGRES_USER/DB bilan (lokal soket — parol kerak emas). pipefail — pg_dump xatosi ushlanadi.
  docker exec "$1" sh -c 'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB"' | gzip > "$2"
}
tar_uploads() {  # $1 volume, $2 chiqish fayli
  docker run --rm -v "$1":/v:ro "$TOOL_IMAGE" tar czf - -C /v --exclude=./face --exclude=./messaging . > "$2"
}
collect() {  # $1 nom (papka), $2 postgres konteyner, $3 uploads volume, $4 .env yo'li
  local d="$ROOT/$1"; mkdir -p "$d"
  if dump_db "$2" "$d/baza.sql.gz" && [ -s "$d/baza.sql.gz" ]; then log "$1: baza OK ($(du -h "$d/baza.sql.gz" | cut -f1))"
  else log "XATO: $1 baza"; FAILS+=("$1 bazasi"); rm -f "$d/baza.sql.gz"; fi
  if docker volume inspect "$3" >/dev/null 2>&1; then
    if tar_uploads "$3" "$d/fayllar.tar.gz"; then log "$1: fayllar OK ($(du -h "$d/fayllar.tar.gz" | cut -f1))"
    else log "XATO: $1 fayllar"; FAILS+=("$1 fayllari"); fi
  else log "OGOHLANTIRISH: $1 — $3 volume yo'q"; fi
  if [ -f "$4" ]; then cp "$4" "$d/env.txt"; chmod 600 "$d/env.txt"; else FAILS+=("$1 .env"); fi
  [ -f "$(dirname "$4")/docker-compose.yml" ] && [ "$1" != "markaz" ] && cp "$(dirname "$4")/docker-compose.yml" "$d/"
}

collect "markaz" "$MAIN_PG" "$MAIN_UPLOADS_VOL" "$MAIN_DIR/.env"

NAMES="markaz"
if [ -d "$BRANCHES_DIR" ]; then
  for b in "$BRANCHES_DIR"/*/; do
    slug="$(basename "$b")"
    [ -f "$b/.env" ] || continue                       # nginx/ kabi papkalar o'tkaziladi
    collect "filial-$slug" "wlc-$slug-postgres" "wlc-${slug}_uploads" "$b/.env"
    NAMES="$NAMES, $slug"
  done
fi

# --- server qismi ---------------------------------------------------------------------------------------------------
mkdir -p "$ROOT/server"
[ -d "$CERTS_DIR" ] && cp -r "$CERTS_DIR" "$ROOT/server/certs"
[ -d "$BRANCHES_DIR/nginx" ] && cp -r "$BRANCHES_DIR/nginx" "$ROOT/server/branches-nginx"
git -C "$MAIN_DIR" log -1 --format='%H %s' > "$ROOT/server/git-commit.txt" 2>/dev/null || true

cat > "$ROOT/TIKLASH.txt" <<'TXT'
WUNDERKIND — SERVERNI NOLDAN TIKLASH (qisqa)
1) Yangi server (Ubuntu + Docker). git clone <GitHub repo> /root/WunderkindLC ; git checkout <server/git-commit.txt dagi commit>
2) markaz/env.txt -> /root/WunderkindLC/.env  (kalitlar AYNAN shu — CREDENTIAL_VAULT_KEY/FACE_VECTOR_KEY almashsa shifrlangan ma'lumot ochilmaydi)
3) server/certs -> /root/certs  (Cloudflare Origin sertifikati)
4) docker compose up -d --build ; keyin bazani tiklash:
     docker compose stop app
     gunzip -c markaz/baza.sql.gz | docker exec -i wunderkindlc-postgres psql -U wunderkindlc -d wunderkindlc
     (baza bo'sh bo'lishi kerak: avval  docker compose down -v  bilan YANGI o'rnatishda qiling)
5) Fayllar: docker run --rm -i -v wunderkindlc_uploads:/v postgres:16-alpine tar xzf - -C /v < markaz/fayllar.tar.gz
6) docker compose up -d ; Cloudflare: tunnel/DNS yangi server IP'siga.
7) Filiallar: filial-<slug>/env.txt -> /root/wlc-branches/<slug>/.env , docker-compose.yml shu papkaga ;
   server/branches-nginx -> /root/wlc-branches/nginx ; infra/branch/update-branches.sh ; bazasi va fayllari 4-5 qadam kabi
   (konteyner wlc-<slug>-postgres, volume wlc-<slug>_uploads, foydalanuvchi/baza — env.txt dagi POSTGRES_USER/POSTGRES_DB).
To'liq: DEPLOY.md / .claude/skills/deploy.
TXT

# --- bitta fayl: tar → gpg (AES-256). Parol fd orqali — ps/argv'da ko'rinmaydi ---------------------------------------
OUT="$WORK/wunderkind_$TS.tar.gz.gpg"
if ! tar czf - -C "$WORK" "wunderkind_$TS" \
   | gpg --batch --yes --pinentry-mode loopback --passphrase-fd 3 --symmetric --cipher-algo AES256 -o "$OUT" 3<<<"$PASS"; then
  log "XATO: arxiv/shifrlash"
  tg_msg "❌ Zaxira XATO ($WHEN): arxivlash yoki shifrlash bajarilmadi — hech narsa yuborilmadi. Serverni tekshiring."
  exit 1
fi
rm -rf "$ROOT"
# Faqat SINOV uchun: shifrlangan faylni berilgan papkada qoldirish (tiklashni tekshirish). Serverda bo'sh.
[ -n "${KEEP_DIR:-}" ] && cp "$OUT" "$KEEP_DIR/"
SIZE="$(du -h "$OUT" | cut -f1)"
log "arxiv tayyor: $SIZE"

NOTE=""; [ ${#FAILS[@]} -gt 0 ] && NOTE=$'\n⚠️ Kirmay qoldi: '"$(IFS=', '; echo "${FAILS[*]}")"
CAPTION="🗄 To'liq zaxira — $WHEN ($SIZE)
Ichida: $NAMES — baza, fayllar, .env; sertifikat.
Ochish: gpg -d fayl.gpg > zaxira.tar.gz (parol: BACKUP_ENCRYPT_PASSPHRASE)$NOTE"

SENT=1
if [ "$(stat -c %s "$OUT")" -le "$MAX_PART" ]; then
  tg_doc "$OUT" "$CAPTION" || SENT=0
else
  # Katta bo'lsa bo'laklarga — yig'ish: cat *.part-* > fayl.gpg
  split -b "$MAX_PART" -d -a 2 "$OUT" "$OUT.part-"
  parts=("$OUT".part-*); n=${#parts[@]}; i=0
  for p in "${parts[@]}"; do
    i=$((i + 1))
    tg_doc "$p" "$CAPTION
Qism $i/$n — yig'ish: cat *.part-* > $(basename "$OUT")" || SENT=0
  done
fi

if [ "$SENT" = 1 ] && [ ${#FAILS[@]} -eq 0 ]; then log "tugadi: hammasi yuborildi"; exit 0; fi
[ "$SENT" = 1 ] || tg_msg "❌ Zaxira ($WHEN): fayl Telegram'ga yuborilmadi — serverdagi /var/log/wunderkind-tg-backup.log ni tekshiring."
exit 1
