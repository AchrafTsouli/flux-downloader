namespace SwiftFetch.Core;

public static class DownloadCategories
{
    public static readonly string[] All = ["Compressed", "Documents", "Music", "Programs", "Video", "Images", "General"];

    public static string ForFile(string file) => Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".bz2" or ".xz" or ".tgz" or ".iso" => "Compressed",
        ".exe" or ".msi" or ".msix" or ".apk" or ".bat" or ".cmd" or ".bin" => "Programs",
        ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".pptx" or ".ppt" or ".txt" or ".csv" or ".rtf" => "Documents",
        ".mp3" or ".m4a" or ".aac" or ".wav" or ".flac" or ".ogg" or ".wma" => "Music",
        ".mp4" or ".mkv" or ".webm" or ".avi" or ".mov" or ".wmv" or ".flv" or ".m4v" or ".ts" => "Video",
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".svg" => "Images",
        _ => "General"
    };

    public static void EnsureAllDirectoriesExist(string rootDownloadDir)
    {
        try
        {
            Directory.CreateDirectory(rootDownloadDir);
            foreach (var cat in All)
            {
                Directory.CreateDirectory(Path.Combine(rootDownloadDir, cat));
            }
        }
        catch { }
    }
}

