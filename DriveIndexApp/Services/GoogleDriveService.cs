using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Auth.OAuth2;

namespace DriveIndexApp.Services;

public class GoogleDriveService
{
    private const string ClientId     = Secrets.GoogleClientId;
    private const string ClientSecret = "";
    // Google Android OAuth: redirect URI must be reversed client ID scheme
    private static readonly string RedirectUri =
        $"com.googleusercontent.apps.{ClientId.Replace(".apps.googleusercontent.com", "")}:/oauth2redirect";
    private const string Scope        = "https://www.googleapis.com/auth/drive.file openid email";

    private DriveService? _service;
    private string?       _accessToken;
    private string?       _refreshToken;

    public bool IsConnected => _service != null;

    public async Task<string> ConnectAsync()
    {
        // Generate PKCE values
        var codeVerifier  = GenerateCodeVerifier();
        var codeChallenge = GenerateCodeChallenge(codeVerifier);

        var authUrl = new Uri(
            "https://accounts.google.com/o/oauth2/v2/auth" +
            $"?client_id={Uri.EscapeDataString(ClientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
            "&response_type=code" +
            $"&scope={Uri.EscapeDataString(Scope)}" +
            "&access_type=offline" +
            "&prompt=consent" +
            "&code_challenge_method=S256" +
            $"&code_challenge={codeChallenge}");

        var callbackUri = new Uri(RedirectUri);
        var result = await WebAuthenticator.Default.AuthenticateAsync(authUrl, callbackUri);

        var code = result.Properties["code"];

        // Exchange code for tokens
        using var http = new HttpClient();
        var tokenParams = new Dictionary<string, string>
        {
            ["code"]          = code,
            ["client_id"]     = ClientId,
            ["redirect_uri"]  = RedirectUri,
            ["grant_type"]    = "authorization_code",
            ["code_verifier"] = codeVerifier,
        };

        // Debug: log what we're sending
        System.Diagnostics.Debug.WriteLine($"Token exchange - code: {code?.Substring(0, 10)}...");
        System.Diagnostics.Debug.WriteLine($"Token exchange - client_id: {ClientId}");
        System.Diagnostics.Debug.WriteLine($"Token exchange - redirect_uri: {RedirectUri}");
        var tokenResponse = await http.PostAsync("https://oauth2.googleapis.com/token",
            new FormUrlEncodedContent(tokenParams));

        var json = await tokenResponse.Content.ReadAsStringAsync();
        System.Diagnostics.Debug.WriteLine($"Token response ({tokenResponse.StatusCode}): {json}");
        if (!tokenResponse.IsSuccessStatusCode)
            throw new Exception($"Token exchange failed {tokenResponse.StatusCode}: {json}");
        var tokens = JsonDocument.Parse(json).RootElement;

        _accessToken  = tokens.GetProperty("access_token").GetString();
        _refreshToken = tokens.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
        System.Diagnostics.Debug.WriteLine($"Tokens parsed OK. Building service...");

        BuildService();
        System.Diagnostics.Debug.WriteLine($"Service built OK. Fetching userinfo...");

        // Get user email
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        var userJson = await client.GetStringAsync("https://www.googleapis.com/oauth2/v3/userinfo");
        System.Diagnostics.Debug.WriteLine($"Userinfo response: {userJson}");
        var user = JsonDocument.Parse(userJson).RootElement;
        return user.TryGetProperty("email", out var email) ? email.GetString() ?? "Connected" : "Connected";
    }

    public void Disconnect()
    {
        _accessToken  = null;
        _refreshToken = null;
        _service?.Dispose();
        _service = null;
    }

    public async Task<string> GetOrCreateFolderAsync(string folderName)
    {
        var svc = _service ?? throw new InvalidOperationException("Not connected");
        var list = svc.Files.List();
        list.Q      = $"name='{EscapeQ(folderName)}' and mimeType='application/vnd.google-apps.folder' and trashed=false";
        list.Fields = "files(id)";
        var result  = await list.ExecuteAsync();
        if (result.Files.Count > 0) return result.Files[0].Id;

        var meta    = new Google.Apis.Drive.v3.Data.File { Name = folderName, MimeType = "application/vnd.google-apps.folder" };
        var created = await svc.Files.Create(meta).ExecuteAsync();
        return created.Id;
    }

    public async Task<string> UploadFileAsync(string folderId, string fileName, Stream content, string mimeType = "")
    {
        var svc = _service ?? throw new InvalidOperationException("Not connected");
        if (string.IsNullOrEmpty(mimeType)) mimeType = GetMimeType(fileName);

        // Remove existing file with same name
        var list = svc.Files.List();
        list.Q      = $"name='{EscapeQ(fileName)}' and '{folderId}' in parents and trashed=false";
        list.Fields = "files(id)";
        var existing = await list.ExecuteAsync();
        foreach (var old in existing.Files) await svc.Files.Delete(old.Id).ExecuteAsync();

        var meta    = new Google.Apis.Drive.v3.Data.File { Name = fileName, Parents = [folderId] };
        var request = svc.Files.Create(meta, content, mimeType);
        request.Fields = "webViewLink,id,mimeType";
        var upload  = await request.UploadAsync();

        if (upload.Status != UploadStatus.Completed)
            throw new Exception($"Upload failed: {upload.Exception?.Message}");

        var fileId = request.ResponseBody!.Id;

        // Make file publicly readable so the index can link to it
        await svc.Permissions.Create(
            new Google.Apis.Drive.v3.Data.Permission { Role = "reader", Type = "anyone" },
            fileId).ExecuteAsync();

        // Return direct-display URL for images, viewer link for everything else
        var mime = request.ResponseBody?.MimeType ?? "";
        if (mime.StartsWith("image/"))
            return $"https://lh3.googleusercontent.com/d/{fileId}";

        return request.ResponseBody?.WebViewLink ?? "";
    }

    private void BuildService()
    {
        var credential = GoogleCredential.FromAccessToken(_accessToken);
        _service = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName       = "Drive Index App"
        });
    }

    private static string GenerateCodeVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string GenerateCodeChallenge(string verifier)
    {
        var bytes = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string EscapeQ(string s) => s.Replace("'", "\\'");

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
