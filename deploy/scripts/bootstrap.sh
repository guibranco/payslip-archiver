#!/usr/bin/env bash
# One-time host preparation for payslip-archiver. Run as root, from a repository checkout or an
# extracted release tarball:
#
#   sudo deploy/scripts/bootstrap.sh [deploy-user]        # repo checkout (default deploy user: "deploy")
#   sudo ./bootstrap.sh [deploy-user]                      # extracted release tarball
#
# It installs the parts that must be trusted and therefore must NOT come from a deploy upload:
#   /usr/local/sbin/payslip-archiver-install   root-owned copy of install.sh (what sudoers allows)
#   /etc/systemd/system/payslip-archiver.service
#   /etc/sudoers.d/payslip-archiver            lets the deploy user run only that installer
# plus the service user and directories. Re-run it whenever install.sh or the unit changes in the
# repository (install.sh prints a warning when the copy shipped in a release differs from the one
# installed here). The deploy user's SSH key is left for you to add.
set -euo pipefail

DEPLOY_USER="${1:-deploy}"
APP=payslip-archiver
SVC_USER=payslip
APP_ROOT=/opt/${APP}
STATE_DIR=/var/lib/${APP}
CONF_DIR=/etc/${APP}
INSTALLER_DST=/usr/local/sbin/${APP}-install
UNIT_DST=/etc/systemd/system/${APP}.service
SUDOERS_DST=/etc/sudoers.d/${APP}

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# Repository layout first, then the flat layout of a release tarball.
INSTALLER_SRC="${HERE}/install.sh"
UNIT_SRC="${HERE}/../systemd/${APP}.service"
[[ -f "${UNIT_SRC}" ]] || UNIT_SRC="${HERE}/${APP}.service"

if [[ $EUID -ne 0 ]]; then
  echo "bootstrap.sh must run as root (use sudo)" >&2
  exit 1
fi
[[ -f "${INSTALLER_SRC}" ]] || { echo "missing ${INSTALLER_SRC}" >&2; exit 1; }
[[ -f "${UNIT_SRC}" ]] || { echo "missing systemd unit next to bootstrap.sh" >&2; exit 1; }
[[ "${DEPLOY_USER}" =~ ^[a-z_][a-z0-9_-]*$ ]] || { echo "invalid deploy user name '${DEPLOY_USER}'" >&2; exit 1; }

echo "▶ Service user and directories"
if ! id -u "${SVC_USER}" >/dev/null 2>&1; then
  useradd --system --home "${APP_ROOT}" --shell /usr/sbin/nologin "${SVC_USER}"
fi
install -d -m 755 "${APP_ROOT}" "${APP_ROOT}/releases"
install -d -m 750 -o "${SVC_USER}" -g "${SVC_USER}" "${STATE_DIR}"
install -d -m 750 -o root -g "${SVC_USER}" "${CONF_DIR}"

echo "▶ Deploy user '${DEPLOY_USER}'"
if ! id -u "${DEPLOY_USER}" >/dev/null 2>&1; then
  useradd --create-home --shell /bin/bash "${DEPLOY_USER}"
fi
DEPLOY_HOME="$(getent passwd "${DEPLOY_USER}" | cut -d: -f6)"
install -d -m 700 -o "${DEPLOY_USER}" -g "${DEPLOY_USER}" "${DEPLOY_HOME}/.ssh"
touch "${DEPLOY_HOME}/.ssh/authorized_keys"
chown "${DEPLOY_USER}:${DEPLOY_USER}" "${DEPLOY_HOME}/.ssh/authorized_keys"
chmod 600 "${DEPLOY_HOME}/.ssh/authorized_keys"

echo "▶ Trusted installer → ${INSTALLER_DST}"
install -m 755 -o root -g root "${INSTALLER_SRC}" "${INSTALLER_DST}"
bash -n "${INSTALLER_DST}"

echo "▶ systemd unit → ${UNIT_DST}"
install -m 644 -o root -g root "${UNIT_SRC}" "${UNIT_DST}"
systemctl daemon-reload

echo "▶ sudoers rule → ${SUDOERS_DST}"
SUDOERS_TMP="$(mktemp)"
printf '%s ALL=(root) NOPASSWD: %s\n' "${DEPLOY_USER}" "${INSTALLER_DST}" > "${SUDOERS_TMP}"
visudo -cf "${SUDOERS_TMP}" >/dev/null
install -m 440 -o root -g root "${SUDOERS_TMP}" "${SUDOERS_DST}"
rm -f "${SUDOERS_TMP}"

echo
echo "✅ Host prepared."
echo
echo "This host's SSH key fingerprints — compare them with the output of 'ssh-keyscan -p PORT HOST' run from a"
echo "trusted machine before storing that output as the SSH_KNOWN_HOSTS secret:"
for key in /etc/ssh/ssh_host_*_key.pub; do
  if [[ -f "${key}" ]]; then
    ssh-keygen -lf "${key}" 2>/dev/null | sed 's/^/    /' || true
  fi
done
cat <<EOF

Next steps:
  1. Put the public half of the SSH_PRIVATE_KEY repository secret in ${DEPLOY_HOME}/.ssh/authorized_keys
  2. Set SSH_HOST=$(hostname -f 2>/dev/null || hostname), SSH_USER=${DEPLOY_USER}, SSH_KNOWN_HOSTS (see above)
     and the application secrets in the repository
  3. Merge to main — the Deploy workflow uploads the release and runs: sudo ${INSTALLER_DST} <release-dir> <env-file>
EOF
