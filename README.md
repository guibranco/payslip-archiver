# 📬 🔓 payslip-archiver

[![CI](https://github.com/guibranco/payslip-archiver/actions/workflows/ci.yml/badge.svg)](https://github.com/guibranco/payslip-archiver/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)

Watches a payroll mailbox, strips the password from payslip PDFs and archives clean copies to Dropbox.

Payroll providers e-mail payslips as password-protected PDFs, always with the same password. That is
fine for transit and useless for an archive: every time you need a slip you are typing the password again.
This small .NET service polls a dedicated IMAP (or POP3) mailbox, picks out the payslip e-mails, removes
the password from each PDF and files the result under `/<year>/<yyyy-MM> Payslip.pdf` in Dropbox — or in
any local folder you point it at.

## How it works

```mermaid
flowchart LR
    M[(Payroll<br/>mailbox)] -->|IMAP / POP3<br/>MailKit| F{Filter<br/>sender · subject · *.pdf}
    F -->|match| D[Decrypt<br/>PDFsharp or qpdf]
    D --> H[SHA-256<br/>duplicate check]
    H -->|new| S[Store<br/>Dropbox or folder]
    S --> L[(SQLite<br/>ledger)]
    L --> K[Mark handled<br/>seen + move to Processed]
    F -->|no match| L
```

Each cycle:

1. opens one mailbox session and lists candidate messages (unread, from the configured sender);
2. skips anything the **ledger** already knows — archived, ignored, or past its retry budget;
3. fetches the rest, decrypts every matching PDF attachment and hashes the result;
4. uploads each new document (Dropbox never overwrites — a name clash gets auto-renamed);
5. records the outcome in the ledger **before** flagging the e-mail as seen / moving it to `Processed`.

That ordering is what makes the whole thing idempotent: a crash between upload and flagging means the
next cycle sees the ledger entry and skips the upload. A bad message (wrong password, corrupt file) is
retried up to `Pipeline:MaxAttempts` times and then left alone with a warning in the log, instead of
blocking everything behind it.

## Quick start (local, no Dropbox)

```bash
git clone https://github.com/guibranco/payslip-archiver && cd payslip-archiver

# secrets stay out of appsettings.json
cd src/PayslipArchiver.Worker
dotnet user-secrets set "Mailbox:Host"     "mail.example.com"
dotnet user-secrets set "Mailbox:Username" "payslips@example.com"
dotnet user-secrets set "Mailbox:Password" "…"
dotnet user-secrets set "Pdf:Password"     "…"
dotnet user-secrets set "Filter:FromContains" "@payroll-provider.example"

# Development profile: runs once, writes to ./archive, keeps the ledger in ./data
DOTNET_ENVIRONMENT=Development dotnet run
```

Check `./archive/<year>/…` and the log output, then move on to Dropbox.

## Dropbox setup (once)

1. Create an app at <https://www.dropbox.com/developers/apps>: *Scoped access* → **App folder**
   (the archive then lives in `/Apps/<your app name>` and the token can see nothing else).
2. On the *Permissions* tab tick `files.content.write` and `files.metadata.read`.
3. Put the app key in config and run the one-off authorisation:

   ```bash
   dotnet user-secrets set "Dropbox:AppKey" "…"
   dotnet run -- authorize-dropbox
   ```

   It prints a URL; approve the app in the browser, paste the code back, and it prints a **refresh
   token**. No app secret is needed — the PKCE flow is used when `Dropbox:AppSecret` is empty.
4. Store it:

   ```bash
   dotnet user-secrets set "Dropbox:RefreshToken" "…"
   ```

With `Archive:Provider=Dropbox` (the default) the service now uploads straight to the app folder.
If the Dropbox desktop client runs on the same machine you can skip all of this and use
`Archive:Provider=FileSystem` with `Archive:Directory` pointing inside your synced folder.

## Configuration

Everything is plain `IConfiguration`: `appsettings.json`, user secrets, or environment variables
with `__` as the separator (`Mailbox__Password`). The service validates required settings at startup
and refuses to start with a clear message when something is missing.

| Section · key | Default | Notes |
|---|---|---|
| `Mailbox:Protocol` | `Imap` | `Imap` or `Pop3`. POP3 needs server UIDL support and cannot flag messages, so the ledger does all the de-duplication. |
| `Mailbox:Host` / `Port` / `UseSsl` | – / `993` / `true` | `UseSsl=false` means STARTTLS when offered. |
| `Mailbox:Username` / `Password` | – | Use an app password where the provider offers one. |
| `Mailbox:Folder` | `INBOX` | IMAP folder to watch. |
| `Mailbox:ProcessedFolder` | `Processed` | IMAP: handled messages are moved here (created on demand). Empty = leave in place. |
| `Mailbox:OnlyUnseen` | `true` | IMAP: only unread messages are candidates. |
| `Mailbox:DeleteAfterProcessing` | `false` | POP3: delete handled messages from the server. |
| `Filter:FromContains` | – | Case-insensitive substring of the sender, e.g. `@payroll-provider.example`. |
| `Filter:SubjectContains` | – | Case-insensitive substring of the subject. |
| `Filter:AttachmentPattern` | `*.pdf` | Glob on the attachment name. Only PDFs are ever considered. |
| `Pdf:Password` | – | **Required.** The provider's password. |
| `Pdf:Engine` | `PdfSharp` | `PdfSharp` (in-process) or `Qpdf` (shells out to `qpdf`, handles anything). |
| `Naming:PathTemplate` | `/{year}/{period} Payslip.pdf` | Tokens: `{year}` `{month}` `{period}` (`yyyy-MM`) `{original}` `{ext}`. |
| `Naming:PeriodOffsetMonths` | `0` | Used only when no period can be read from the file name or subject: shifts the e-mail's month (e.g. `-1` when slips arrive early the next month). |
| `Archive:Provider` | `Dropbox` | `Dropbox` or `FileSystem`. |
| `Archive:Directory` | `archive` | Root folder for `FileSystem`. |
| `Dropbox:AppKey` / `RefreshToken` | – | **Required** for the Dropbox provider. `AppSecret` is optional (PKCE). |
| `Dropbox:RootPath` | `/` | Folder under which the template is rendered. |
| `Ledger:Path` | `data/ledger.db` | SQLite file; created on first run. |
| `Pipeline:MaxMessagesPerCycle` | `50` | Backlog is drained gradually. |
| `Pipeline:MaxAttempts` | `5` | Retry budget per message. |
| `Schedule:Interval` | `00:30:00` | Time between cycles (the first runs at start-up). |
| `Schedule:RunOnce` | `false` | Run one cycle and exit (exit code 1 if anything failed) — for cron / systemd timers. |
| `Heartbeat:PingUrl` | – | Healthchecks.io ping URL; `/start` and `/fail` are appended automatically. Empty disables it. |

### Pay period detection

The month a slip is filed under is read, in order, from the attachment name, the subject, and finally
the e-mail date. Recognised shapes include `2026-10`, `202610`, `2026-10-15`, `10/2026`, `Oct 2026`,
`October2026` and `2026 October`. Anything else falls back to the e-mail date plus
`Naming:PeriodOffsetMonths`.

## Deploying

**systemd (recommended for a VM).** Publish a self-contained single file and install the unit:

```bash
dotnet publish src/PayslipArchiver.Worker -c Release -r linux-x64 -o publish/linux-x64
```

Then follow the comments at the top of [`deploy/systemd/payslip-archiver.service`](deploy/systemd/payslip-archiver.service)
(secrets go in `/etc/payslip-archiver/env`, template in [`deploy/systemd/payslip-archiver.env`](deploy/systemd/payslip-archiver.env)).
The host integrates with systemd (`Type=notify`) and logs to the journal. CI uploads the same publish
output as an artifact on every push to `main`.

**Docker.** `deploy/docker-compose.yml` builds the image and keeps the ledger in a named volume:

```bash
cp deploy/systemd/payslip-archiver.env deploy/.env   # fill in the secrets
docker compose -f deploy/docker-compose.yml up -d
```

**Cron instead of a daemon.** Set `Schedule__RunOnce=true` and let cron or a systemd timer invoke
the binary; the exit code tells the scheduler whether the cycle was clean.

## Project layout

```
src/
  PayslipArchiver.Domain          pure model: messages, attachments, pay periods, path template
  PayslipArchiver.Application     the archive use case, message filter, ports (interfaces), options
  PayslipArchiver.Infrastructure  MailKit (IMAP/POP3), PDFsharp + qpdf, Dropbox + file system, SQLite, Healthchecks.io
  PayslipArchiver.Worker          host: BackgroundService + PeriodicTimer, systemd integration, authorize-dropbox command
tests/
  PayslipArchiver.Domain.Tests
  PayslipArchiver.Application.Tests   pipeline behaviour with NSubstitute fakes
  PayslipArchiver.Infrastructure.Tests  real encrypted PDF fixtures (RC4, AES-128, AES-256), SQLite, MIME mapping
deploy/                            systemd unit + env template, docker-compose
scripts/make-fixtures.sh           regenerates the encrypted test PDFs with qpdf
```

### Why PDFsharp 6.2.x

Payroll providers hand out only the *user* password. PDFsharp refuses to open such a document for
modification, so the decryptor opens it in *Import* mode and copies the pages into a brand-new,
unencrypted document instead — content and resources come along, the security handler does not.

PDFsharp is pinned to **6.2.4**, the last MIT-licensed release: 6.5.0 moved to AGPL-3.0 / commercial and
requires a licence call at start-up. 6.2.4 already reads AES-256 (R6) files, which covers everything seen
in the wild. Set `Pdf:Engine=Qpdf` if a provider ever ships something PDFsharp cannot open.

## Development

```bash
dotnet build          # warnings are errors; analyzers at latest-recommended
dotnet test           # 96 tests; the qpdf ones skip themselves when qpdf is not installed
scripts/make-fixtures.sh   # only if you change the test PDFs (needs qpdf + python3)
```

Commits that add a feature carry `+semver: minor` in the message; GitVersion stamps the build.

## License

[MIT](LICENSE) © Guilherme Branco Stracini
