#!/usr/bin/env bash
# Installs or upgrades payslip-archiver on the host it runs on. Idempotent: safe to run on every deploy.
#
# This script is installed ONCE by bootstrap.sh as the root-owned /usr/local/sbin/payslip-archiver-install,
# which is the only command the deploy user may run with sudo. Everything it receives as arguments is
# untrusted (the deploy user controls it), so it never executes or installs anything from the release
# as root: the binary only ever runs as the unprivileged service user, and the systemd unit and this
# script are only ever taken from the trusted copies bootstrap.sh put in place.
#
#   sudo /usr/local/sbin/payslip-archiver-install <release-dir> <env-file>
#
#   <release-dir>  absolute path of the extracted release (payslip-archiver binary, appsettings*.json, VERSION)
#   <env-file>     absolute path of the rendered environment file (becomes /etc/payslip-archiver/env)
#
# Layout on the host:
#   /opt/payslip-archiver/bin          symlink to the current release (swapped atomically on upgrade)
#   /opt/payslip-archiver/releases/    one directory per deployed version, last 3 kept for rollback
#   /var/lib/payslip-archiver          SQLite ledger + single-file extraction cache (survives upgrades)
#   /etc/payslip-archiver/env          secrets, root:root 0600, read by systemd before dropping privileges
#
# If the new release fails to become active, the previous release (and its env file) are put back and
# the script exits non-zero, so a bad deploy never leaves the service down.
set -euo pipefail

RELEASE_DIR="${1:?usage: payslip-archiver-install <release-dir> <env-file>}"
ENV_FILE="${2:?usage: payslip-archiver-install <release-dir> <env-file>}"

APP=payslip-archiver
SVC_USER=payslip
APP_ROOT=/opt/${APP}
STATE_DIR=/var/lib/${APP}
CONF_DIR=/etc/${APP}
UNIT_DST=/etc/systemd/system/${APP}.service
INSTALLER_DST=/usr/local/sbin/${APP}-install
STAMP="$(date -u +%Y%m%d%H%M%S)"

fail() { echo "❌ $*" >&2; exit 1; }
warn() { echo "WARNING: $*" >&2; }

# ── Preconditions ────────────────────────────────────────────────────────────
[[ $EUID -eq 0 ]] || fail "must run as root (use sudo)"
[[ "${RELEASE_DIR}" == /* && "${ENV_FILE}" == /* ]] || fail "release dir and env file must be absolute paths"
[[ -d "${RELEASE_DIR}" && ! -L "${RELEASE_DIR}" ]] || fail "release dir ${RELEASE_DIR} is not a directory"
[[ -f "${ENV_FILE}" && ! -L "${ENV_FILE}" ]] || fail "env file ${ENV_FILE} is not a regular file"
[[ -f "${RELEASE_DIR}/${APP}" && ! -L "${RELEASE_DIR}/${APP}" ]] || fail "missing executable ${RELEASE_DIR}/${APP}"
[[ -f "${UNIT_DST}" ]] || fail "${UNIT_DST} is not installed — run bootstrap.sh as root first"
id -u "${SVC_USER}" >/dev/null 2>&1 || fail "service user ${SVC_USER} does not exist — run bootstrap.sh as root first"

# The release was extracted by the deploy user: refuse anything that is not a plain file or directory,
# so a planted symlink can never make the chown/chmod below touch something outside the release.
if find "${RELEASE_DIR}" ! -type f ! -type d -print -quit | grep -q .; then
  fail "release dir contains symlinks or special files"
fi

VERSION="$(head -n1 "${RELEASE_DIR}/VERSION" 2>/dev/null | tr -cd '[:alnum:].+-' || true)"

# The unit and this installer ship in every release for bootstrap.sh; they are NOT installed from here.
if [[ -f "${RELEASE_DIR}/${APP}.service" ]] && ! cmp -s "${RELEASE_DIR}/${APP}.service" "${UNIT_DST}"; then
  warn "the systemd unit in this release differs from ${UNIT_DST}; re-run bootstrap.sh as root to apply it"
fi
if [[ -f "${RELEASE_DIR}/install.sh" ]] && ! cmp -s "${RELEASE_DIR}/install.sh" "${INSTALLER_DST}"; then
  warn "install.sh in this release differs from ${INSTALLER_DST}; re-run bootstrap.sh as root to apply it"
fi

# ── Helpers ──────────────────────────────────────────────────────────────────
wait_active() {
  local i
  for i in $(seq 1 15); do
    if systemctl is-active --quiet "${APP}"; then
      return 0
    fi
    if systemctl is-failed --quiet "${APP}"; then
      return 1
    fi
    sleep 2
  done
  systemctl is-active --quiet "${APP}"
}

switch_bin_to() {
  ln -sfn "$1" "${APP_ROOT}/bin.new"
  mv -Tf "${APP_ROOT}/bin.new" "${APP_ROOT}/bin"   # atomic symlink swap
}

# ── Stage ────────────────────────────────────────────────────────────────────
echo "▶ Preparing directories"
install -d -m 755 "${APP_ROOT}" "${APP_ROOT}/releases"
install -d -m 750 -o "${SVC_USER}" -g "${SVC_USER}" "${STATE_DIR}"
install -d -m 750 -o root -g "${SVC_USER}" "${CONF_DIR}"

PREVIOUS_RELEASE="$(readlink -f "${APP_ROOT}/bin" 2>/dev/null || true)"
NEW_RELEASE="${APP_ROOT}/releases/${STAMP}"

echo "▶ Staging release ${STAMP}${VERSION:+ (${VERSION})}"
install -d -m 755 "${NEW_RELEASE}"
cp -R --no-dereference --preserve=timestamps "${RELEASE_DIR}/." "${NEW_RELEASE}/"
rm -f "${NEW_RELEASE}/${APP}.service" "${NEW_RELEASE}/${APP}.env" "${NEW_RELEASE}/install.sh" "${NEW_RELEASE}/bootstrap.sh"
chown -R root:root "${NEW_RELEASE}"
chmod -R u=rwX,go=rX "${NEW_RELEASE}"           # strips any setuid/setgid bits that came with the upload
chmod 755 "${NEW_RELEASE}/${APP}"

echo "▶ Installing secrets"
rm -f "${CONF_DIR}/env.previous"                 # left behind only by a rolled-back deploy
if [[ -f "${CONF_DIR}/env" ]]; then
  cp -p "${CONF_DIR}/env" "${CONF_DIR}/env.previous"
fi
install -m 600 -o root -g root "${ENV_FILE}" "${CONF_DIR}/env"

# ── Switch ───────────────────────────────────────────────────────────────────
echo "▶ Switching ${APP_ROOT}/bin to the new release"
if systemctl is-active --quiet "${APP}"; then
  systemctl stop "${APP}"
fi
if [[ -d "${APP_ROOT}/bin" && ! -L "${APP_ROOT}/bin" ]]; then
  # A hand-installed copy from before the symlink layout: keep it as a release so it can be rolled back to.
  mv "${APP_ROOT}/bin" "${APP_ROOT}/releases/manual-${STAMP}"
  PREVIOUS_RELEASE="${APP_ROOT}/releases/manual-${STAMP}"
fi
switch_bin_to "${NEW_RELEASE}"

echo "▶ Starting service"
systemctl enable --quiet "${APP}"
systemctl restart "${APP}" || true

if ! wait_active; then
  echo "❌ ${APP} did not become active on the new release" >&2
  journalctl -u "${APP}" -n 50 --no-pager >&2 || true
  mv "${NEW_RELEASE}" "${NEW_RELEASE}-failed" || true

  if [[ -n "${PREVIOUS_RELEASE}" && -d "${PREVIOUS_RELEASE}" && -x "${PREVIOUS_RELEASE}/${APP}" ]]; then
    echo "↩️  Rolling back to $(basename "${PREVIOUS_RELEASE}")" >&2
    if [[ -f "${CONF_DIR}/env.previous" ]]; then
      install -m 600 -o root -g root "${CONF_DIR}/env.previous" "${CONF_DIR}/env"
    fi
    switch_bin_to "${PREVIOUS_RELEASE}"
    systemctl restart "${APP}" || true
    if wait_active; then
      echo "↩️  Rolled back; ${APP} is active on the previous release" >&2
    else
      echo "❌ The previous release did not come back either — manual intervention needed" >&2
      journalctl -u "${APP}" -n 50 --no-pager >&2 || true
    fi
  else
    echo "❌ No previous release to roll back to" >&2
  fi
  exit 1
fi
echo "✅ ${APP} is active"

# ── Housekeeping ─────────────────────────────────────────────────────────────
echo "▶ Pruning old releases (keeping the current one and the 2 before it)"
# (grep exits 1 when nothing but the current release exists; that is not an error under pipefail.)
{ ls -1dt "${APP_ROOT}"/releases/*/ 2>/dev/null | grep -vxF "${NEW_RELEASE}/" || true; } \
  | tail -n +3 \
  | xargs -r rm -rf
rm -f "${CONF_DIR}/env.previous"

echo "▶ Recent log lines"
journalctl -u "${APP}" -n 20 --no-pager || true
echo "✅ Deployed ${VERSION:-${STAMP}}"
