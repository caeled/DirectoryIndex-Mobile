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

    public MainViewModel()
    {
        ConnectCommand    = new AsyncCommand(ConnectAsync);
        PickImagesCommand = new AsyncCommand(PickImagesAsync);
        UploadCommand     = new AsyncCommand(UploadAsync, () => CanUpload);
        OpenIndexCommand  = new Command(() => Launcher.Default.OpenAsync(_indexUrl));
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
            AccountLabel = email;
            StatusMessage = "";
            OnPropertyChanged(nameof(ConnectButtonText));
            OnPropertyChanged(nameof(CanUpload));
        }
        catch (Exception ex)
        {
            StatusMessage = $"Sign-in failed: {ex.Message}";
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

            StatusMessage = "Uploading index.html…";
            using var htmlStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(html));
            _indexUrl = await _drive.UploadFileAsync(folderId, "index.html", htmlStream, "text/html");

            Progress = 1;
            StatusMessage = $"Done! {uploaded.Count} images uploaded.";
            HasResult = true;
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
