#!/usr/bin/env bash
#
# Generate the local-dev CA + `*.myhrm.org` leaf for the subdomain-TLS rig
# (docker-compose.tls.yml / nginx.docker.conf). Output lands in
# `local-dev/certs/` which is gitignored — keys/certs never enter the repo.
# Re-run any time to regenerate (existing private keys are reused).
#
#   ./local-dev/gen-dev-certs.sh
#
# Then TRUST the CA so browsers/curl accept https://<tenant>.myhrm.org:
#   # system (curl, apt tools):
#   sudo cp local-dev/certs/ca.crt /usr/local/share/ca-certificates/hrm-dev-ca.crt
#   sudo update-ca-certificates
#   # Chrome/Chromium use their own NSS store (needs libnss3-tools):
#   mkdir -p "$HOME/.pki/nssdb" && certutil -d sql:"$HOME/.pki/nssdb" -N --empty-password 2>/dev/null || true
#   certutil -d sql:"$HOME/.pki/nssdb" -A -n "HRM Local Dev CA" -t "C,," -i local-dev/certs/ca.crt
#   # Firefox: import local-dev/certs/ca.crt via Settings -> Certificates -> Authorities.
#
# WHY THIS SCRIPT EXISTS (DF-tls-ca-basicconstraints): a CA cert generated
# without `basicConstraints=critical,CA:TRUE` is rejected by OpenSSL 3.x,
# Chrome, and curl on modern Linux ("error 79 invalid CA certificate"). The
# CA block below sets it explicitly so the rig works out of the box.
set -euo pipefail

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/certs"
mkdir -p "$DIR"
cd "$DIR"

# --- 1. Local root CA (self-signed; CA:TRUE is the load-bearing bit) ---
[ -f ca.key ] || openssl genrsa -out ca.key 4096
openssl req -x509 -new -nodes -key ca.key -sha256 -days 3650 \
  -subj "/CN=HRM Local Dev CA" \
  -addext "basicConstraints=critical,CA:TRUE,pathlen:0" \
  -addext "keyUsage=critical,keyCertSign,cRLSign" \
  -out ca.crt

# --- 2. Wildcard leaf for *.myhrm.org, signed by the CA ---
[ -f myhrm.key ] || openssl genrsa -out myhrm.key 2048
openssl req -new -key myhrm.key -subj "/CN=*.myhrm.org" -out myhrm.csr

# Leaf extensions (SANs cover the apex, wildcard subdomains, and localhost).
cat > ext.cnf <<'EXT'
subjectAltName=DNS:*.myhrm.org,DNS:myhrm.org,DNS:localhost,IP:127.0.0.1
basicConstraints=CA:FALSE
keyUsage=digitalSignature,keyEncipherment
extendedKeyUsage=serverAuth
EXT

openssl x509 -req -in myhrm.csr -CA ca.crt -CAkey ca.key -CAcreateserial \
  -days 730 -sha256 -extfile ext.cnf -out myhrm.crt

# nginx serves the fullchain (leaf + CA).
cat myhrm.crt ca.crt > myhrm.fullchain.crt

# --- 3. Verify + report ---
openssl verify -CAfile ca.crt myhrm.crt
echo "OK -> $DIR (CA:TRUE)."
echo "CA:        $(openssl x509 -in ca.crt   -noout -ext basicConstraints | tr -d '\n')"
echo "leaf SANs: $(openssl x509 -in myhrm.crt -noout -ext subjectAltName | tail -1 | xargs)"
echo "Next: trust ca.crt (see the header of this script), then \`docker compose -f docker-compose.yml -f docker-compose.tls.yml up -d\`."
