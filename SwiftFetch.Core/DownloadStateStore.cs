using System.Text.Json;

namespace SwiftFetch.Core;

public sealed class DownloadSnapshot
{
    public int SchemaVersion { get; set; } = 1;
    public DownloadSettings Settings { get; set; } = new();
    public List<DownloadJob> Downloads { get; set; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    public List<PendingDownload> PendingDownloads { get; set; } = [];
}

public interface IDownloadStateStore
{
    bool RecoveredFromBackup { get; }
    DownloadSnapshot Load();
    void Save(DownloadSnapshot snapshot);
}

/// <summary>Versioned snapshots with a flushed temporary file, atomic replacement, and a last-good backup.</summary>
public sealed class DownloadStateStore(string path) : IDownloadStateStore
{
    private readonly object gate = new();
    public bool RecoveredFromBackup { get; private set; }
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public DownloadSnapshot Load()
    {
        lock (gate)
        {
            RecoveredFromBackup = false;
            if (!File.Exists(path))
            {
                if (!File.Exists(path + ".bak")) return new();
                RecoveredFromBackup = true;
                return Read(path + ".bak");
            }
            try { return Read(path); }
            catch (Exception error) when (error is JsonException or IOException or InvalidDataException or ArgumentException)
            {
                if (!File.Exists(path + ".bak")) throw new InvalidDataException("Download history could not be read. The original file has been preserved.", error);
                var backup = Read(path + ".bak");
                RecoveredFromBackup = true;
                return backup;
            }
        }
    }

    private static DownloadSnapshot Read(string file)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(file));
        DownloadSnapshot snapshot;
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            snapshot = new() { Downloads = document.RootElement.Deserialize<List<DownloadJob>>() ?? [] };
            // Legacy history has no creation timestamps; do not invent historical dates.
            int index = 0;
            foreach (var item in document.RootElement.EnumerateArray())
                if (!item.TryGetProperty("CreatedAt", out _)) snapshot.Downloads[index++].CreatedAt = null; else index++;
        }
        else snapshot = document.RootElement.Deserialize<DownloadSnapshot>() ?? throw new InvalidDataException("Empty download history.");
        if (snapshot.SchemaVersion != 1) throw new NotSupportedException("This history version requires a newer SwiftFetch.");
        if (snapshot.Settings == null || snapshot.Downloads == null) throw new InvalidDataException("History settings or downloads are missing.");
        snapshot.Settings.Validate();
        foreach (var job in snapshot.Downloads)
        {
            if (!Guid.TryParseExact(job.Id, "N", out _) || !Path.IsPathFullyQualified(job.Destination) || !Uri.TryCreate(job.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                throw new InvalidDataException("Download history contains an invalid job.");
            if (job.Category == "Other") job.Category = DownloadCategories.ForFile(job.FileName);
        }
        return snapshot;
    }

    public void Save(DownloadSnapshot snapshot)
    {
        lock (gate)
        {
            snapshot.Settings.Validate();
            if (snapshot.SchemaVersion != 1) throw new NotSupportedException("Unsupported history version.");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(output, snapshot, Json);
                    output.Flush(true);
                }
                if (File.Exists(path))
                {
                    // Do not replace a good backup with a corrupted primary snapshot.
                    bool valid = true;
                    try { Read(path); }
                    catch (Exception error) when (error is JsonException or IOException or InvalidDataException or ArgumentException) { valid = false; }
                    if (valid) File.Replace(temporary, path, path + ".bak");
                    else
                    {
                        File.Copy(path, path + ".corrupt-" + Guid.NewGuid().ToString("N"));
                        File.Move(temporary, path, true);
                    }
                }
                else File.Move(temporary, path);
                RecoveredFromBackup = false;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
