using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DriveIndexApp.Services;

namespace DriveIndexApp;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly GoogleDriveService _drive = new();

    private string _driveFolderName = "";
    private string _pageTitle = "";
    private string _accountLabel = "Not connected";
    private string _statusMessage = "";
    private string _pickedLabel = "No images selected";
    private string _indexUrl = "";
    private string _localIndexPath = "";
    private bool _isBusy;
    private bool _hasResult;
    private double _progress;
    private List<FileResult> _pickedFiles = [];

    public string DriveFolderName  { get => _driveFolderName;  set { _driveFolderName = value;  OnPropertyChanged(); OnPropertyChanged(nameof(CanUpload)); } }
    public string PageTitle        { get => _pageTitle;        set { _pageTitle = value;         OnPropertyChanged(); } }
    public string AccountLabel     { get => _accountLabel;     set { _accountLabel = value;      OnPropertyChanged(); } }
    public string StatusMessage    { get => _statusMessage;    set { _statusMessage = value;     OnPropertyChanged(); } }
    public string PickedLabel      { get => _pickedLabel;      set { _pickedLabel = value;       OnPropertyChanged(); } }
    public bool   IsBusy           { get => _isBusy;           set { _isBusy = value;            OnPropertyChanged(); OnPropertyChanged(nameof(CanUpload)); } }
    public bool   HasResult        { get => _hasResult;        set { _hasResult = value;         OnPropertyChanged(); } }
    public double Progress         { get => _progress;         set { _progress = value;          OnPropertyChanged(); } }

    public bool CanUpload => !IsBusy && _drive.IsConnected && _pickedFiles.Count > 0 && !string.IsNullOrWhiteSpace(DriveFolderName);

    public string ConnectButtonText => _drive.IsConnected ? "Disconnect" : "Connect to Google Drive";

    public ICommand ConnectCommand    { get; }
    public ICommand PickImagesCommand { get; }
    public ICommand UploadCommand     { get; }
    public ICommand OpenIndexCommand  { get; }
    public ICommand HelpCommand       { get; }

    public MainViewModel()
    {
        ConnectCommand    = new AsyncCommand(ConnectAsync);
        PickImagesCommand = new AsyncCommand(PickImagesAsync);
        UploadCommand     = new AsyncCommand(UploadAsync, () => CanUpload);
        OpenIndexCommand  = new AsyncCommand(OpenIndexAsync);
        HelpCommand       = new Command(ShowHelp);
    }

    private async Task ConnectAsync()
    {
        if (_drive.IsConnected)
        {
            _drive.Disconnect();
            AccountLabel = "Not connected";
            OnPropertyChanged(nameof(ConnectButtonText));
            OnPropertyChanged(nameof(CanUpload));
            return;
        }

        StatusMessage = "Opening Google sign-in…";
        try
        {
            var email = await _drive.ConnectAsync();
            MainThread.BeginInvokeOnMainThread(() =>
            {
                AccountLabel = email;
                StatusMessage = "";
                OnPropertyChanged(nameof(ConnectButtonText));
                OnPropertyChanged(nameof(CanUpload));
            });
        }
        catch (Exception ex)
        {
            MainThread.BeginInvokeOnMainThread(() =>
                StatusMessage = $"Sign-in failed: {ex.Message}");
        }
    }

    private async Task PickImagesAsync()
    {
        var results = await FilePicker.Default.PickMultipleAsync(new PickOptions
        {
            PickerTitle = "Select images",
            FileTypes = FilePickerFileType.Images
        });

        _pickedFiles = results?.ToList() ?? [];
        PickedLabel = _pickedFiles.Count == 0
            ? "No images selected"
            : $"{_pickedFiles.Count} image{(_pickedFiles.Count == 1 ? "" : "s")} selected";

        OnPropertyChanged(nameof(CanUpload));
        (UploadCommand as AsyncCommand)?.RaiseCanExecuteChanged();
    }

    private async Task UploadAsync()
    {
        IsBusy = true;
        HasResult = false;
        Progress = 0;

        try
        {
            StatusMessage = "Finding/creating Drive folder…";
            var folderId = await _drive.GetOrCreateFolderAsync(DriveFolderName);

            var uploaded = new List<(string name, string webViewLink)>();
            int i = 0;

            foreach (var file in _pickedFiles)
            {
                StatusMessage = $"Uploading {file.FileName} ({i + 1}/{_pickedFiles.Count})…";
                using var stream = await file.OpenReadAsync();
                var link = await _drive.UploadFileAsync(folderId, file.FileName, stream);
                uploaded.Add((file.FileName, link));
                i++;
                Progress = (double)i / (_pickedFiles.Count + 1);
            }

            StatusMessage = "Generating index.html…";
            var title = string.IsNullOrWhiteSpace(PageTitle) ? DriveFolderName : PageTitle;
            var html  = await HtmlIndexGenerator.GenerateAsync(title, uploaded);

            // Save locally so we can open it directly in the browser
            _localIndexPath = Path.Combine(FileSystem.CacheDirectory, "index.html");
            await File.WriteAllTextAsync(_localIndexPath, html);

            // Also upload to Drive as a backup copy
            StatusMessage = "Uploading index.html to Drive…";
            using var htmlStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(html));
            _indexUrl = await _drive.UploadFileAsync(folderId, "index.html", htmlStream, "text/html");

            Progress = 1;
            HasResult = true;

            // Try to open locally in browser immediately
            MainThread.BeginInvokeOnMainThread(() =>
            {
                StatusMessage = $"Done! {uploaded.Count} images uploaded.";
                OpenHtmlFile(_localIndexPath);
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task OpenIndexAsync()
    {
        if (!string.IsNullOrEmpty(_localIndexPath) && File.Exists(_localIndexPath))
            OpenHtmlFile(_localIndexPath);
        else if (!string.IsNullOrEmpty(_indexUrl))
            Launcher.Default.OpenAsync(_indexUrl);
        return Task.CompletedTask;
    }

    private static void OpenHtmlFile(string path)
    {
#if ANDROID
        try
        {
            var javaFile = new Java.IO.File(path);
            var contentUri = AndroidX.Core.Content.FileProvider.GetUriForFile(
                Android.App.Application.Context,
                "com.stevepowell.driveindex.fileprovider",
                javaFile);
            var intent = new Android.Content.Intent(Android.Content.Intent.ActionView);
            intent.SetDataAndType(contentUri, "text/html");
            intent.AddFlags(Android.Content.ActivityFlags.GrantReadUriPermission |
                            Android.Content.ActivityFlags.NewTask);
            var chooser = Android.Content.Intent.CreateChooser(intent, "Open gallery with…");
            chooser!.AddFlags(Android.Content.ActivityFlags.NewTask);
            Android.App.Application.Context.StartActivity(chooser);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OpenHtmlFile failed: {ex.Message}");
        }
#endif
    }

    private void ShowHelp()
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Application.Current!.MainPage!.DisplayAlert(
                "How to Use",
                "1. Tap 'Connect to Google Drive' and sign in.\n\n" +
                "2. Enter a folder name — it will be created in your Drive if it doesn't exist.\n\n" +
                "3. Optionally enter a page title for the gallery.\n\n" +
                "4. Tap 'Pick Images' and select the photos you want.\n\n" +
                "5. Tap 'Upload & Generate Index' — your images are uploaded to Drive and a gallery page is created.\n\n" +
                "6. A share sheet opens so you can open the gallery in your browser. Choose Chrome or any browser to view it.\n\n" +
                "7. The gallery file (index.html) is also saved to your Google Drive folder as a backup — but open it locally for best results, as Drive cannot render HTML pages directly.",
                "Got it");
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null) : ICommand
{
    private bool _running;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? p) => !_running && (canExecute?.Invoke() ?? true);
    public void Execute(object? p) => ExecuteAsync();
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    private async void ExecuteAsync()
    {
        _running = true; RaiseCanExecuteChanged();
        try { await execute(); }
        finally { _running = false; RaiseCanExecuteChanged(); }
    }
}
