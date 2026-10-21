# Test fixtures

Tiny single-page PDFs used by the decryptor tests. The user password for every encrypted file is
the first line of `password.txt` (read by both the tests and the generator script); the owner
password, where different, is `owner-pass-unknown` and deliberately *not* known to the tests —
payroll providers only ever hand out the user password.

| File               | Encryption                          | Notes                               |
|--------------------|-------------------------------------|-------------------------------------|
| `plain.pdf`        | none                                | source of all the others            |
| `rc4-128-user.pdf` | RC4 128-bit (R3), user password     | legacy scheme, still seen in the wild |
| `aes128-user.pdf`  | AES-128 (R4), user password         |                                     |
| `aes256-user.pdf`  | AES-256 (R6), user password         | what most providers emit today      |
| `aes256-same.pdf`  | AES-256 (R6), user == owner password|                                     |

Regenerate with qpdf (the base PDF is written by hand in `scripts/make-fixtures.sh`):

```bash
scripts/make-fixtures.sh
```
