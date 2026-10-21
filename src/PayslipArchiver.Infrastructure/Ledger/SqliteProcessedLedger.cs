using System.Data;
using System.Globalization;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

using PayslipArchiver.Application.Abstractions;
using PayslipArchiver.Domain.Archiving;
using PayslipArchiver.Domain.Mail;

namespace PayslipArchiver.Infrastructure.Ledger;

/// <summary>
/// SQLite-backed ledger. Two tables: one row per message key, one row per archived document (keyed by
/// content hash). The schema is created on first use, so there is nothing to migrate for a fresh install.
/// </summary>
public sealed class SqliteProcessedLedger : IProcessedLedger, IDisposable
{
    private const string Schema = """
        CREATE TABLE IF NOT EXISTS messages (
            key          TEXT    PRIMARY KEY,
            status       TEXT    NOT NULL,
            attempts     INTEGER NOT NULL DEFAULT 0,
            message_id   TEXT    NULL,
            sender       TEXT    NULL,
            subject      TEXT    NULL,
            received_at  TEXT    NULL,
            note         TEXT    NULL,
            last_error   TEXT    NULL,
            created_at   TEXT    NOT NULL,
            updated_at   TEXT    NOT NULL
        );

        CREATE TABLE IF NOT EXISTS documents (
            sha256           TEXT    PRIMARY KEY,
            message_key      TEXT    NOT NULL REFERENCES messages(key),
            archive_path     TEXT    NOT NULL,
            period           TEXT    NOT NULL,
            source_file_name TEXT    NOT NULL,
            size_bytes       INTEGER NOT NULL,
            archived_at      TEXT    NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_documents_message_key ON documents(message_key);
        """;

    private readonly string _connectionString;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _schemaGate = new(1, 1);
    private volatile bool _schemaReady;

    public SqliteProcessedLedger(IOptions<LedgerOptions> options, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var path = Path.GetFullPath(options.Value.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
        }.ToString();
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<LedgerEntry?> FindAsync(string key, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT status, attempts, last_error, updated_at FROM messages WHERE key = $key";
        command.Parameters.AddWithValue("$key", key);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new LedgerEntry(
            key,
            Enum.Parse<ProcessingStatus>(reader.GetString(0)),
            reader.GetInt32(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture));
    }

    public async Task<bool> IsContentArchivedAsync(string sha256, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM documents WHERE sha256 = $sha256 LIMIT 1";
        command.Parameters.AddWithValue("$sha256", sha256);

        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
    }

    public async Task RecordArchivedAsync(IncomingMessage message, IReadOnlyList<ArchivedDocument> documents, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(documents);

        var now = Now();
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await UpsertMessageAsync(connection, transaction, message.Key, ProcessingStatus.Archived, message, note: null, error: null, now, cancellationToken).ConfigureAwait(false);

        foreach (var document in documents)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = (SqliteTransaction)transaction;
            insert.CommandText = """
                INSERT OR REPLACE INTO documents (sha256, message_key, archive_path, period, source_file_name, size_bytes, archived_at)
                VALUES ($sha256, $key, $path, $period, $source, $size, $now)
                """;
            insert.Parameters.AddWithValue("$sha256", document.Sha256);
            insert.Parameters.AddWithValue("$key", message.Key);
            insert.Parameters.AddWithValue("$path", document.Path);
            insert.Parameters.AddWithValue("$period", document.Period.ToString());
            insert.Parameters.AddWithValue("$source", document.SourceFileName);
            insert.Parameters.AddWithValue("$size", document.SizeBytes);
            insert.Parameters.AddWithValue("$now", now);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RecordIgnoredAsync(IncomingMessage message, string reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await UpsertMessageAsync(connection, null, message.Key, ProcessingStatus.Ignored, message, reason, error: null, Now(), cancellationToken).ConfigureAwait(false);
    }

    public async Task RecordFailureAsync(string key, string errorMessage, CancellationToken cancellationToken)
    {
        var now = Now();
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO messages (key, status, attempts, last_error, created_at, updated_at)
            VALUES ($key, $status, 1, $error, $now, $now)
            ON CONFLICT(key) DO UPDATE SET
                status = excluded.status,
                attempts = messages.attempts + 1,
                last_error = excluded.last_error,
                updated_at = excluded.updated_at
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$status", ProcessingStatus.Failed.ToString());
        command.Parameters.AddWithValue("$error", Truncate(errorMessage, 2000));
        command.Parameters.AddWithValue("$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose() => _schemaGate.Dispose();

    private static async Task UpsertMessageAsync(
        SqliteConnection connection,
        IDbTransaction? transaction,
        string key,
        ProcessingStatus status,
        IncomingMessage message,
        string? note,
        string? error,
        string now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction?)transaction;
        command.CommandText = """
            INSERT INTO messages (key, status, attempts, message_id, sender, subject, received_at, note, last_error, created_at, updated_at)
            VALUES ($key, $status, 0, $messageId, $sender, $subject, $receivedAt, $note, $error, $now, $now)
            ON CONFLICT(key) DO UPDATE SET
                status = excluded.status,
                message_id = excluded.message_id,
                sender = excluded.sender,
                subject = excluded.subject,
                received_at = excluded.received_at,
                note = excluded.note,
                last_error = excluded.last_error,
                updated_at = excluded.updated_at
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$status", status.ToString());
        command.Parameters.AddWithValue("$messageId", (object?)message.MessageId ?? DBNull.Value);
        command.Parameters.AddWithValue("$sender", message.From);
        command.Parameters.AddWithValue("$subject", message.Subject);
        command.Parameters.AddWithValue("$receivedAt", message.ReceivedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$note", (object?)note ?? DBNull.Value);
        command.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value);
        command.Parameters.AddWithValue("$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        if (!_schemaReady)
        {
            await _schemaGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!_schemaReady)
                {
                    await using var command = connection.CreateCommand();
                    command.CommandText = "PRAGMA journal_mode = WAL; PRAGMA foreign_keys = ON;" + Schema;
                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    _schemaReady = true;
                }
            }
            finally
            {
                _schemaGate.Release();
            }
        }

        return connection;
    }

    private string Now() => _clock.GetUtcNow().ToString("O", CultureInfo.InvariantCulture);

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}