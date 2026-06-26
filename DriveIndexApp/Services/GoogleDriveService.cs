using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;

namespace DriveIndexApp.Services;

public class GoogleDriveService
{
    // Replace these with your own from Google Cloud Console
    private const string ClientId     = "YOUR_CLIENT_ID";
    private const string ClientSecret = "YOUR_CLIENT_SECRET";

    private DriveService? _service;
    private UserCredential? _credential;

    public bool IsConnected => _service != null;

    public async Task<string> ConnectAsync()
    {
        _credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            new ClientSecrets { ClientId = ClientId, ClientSecret = ClientSecret },
            [DriveService.Scope.DriveFile],
            "user",
            CancellationToken.None);

        _service = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = _credential,
            ApplicationName = "Drive Index App"
        });

        var about = await _service.About.Get().ExecuteAsync();
        return about.User.EmailAddress ?? "Connected";
    }

    public void Disconnect()
    {
        _credential = null;
        _service?.Dispose();
        _service = null;
    }

    public async Task<string> GetOrCreateFolderAsync(string folderName)
    {
        var svc = _service ?? throw new InvalidOperationException("Not connected");

        // Check if folder exists
        var list = svc.Files.List();
        list.Q = $"name='{folderName}' and mimeType='application/vnd.google-apps.folder' and trashed=false";
        list.Fields = "files(id)";
        var result = await list.ExecuteAsync();

        if (result.Files.Count > 0)
            return result.Files[0].Id;

        // Create it
        var meta = new Google.Apis.Drive.v3.Data.File
        {
            Name     = folderName,
            MimeType = "application/vnd.google-apps.folder"
        };
        var created = await svc.Files.Create(meta).ExecuteAsync();
        return created.Id;
    }

    public async Task<string> UploadFileAsync(string folderId, string fileName, Stream content, string mimeType = "")
    {
        var svc = _service ?? throw new InvalidOperationException("Not connected");

        if (string.IsNullOrEmpty(mimeType))
            mimeType = GetMimeType(fileName);

        // Delete existing file with same name in folder first
        var list = svc.Files.List();
        list.Q = $"name='{fileName}' and '{folderId}' in parents and trashed=false";
        list.Fields = "files(id)";
        var existing = await list.ExecuteAsync();
        foreach (var old in existing.Files)
            await svc.Files.Delete(old.Id).ExecuteAsync();

        var meta = new Google.Apis.Drive.v3.Data.File
        {
            Name    = fileName,
            Parents = [folderId]
        };

        var request = svc.Files.Create(meta, content, mimeType);
        request.Fields = "webViewLink";
        var upload = await request.UploadAsync();

        if (upload.Status != UploadStatus.Completed)
            throw new Exception($"Upload failed: {upload.Exception?.Message}");

        return request.ResponseBody?.WebViewLink ?? "";
    }

    private static string GetMimeType(string fileName) => Path.GetExtension(fileName).ToLower() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png"            => "image/png",
        ".gif"            => "image/gif",
        ".webp"           => "image/webp",
        ".html"           => "text/html",
        _                 => "application/octet-stream"
    };
}
