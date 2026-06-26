namespace DriveIndexApp.Services;

public static class HtmlIndexGenerator
{
    public static async Task<string> GenerateAsync(string title, List<(string name, string url)> files)
    {
        using var stream = await FileSystem.OpenAppPackageFileAsync("HtmlTemplate.html");
        using var reader = new StreamReader(stream);
        var template = await reader.ReadToEndAsync();

        var fileEntries = files.Select(f => new
        {
            name      = f.name,
            path      = f.url,
            fullPath  = f.url,
            size      = 0,
            type      = GetFileType(f.name),
            extension = Path.GetExtension(f.name).TrimStart('.').ToLower(),
            title     = (string?)null,
            artist    = (string?)null,
            album     = (string?)null,
            cover     = (string?)null
        }).ToList();

        var folderData = new
        {
            name       = title,
            path       = "",
            cover      = (string?)null,
            files      = fileEntries,
            subfolders = Array.Empty<string>()
        };

        var json = System.Text.Json.JsonSerializer.Serialize(folderData);

        return template
            .Replace("const embeddedFolderData = null;", $"const embeddedFolderData = {json};")
            .Replace("__PAGE_TITLE__", System.Web.HttpUtility.HtmlEncode(title))
            .Replace("__AUTHOR_NAME__", "")
            .Replace("__HEADER_IMAGE__", "")
            .Replace("__HEADER_TITLE_CLASS__", "");
    }

    private static string GetFileType(string fileName) =>
        Path.GetExtension(fileName).ToLower() switch
        {
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".bmp" => "image",
            ".mp3" or ".flac" or ".wav" or ".aac" or ".ogg"            => "audio",
            ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm"            => "video",
            ".pdf"                                                       => "pdf",
            ".epub"                                                      => "epub",
            _                                                            => "other"
        };
}
