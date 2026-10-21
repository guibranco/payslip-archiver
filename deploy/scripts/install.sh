#!/usr/bin/env bash
# Installs or upgrades payslip-archiver on the host it runs on. Idempotent: safe to run on every deploy.
#
#   sudo deploy/scripts/install.sh <release-dir> <env-file>
#
#   <release-dir>  directory holding the published single-file build (payslip-archiver binary,
#                  appsettings*.json) plus deploy/systemd/payslip-archiver.service
#   <env-file>     the rendered environment file with the secrets (becomes /etc/payslip-archiver/env)
#
# Layout on the host:
#   /opt/payslip-archiver/bin          symlink to the current release (swapped atomically on upgrade)
#   /opt/payslip-archiver/releases/    one directory per deployed version, last 3 kept for rollback
#   /var/lib/payslip-archiver          SQLite ledger + single-file extraction cache (survives upgrades)
#   /etc/payslip-archiver/env          secrets, root:root 0600, read by systemd before dropping privileges
set -euo pipefail

RELEASE_DIR="${1:?usage: install.sh <release-dir> <env-file>}"
ENV_FILE="${2:?usage: install.sh <release-dir> <env-file>}"

APP=payslip-archiver
SVC_USER=payslip
APP_ROOT=/opt/${APP}
STATE_DIR=/var/lib/${APP}
CONF_DIR=/etc/${APP}
UNIT_SRC="${RELEASE_DIR}/${APP}.service"
UNIT_DST="/etc/systemd/system/${APP}.service"
VERSION="$(cat "${RELEASE_DIR}/VERSION" 2>/dev/null || true)"   # written by the deploy workflow
STAMP="$(date -u +%Y%m%d%H%M%S)"

if [[ $EUID -ne 0 ]]; then
  echo "install.sh must run as root (use sudo)" >&2
  exit 1
fi
[[ -x "${RELEASE_DIR}/${APP}" ]] || { echo "missing executable ${RELEASE_DIR}/${APP}" >&2; exit 1; }
[[ -f "${UNIT_SRC}" ]] || { echo "missing systemd unit ${UNIT_SRC}" >&2; exit 1; }
[[ -f "${ENV_FILE}" ]] || { echo "missing env file ${ENV_FILE}" >&2; exit 1; }

echo "▶ Preparing user and directories"
if ! id -u "${SVC_USER}" >/dev/null 2>&1; then
  useradd --system --home "${APP_ROOT}" --shell /usr/sbin/nologin "${SVC_USER}"
fi
install -d -m 755 "${APP_ROOT}" "${APP_ROOT}/releases"
install -d -m 750 -o "${SVC_USER}" -g "${SVC_USER}" "${STATE_DIR}"
install -d -m 750 -o root -g "${SVC_USER}" "${CONF_DIR}"

echo "▶ Installing secrets"
install -m 600 -o root -g root "${ENV_FILE}" "${CONF_DIR}/env"

echo "▶ Staging release ${STAMP}${VERSION:+ (${VERSION})}"
NEW_RELEASE="${APP_ROOT}/releases/${STAMP}"
install -d -m 755 "${NEW_RELEASE}"
cp -a "${RELEASE_DIR}/." "${NEW_RELEASE}/"
rm -f "${NEW_RELEASE}/${APP}.service" "${NEW_RELEASE}/${APP}.env" "${NEW_RELEASE}/install.sh"
chown -R root:root "${NEW_RELEASE}"
chmod 755 "${NEW_RELEASE}/${APP}"

echo "▶ Installing systemd unit"
install -m 644 "${UNIT_SRC}" "${UNIT_DST}"
systemctl daemon-reload

echo "▶ Switching ${APP_ROOT}/bin to the new release"
if systemctl is-active --quiet "${APP}"; then
  systemctl stop "${APP}"
fi
if [[ -d "${APP_ROOT}/bin" && ! -L "${APP_ROOT}/bin" ]]; then
  # A hand-installed copy from before the symlink layout: keep it as a release so it can be rolled back to.
  mv "${APP_ROOT}/bin" "${APP_ROOT}/releases/manual-${STAMP}"
fi
ln -sfn "${NEW_RELEASE}" "${APP_ROOT}/bin.new"
mv -Tf "${APP_ROOT}/bin.new" "${APP_ROOT}/bin"   # atomic symlink swap

echo "▶ Starting service"
systemctl enable --quiet "${APP}"
systemctl restart "${APP}"

# A failed start usually means bad config: surface the journal instead of leaving a dead unit behind.
for i in $(seq 1 10); do
  if systemctl is-active --quiet "${APP}"; then
    echo "✅ ${APP} is active (attempt ${i})"
    break
  fi
  if systemctl is-failed --quiet "${APP}"; then
    echo "❌ ${APP} failed to start" >&2
    journalctl -u "${APP}" -n 50 --no-pager >&2
    exit 1
  fi
  sleep 2
done
systemctl is-active --quiet "${APP}" || { echo "❌ ${APP} did not become active" >&2; journalctl -u "${APP}" -n 50 --no-pager >&2; exit 1; }

echo "▶ Pruning old releases (keeping the last 3)"
ls -1dt "${APP_ROOT}"/releases/*/ 2>/dev/null | tail -n +4 | xargs -r rm -rf

echo "▶ Recent log lines"
journalctl -u "${APP}" -n 20 --no-pager || true
echo "✅ Deployed ${VERSION:-${STAMP}}"
