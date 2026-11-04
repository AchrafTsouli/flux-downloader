using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace SwiftFetch.Core;

public sealed class SqliteDownloadStateStore(string databasePath, string? legacyPath = null) : IDownloadStateStore
{
    private readonly object gate = new();
    public bool RecoveredFromBackup { get; private set; }
    private DateTimeOffset lastBackup;
    public string? BackupError { get; private set; }
    private static readonly JsonSerializerOptions Json = new();
    public static DownloadSettings ReadPreferences(string path)
    {
        if (!File.Exists(path)) return new();
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()); connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = "SELECT value FROM settings WHERE key='preferences'";
        if (command.ExecuteScalar() is not string value) return new();
        var settings = JsonSerializer.Deserialize<DownloadSettings>(value.StartsWith("dpapi:") ? Encoding.UTF8.GetString(WindowsSecrets.Unprotect(Convert.FromBase64String(value[6..]))) : value, Json) ?? throw new InvalidDataException("Invalid saved preferences.");
        settings.Validate(); return settings;
    }

    private SqliteConnection Open()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString());
        try
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version";
            long version = (long)command.ExecuteScalar()!;
            if (version is not (0 or 1)) throw new NotSupportedException("This database requires a newer SwiftFetch.");
            command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
            command.ExecuteNonQuery();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS downloads (
                  id TEXT PRIMARY KEY, filename TEXT NOT NULL, state TEXT NOT NULL, category TEXT NOT NULL,
                  total_bytes INTEGER, payload BLOB NOT NULL, session BLOB, fingerprint TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS segments (
                  download_id TEXT NOT NULL REFERENCES downloads(id) ON DELETE CASCADE,
                  part_index INTEGER NOT NULL, start_byte INTEGER NOT NULL, end_byte INTEGER, committed_bytes INTEGER,
                  PRIMARY KEY(download_id, part_index));
                CREATE INDEX IF NOT EXISTS downloads_state ON downloads(state);
                PRAGMA user_version=1;
                """;
            command.ExecuteNonQuery();
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }

    public DownloadSnapshot Load()
    {
        lock (gate)
        {
            using var connection = Open();
            using var marker = connection.CreateCommand();
            marker.CommandText = "SELECT value FROM settings WHERE key='initialized'";
            if (marker.ExecuteScalar() == null)
            {
                var legacy = legacyPath != null ? new DownloadStateStore(legacyPath) : null;
                var imported = legacy?.Load() ?? new();
                RecoveredFromBackup = legacy?.RecoveredFromBackup == true;
                SaveCore(connection, imported);
                if (legacyPath != null && File.Exists(legacyPath))
                {
                    string copy = legacyPath + ".pre-sqlite";
                    if (!File.Exists(copy)) File.Copy(legacyPath, copy, false);
                }
                Backup(connection);
                return imported;
            }
            var snapshot = new DownloadSnapshot();
            using (var settings = connection.CreateCommand())
            {
                settings.CommandText = "SELECT value FROM settings WHERE key='preferences'";
                string preferences = (string)settings.ExecuteScalar()!;
                snapshot.Settings = JsonSerializer.Deserialize<DownloadSettings>(preferences.StartsWith("dpapi:") ? Encoding.UTF8.GetString(WindowsSecrets.Unprotect(Convert.FromBase64String(preferences[6..]))) : preferences, Json)!;
                snapshot.Settings.Validate();
            }
            using (var pending = connection.CreateCommand())
            {
                pending.CommandText = "SELECT value FROM settings WHERE key='pending_downloads'";
                if (pending.ExecuteScalar() is string encoded)
                    snapshot.PendingDownloads = JsonSerializer.Deserialize<List<PendingDownload>>(WindowsSecrets.Unprotect(Convert.FromBase64String(encoded)), Json) ?? throw new InvalidDataException("Invalid pending downloads.");
                ValidatePending(snapshot.PendingDownloads);
            }
            using (var tasks = connection.CreateCommand())
            {
                tasks.CommandText = "SELECT id, payload, session FROM downloads ORDER BY rowid";
                using var reader = tasks.ExecuteReader();
                while (reader.Read())
                {
                    byte[] raw = WindowsSecrets.Unprotect((byte[])reader[1]);
                    var job = JsonSerializer.Deserialize<DownloadJob>(raw, Json) ?? throw new InvalidDataException("Invalid task payload.");
                    if (job.Id != reader.GetString(0)) throw new InvalidDataException("Stored task identifiers do not match.");
                    if (!reader.IsDBNull(2)) job.Session = JsonSerializer.Deserialize<DownloadSession>(WindowsSecrets.Unprotect((byte[])reader[2]), Json);
                    ValidateJob(job);
                    snapshot.Downloads.Add(job);
                }
            }
            foreach (var job in snapshot.Downloads)
            {
                using var parts = connection.CreateCommand();
                parts.CommandText = "SELECT part_index,start_byte,end_byte,committed_bytes FROM segments WHERE download_id=$id ORDER BY part_index";
                parts.Parameters.AddWithValue("$id", job.Id);
                using var reader = parts.ExecuteReader();
                job.Segments.Clear();
                while (reader.Read())
                {
                    if (reader.GetInt32(0) != job.Segments.Count) throw new InvalidDataException("Segment identifiers are incomplete.");
                    var part = new DownloadSegment { Start = reader.GetInt64(1), End = reader.IsDBNull(2) ? null : reader.GetInt64(2), CommittedBytes = reader.IsDBNull(3) ? null : reader.GetInt64(3) };
                    if (part.Start < 0 || part.End < part.Start || part.CommittedBytes < 0 || part.End is long end && part.CommittedBytes > end - part.Start + 1) throw new InvalidDataException("Invalid stored segment.");
                    job.Segments.Add(part);
                }
            }
            return snapshot;
        }
    }

    public void Save(DownloadSnapshot snapshot)
    {
        lock (gate)
        {
            using var connection = Open(); SaveCore(connection, snapshot);
            if (DateTimeOffset.UtcNow - lastBackup > TimeSpan.FromMinutes(1)) Backup(connection);
        }
    }
    public void CreateBackup() { lock (gate) { using var connection = Open(); Backup(connection); } }
    public void SaveCheckpoints(IEnumerable<DownloadJob> jobs)
    {
        var changed = jobs.ToList();
        if (changed.Count == 0) return;
        lock (gate)
        {
            using var connection = Open();
            SaveCore(connection,new DownloadSnapshot { Downloads=changed },progressOnly:true);
            if (DateTimeOffset.UtcNow-lastBackup > TimeSpan.FromMinutes(1)) Backup(connection);
        }
    }
    private void Backup(SqliteConnection connection)
    {
        string target = Path.ChangeExtension(databasePath, ".backup.db");
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temporary, Pooling = false }.ToString()))
            {
                destination.Open(); connection.BackupDatabase(destination);
            }
            File.Move(temporary, target, true);
            lastBackup = DateTimeOffset.UtcNow; BackupError = null;
        }
        catch (Exception error) { BackupError = error.Message; }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static void SaveCore(SqliteConnection connection, DownloadSnapshot snapshot, bool progressOnly = false)
    {
        if (snapshot.SchemaVersion != 1) throw new NotSupportedException("Unsupported snapshot version.");
        if (!progressOnly) { snapshot.Settings.Validate(); ValidatePending(snapshot.PendingDownloads); }
        using var transaction = connection.BeginTransaction();
        void Sql(string text, params (string Name, object? Value)[] parameters)
        {
            using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = text;
            foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
            command.ExecuteNonQuery();
        }
        if (!progressOnly) Sql("CREATE TEMP TABLE retained (id TEXT PRIMARY KEY)");
        foreach (var job in snapshot.Downloads)
        {
            lock (job.SegmentGate)
            {
                ValidateJob(job);
                byte[] payload = JsonSerializer.SerializeToUtf8Bytes(job, Json);
                byte[]? session = job.Session == null ? null : JsonSerializer.SerializeToUtf8Bytes(job.Session, Json);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                hash.AppendData(payload); if (session != null) hash.AppendData(session);
                string fingerprint = Convert.ToHexString(hash.GetHashAndReset());
                if (!progressOnly) Sql("INSERT INTO retained VALUES($id)", ("$id", job.Id));
                using var lookup = connection.CreateCommand(); lookup.Transaction = transaction;
                lookup.CommandText = "SELECT fingerprint FROM downloads WHERE id=$id"; lookup.Parameters.AddWithValue("$id", job.Id);
                string? previousFingerprint = lookup.ExecuteScalar() as string;
                if (previousFingerprint == fingerprint) continue;
                if (progressOnly && previousFingerprint == null) throw new InvalidDataException("A checkpoint cannot create a new download.");
                Sql("""
                    INSERT INTO downloads(id,filename,state,category,total_bytes,payload,session,fingerprint)
                    VALUES($id,$filename,$state,$category,$total,$payload,$session,$fingerprint)
                    ON CONFLICT(id) DO UPDATE SET filename=excluded.filename,state=excluded.state,category=excluded.category,
                    total_bytes=excluded.total_bytes,payload=excluded.payload,session=excluded.session,fingerprint=excluded.fingerprint
                    """, ("$id", job.Id), ("$filename", job.FileName), ("$state", job.Status), ("$category", job.Category), ("$total", job.TotalBytes),
                    ("$payload", WindowsSecrets.Protect(payload)), ("$session", session == null ? null : WindowsSecrets.Protect(session)), ("$fingerprint", fingerprint));
                Sql("DELETE FROM segments WHERE download_id=$id", ("$id", job.Id));
                for (int i = 0; i < job.Segments.Count; i++)
                    Sql("INSERT INTO segments VALUES($id,$index,$start,$end,$committed)", ("$id", job.Id), ("$index", i), ("$start", job.Segments[i].Start), ("$end", job.Segments[i].End), ("$committed", job.Segments[i].CommittedBytes));
            }
        }
        if (progressOnly) { transaction.Commit(); return; }
        Sql("DELETE FROM downloads WHERE id NOT IN (SELECT id FROM retained)");
        using (var previous = connection.CreateCommand())
        {
            previous.Transaction = transaction;
            previous.CommandText = "SELECT value FROM settings WHERE key='preferences'";
            string serialized = JsonSerializer.Serialize(snapshot.Settings, Json);
            string? saved = previous.ExecuteScalar() as string;
            string? plain = saved?.StartsWith("dpapi:") == true ? Encoding.UTF8.GetString(WindowsSecrets.Unprotect(Convert.FromBase64String(saved[6..]))) : saved;
            if (plain != serialized || saved?.StartsWith("dpapi:") != true)
                Sql("INSERT INTO settings VALUES('preferences',$value) ON CONFLICT(key) DO UPDATE SET value=excluded.value", ("$value", "dpapi:" + Convert.ToBase64String(WindowsSecrets.Protect(Encoding.UTF8.GetBytes(serialized)))));
        }
        if (snapshot.PendingDownloads.Count == 0) Sql("DELETE FROM settings WHERE key='pending_downloads'");
        else Sql("INSERT INTO settings VALUES('pending_downloads',$value) ON CONFLICT(key) DO UPDATE SET value=excluded.value", ("$value", Convert.ToBase64String(WindowsSecrets.Protect(JsonSerializer.SerializeToUtf8Bytes(snapshot.PendingDownloads, Json)))));
        Sql("INSERT INTO settings VALUES('initialized','1') ON CONFLICT(key) DO NOTHING");
        Sql("DROP TABLE retained");
        transaction.Commit();
    }
    private static void ValidateJob(DownloadJob job)
    {
        if (!Guid.TryParseExact(job.Id, "N", out _) || !Path.IsPathFullyQualified(job.Destination) || !Uri.TryCreate(job.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") && !(uri.Scheme == "magnet" && job.Torrent != null)) throw new InvalidDataException("Invalid stored download.");
        if (job.TemporaryDirectory is { Length: > 0 } && !Path.IsPathFullyQualified(job.TemporaryDirectory)) throw new InvalidDataException("Invalid stored parts folder.");
        job.Session?.Validate(job.Url);
        job.Video?.Validate(job.Url);
        job.Torrent?.Validate(job.Url);
        if (job.Video != null && job.MediaWorkDirectory != null) VideoTools.WorkDirectory(job);
    }
    private static void ValidatePending(List<PendingDownload> pending)
    {
        if (pending == null || pending.Count > 1000 || pending.Any(value => value == null) || pending.Select(value => value.RequestId).Distinct().Count() != pending.Count) throw new InvalidDataException("Invalid pending handoff list.");
        foreach (var item in pending) item.Validate();
    }
}
