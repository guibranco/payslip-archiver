#!/usr/bin/env bash
# Regenerates the encrypted PDF fixtures used by the Infrastructure tests. Requires qpdf and python3.
set -euo pipefail

FIXTURES="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/tests/PayslipArchiver.Infrastructure.Tests/Fixtures"
# The user password is shared with the tests through Fixtures/password.txt; the owner password is
# deliberately different and never used by the tests (providers only hand out the user password).
PASSWORD="$(head -n 1 "$FIXTURES/password.txt")"
OWNER='owner-pass-unknown'

python3 - "$FIXTURES/plain.pdf" <<'EOF'
import sys

content = b"BT /F1 24 Tf 72 770 Td (Payslip fixture 2026-10) Tj ET"
objs = [
    b"<< /Type /Catalog /Pages 2 0 R >>",
    b"<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
    b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
    b"<< /Length %d >>\nstream\n" % len(content) + content + b"\nendstream",
    b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
    b"<< /Title (Payslip fixture) /Producer (payslip-archiver tests) >>",
]
out = bytearray(b"%PDF-1.4\n%\xe2\xe3\xcf\xd3\n")
offsets = []
for i, body in enumerate(objs, start=1):
    offsets.append(len(out))
    out += b"%d 0 obj\n" % i + body + b"\nendobj\n"
xref = len(out)
out += b"xref\n0 %d\n" % (len(objs) + 1)
out += b"0000000000 65535 f \n"
for off in offsets:
    out += b"%010d 00000 n \n" % off
out += b"trailer\n<< /Size %d /Root 1 0 R /Info 6 0 R >>\nstartxref\n%d\n%%%%EOF\n" % (len(objs) + 1, xref)
open(sys.argv[1], "wb").write(out)
EOF

cd "$FIXTURES"
qpdf --check plain.pdf > /dev/null
qpdf --encrypt "$PASSWORD" "$OWNER" 128 --use-aes=y -- plain.pdf aes128-user.pdf
qpdf --encrypt "$PASSWORD" "$OWNER" 256 -- plain.pdf aes256-user.pdf
qpdf --encrypt "$PASSWORD" "$PASSWORD" 256 -- plain.pdf aes256-same.pdf
qpdf --allow-weak-crypto --encrypt "$PASSWORD" "$OWNER" 128 --use-aes=n -- plain.pdf rc4-128-user.pdf

ls -l "$FIXTURES"/*.pdf
