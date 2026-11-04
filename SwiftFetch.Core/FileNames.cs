namespace SwiftFetch.Core;

public static class FileNames
{
    public static bool IsOpaque(string name) => Guid.TryParse(Path.GetFileNameWithoutExtension(name), out _) || string.IsNullOrWhiteSpace(name) || name is "download" or "download.bin";
    public static string Sanitize(string name)
    {
        name = name.Trim('"').Replace('\\', '/').Split('/').Last();
        name = new string(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(name)) name = "download.bin";
        string extension = Path.GetExtension(name);
        if (name.Length > 150) name = Path.GetFileNameWithoutExtension(name)[..Math.Max(1, 150 - Math.Min(extension.Length, 20))] + (extension.Length <= 20 ? extension : "");
        string stem = name.Split('.')[0].TrimEnd().ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] is >= '1' and <= '9')) name = "_" + name;
        return name;
    }
    public static string UniquePath(string directory, string name, Func<string, bool>? reserved = null)
    {
        name = Sanitize(name);
        string path = Path.Combine(directory, name);
        int suffix = 1;
        while (File.Exists(path) || Directory.Exists(path) || reserved?.Invoke(path) == true)
            path = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(name)} ({suffix++}){Path.GetExtension(name)}");
        return path;
    }
}
