using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using CamFlux.Core;
using CamFlux.Infrastructure;
using Microsoft.Win32;
using CaptureMode = CamFlux.Core.CaptureMode;

namespace CamFlux.App;

internal abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Changed(name);
    }
}

internal sealed class ActionCommand(Func<object?, Task> execute, Predicate<object?>? available = null) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
    public bool CanExecute(object? parameter) => available?.Invoke(parameter) ?? true;
    public async void Execute(object? parameter) => await execute(parameter);
}

internal sealed record ProfileChoice(Guid Id, string Name)
{
    public override string ToString() => Name;
}

internal sealed record AudioChoice(string? Id, string Name)
{
    public override string ToString() => Name;
}

internal sealed class CameraRow : Observable
{
    private bool enabled;
    private string path;
    private string bitrate;
    private CaptureMode? mode;
    private AudioChoice? audio;
    private StreamState state;
    private string metrics = "";
    private IReadOnlyList<CaptureMode> modes;
    private string status = "";
    private string failureDetail = "";

    public CameraRow(Camera camera)
    {
        DeviceId = camera.DeviceId;
        Name = camera.Name;
        enabled = camera.Enabled;
        path = camera.Path;
        bitrate = camera.Bitrate.ToString(CultureInfo.InvariantCulture);
        mode = camera.Mode;
        modes = mode is null ? [] : [mode];
        if (camera.AudioId is not null)
            audio = new AudioChoice(camera.AudioId, camera.AudioId);
    }

    public string DeviceId { get; }
    public string Name { get; }
    public bool Enabled { get => enabled; set => Set(ref enabled, value); }
    public string Path { get => path; set => Set(ref path, value); }
    public string Bitrate { get => bitrate; set => Set(ref bitrate, value); }
    public CaptureMode? Mode { get => mode; set => Set(ref mode, value); }
    public AudioChoice? Audio { get => audio; set => Set(ref audio, value); }
    public IReadOnlyList<CaptureMode> Modes { get => modes; set => Set(ref modes, value); }
    public StreamState State { get => state; set => Set(ref state, value); }
    public string Status { get => status; set => Set(ref status, value); }
    public string FailureDetail { get => failureDetail; set => Set(ref failureDetail, value); }
    public string? LastError { get; set; }
    public string Metrics { get => metrics; set => Set(ref metrics, value); }
    public StreamProgress? Progress { get; set; }
    public string? ValidationError { get; set; }
    public bool Editable => Operation is null;
    public CancellationTokenSource? Operation { get; private set; }
    public Task? Completion { get; set; }

    public void Own(CancellationTokenSource? operation)
    {
        Operation = operation;
        Changed(nameof(Editable));
    }

    public Camera Snapshot() => new(DeviceId, Name, Enabled, Path.Trim(), Mode,
        int.TryParse(Bitrate, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0, Audio?.Id);
}

internal sealed class MainViewModel : Observable
{
    private readonly string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CamFlux");
    private readonly CancellationTokenSource shutdown = new();
    private readonly ProfileStore store;
    private readonly Queue<(DateTime Time, string? Path, string Code)> log = new();
    private readonly HashSet<string> attached = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> attachedAudio = new(StringComparer.OrdinalIgnoreCase);
    private Ffmpeg? ffmpeg;
    private string? ffmpegPath;
    private Task? work;
    private bool busy;
    private string language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? "ru" : "en";
    private string diagnostics = "";
    private string notice = "";
    private string noticeCode = "Ready";
    private string host = "";
    private string port = "8890";
    private string latency = "500";
    private string publishUser = "";
    private string profileName = "Default";
    private ProfileChoice? selectedProfile;
    private IReadOnlyList<AudioChoice> audioDevices = [];

    public MainViewModel()
    {
        store = new ProfileStore(Path.Combine(root, "profiles"));
        RefreshProfiles();
        ReloadText();
        Discover = Command(_ => ffmpeg is null ? InitializeAsync() : WorkAsync(RefreshDevicesAsync),
            _ => !busy && !shutdown.IsCancellationRequested && (ffmpeg is not null || CanDiscover));
        Browse = Command(_ => BrowseAsync(), _ => CanDiscover);
        Probe = Command(p => ProbeAsync((CameraRow)p!), p => !busy && ffmpeg is not null &&
            p is CameraRow { Editable: true } row && attached.Contains(row.DeviceId));
        Start = Command(p => StartRows(p is CameraRow row ? [row] : Cameras.Where(c => c.Enabled).ToArray()),
            p => !busy && ffmpeg is not null && (p is CameraRow row ? row.Enabled && row.Editable && attached.Contains(row.DeviceId) :
                Cameras.Any(c => c.Enabled && c.Editable)));
        Stop = Command(p => StopRows(p is CameraRow row ? [row] : Cameras),
            p => p is CameraRow row ? row.Operation is { IsCancellationRequested: false } :
                Cameras.Any(c => c.Operation is { IsCancellationRequested: false }));
        Save = Command(_ => SaveProfile(), _ => !busy);
        Load = Command(_ => LoadProfileAsync(), _ => CanDiscover && SelectedProfile is not null);
        Copy = Command(p => CopyUrl((CameraRow)p!), p => p is CameraRow);
    }

    public ObservableCollection<CameraRow> Cameras { get; } = [];
    public ObservableCollection<ProfileChoice> Profiles { get; } = [];
    public IReadOnlyList<AudioChoice> AudioDevices { get => audioDevices; private set => Set(ref audioDevices, value); }
    public ProfileChoice? SelectedProfile { get => selectedProfile; set => Set(ref selectedProfile, value); }
    public string ProfileName { get => profileName; set => Set(ref profileName, value); }
    public string Host { get => host; set => Set(ref host, value); }
    public string Port { get => port; set => Set(ref port, value); }
    public string Latency { get => latency; set => Set(ref latency, value); }
    public string PublishUser { get => publishUser; set => Set(ref publishUser, value); }
    public string PublishPassword { private get; set; } = "";
    public string ReaderUser { private get; set; } = "";
    public string ReaderPassword { private get; set; } = "";
    public event Action<string>? PublishingPasswordLoaded;
    public string[] Languages { get; } = ["en", "ru"];
    public string Language
    {
        get => language;
        set
        {
            if (value is not ("en" or "ru") || value == language) return;
            Set(ref language, value);
            ReloadText();
        }
    }

    public string Bandwidth => string.Format(CultureInfo.GetCultureInfo(Language),
        Texts.Get("Bandwidth", Language), Streaming.EstimatedMbps(Cameras.Select(c => c.Snapshot())));
    public string Diagnostics { get => diagnostics; private set => Set(ref diagnostics, value); }
    public bool CanEditEndpoint => Cameras.All(c => c.Editable);
    public string Notice { get => notice; private set => Set(ref notice, value); }
    public ICommand Discover { get; }
    public ICommand Browse { get; }
    public ICommand Probe { get; }
    public ICommand Start { get; }
    public ICommand Stop { get; }
    public ICommand Save { get; }
    public ICommand Load { get; }
    public ICommand Copy { get; }
    private bool CanDiscover => !busy && Cameras.All(c => c.Editable) && !shutdown.IsCancellationRequested;

    public Task InitializeAsync()
    {
        if (!CanDiscover) return Task.CompletedTask;
        return WorkAsync(async () =>
        {
            ffmpeg = null;
            Notify("Checking");
            var path = await Ffmpeg.ResolveAsync(root, ffmpegPath, () => OnUi(() => Notify("Downloading")), shutdown.Token);
            ffmpegPath = path;
            ffmpeg = new Ffmpeg(path);
            await RefreshDevicesAsync();
        });
    }

    private async Task BrowseAsync()
    {
        var dialog = new OpenFileDialog { Filter = "ffmpeg.exe|ffmpeg.exe|*.exe|*.exe", CheckFileExists = true };
        if (dialog.ShowDialog() != true) return;
        await WorkAsync(async () =>
        {
            Notify("Checking");
            var candidate = new Ffmpeg(dialog.FileName);
            if (!await candidate.SupportsMvpAsync(shutdown.Token))
            {
                Notify("InvalidFfmpeg");
                return;
            }
            ffmpegPath = dialog.FileName;
            ffmpeg = candidate;
            await RefreshDevicesAsync();
        });
    }

    private async Task RefreshDevicesAsync()
    {
        var devices = await ffmpeg!.DevicesAsync(shutdown.Token);
        var savedAudio = Cameras.ToDictionary(c => c.DeviceId, c => c.Audio, StringComparer.OrdinalIgnoreCase);
        attachedAudio.Clear();
        attachedAudio.UnionWith(devices.Where(d => d.IsAudio).Select(d => d.Id));
        AudioDevices = new[] { new AudioChoice(null, Text("NoAudio")) }.Concat(
            devices.Where(d => d.IsAudio).Select(d => new AudioChoice(d.Id, d.Name)))
            .Concat(savedAudio.Values.OfType<AudioChoice>().Where(a => a.Id is not null && !attachedAudio.Contains(a.Id))
                .Select(a => new AudioChoice(a.Id, a.Id!)))
            .DistinctBy(a => a.Id, StringComparer.OrdinalIgnoreCase).ToArray();

        attached.Clear();
        foreach (var device in devices.Where(d => !d.IsAudio))
        {
            attached.Add(device.Id);
            if (Cameras.Any(c => StringComparer.OrdinalIgnoreCase.Equals(c.DeviceId, device.Id)))
                continue;
            var number = Cameras.Count + 1;
            while (Cameras.Any(c => c.Path == $"camera-{number}")) number++;
            AddRow(new Camera(device.Id, device.Name, true, $"camera-{number}", null, 2500, null));
        }
        foreach (var row in Cameras)
        {
            row.Audio = AudioDevices.FirstOrDefault(a => StringComparer.OrdinalIgnoreCase.Equals(a.Id, savedAudio.GetValueOrDefault(row.DeviceId)?.Id));
            row.ValidationError = null;
            UpdateStatus(row);
        }
        Notify(attached.Count == 0 ? "NoCameras" : "Ready");
    }

    private Task ProbeAsync(CameraRow row) => WorkAsync(async () =>
    {
        Notify("Probing");
        var previous = row.Mode;
        var modes = await ffmpeg!.ModesAsync(row.DeviceId, shutdown.Token);
        row.Modes = modes;
        row.Mode = previous is null ? modes.FirstOrDefault(m => !m.IsInferred) :
            modes.FirstOrDefault(m => m.Width == previous.Width && m.Height == previous.Height &&
                m.Fps == previous.Fps && m.Format == previous.Format && m.IsCodec == previous.IsCodec);
        Notify(modes.Count == 0 ? "NoModes" : "Ready");
    });

    private void AddRow(Camera camera)
    {
        var row = new CameraRow(camera);
        row.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(CameraRow.Path) or nameof(CameraRow.Mode) or nameof(CameraRow.Bitrate) or nameof(CameraRow.Audio))
            {
                row.ValidationError = null;
                UpdateStatus(row);
            }
            if (e.PropertyName is nameof(CameraRow.Enabled) or nameof(CameraRow.Bitrate) or nameof(CameraRow.Audio))
            {
                Changed(nameof(Bandwidth));
                CommandManager.InvalidateRequerySuggested();
            }
        };
        Cameras.Add(row);
        UpdateStatus(row);
        Changed(nameof(Bandwidth));
    }

    private void StartRows(CameraRow[] rows)
    {
        var candidates = rows.Where(row => row.Editable).ToArray();
        var failed = false;
        Endpoint endpoint;
        try { endpoint = CurrentEndpoint(); }
        catch (ArgumentException)
        {
            // Let the planner report the common endpoint error on each otherwise valid row.
            endpoint = new Endpoint(Host, Port: 0);
        }
        var active = Cameras.Where(row => !row.Editable).Select(row => row.Snapshot()).ToArray();
        var decisions = StartPlanning.Plan(candidates.Select(row => row.Snapshot()), active,
            attached, attachedAudio, endpoint, new Credentials(PublishUser, PublishPassword));
        foreach (var (row, decision) in candidates.Zip(decisions))
        {
            row.ValidationError = decision.Error;
            if (decision.Arguments is { } arguments)
                StartRow(row, arguments);
            else
            {
                failed = true;
                UpdateStatus(row);
                AddLog((Cameras.IndexOf(row) + 1).ToString(CultureInfo.InvariantCulture), decision.Error!);
            }
        }
        Notify(failed ? "ReviewRows" : "Ready");
        CommandManager.InvalidateRequerySuggested();
    }

    private void StartRow(CameraRow row, string[] arguments)
    {
        var operation = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
        row.Own(operation);
        Changed(nameof(CanEditEndpoint));
        row.Progress = null;
        row.LastError = null;
        row.State = StreamState.Starting;
        UpdateStatus(row);
        UpdateMetrics(row);
        var supervisor = new Supervisor(ffmpeg!);
        var rowNumber = (Cameras.IndexOf(row) + 1).ToString(CultureInfo.InvariantCulture);
        row.Completion = RunRowAsync();

        async Task RunRowAsync()
        {
            try
            {
                await Task.Run(() => supervisor.RunAsync(arguments,
                    state => OnUi(() =>
                    {
                        if (!operation.IsCancellationRequested || state == StreamState.Idle)
                        {
                            row.State = state;
                            if (state == StreamState.Starting)
                                row.LastError = null;
                            if (state != StreamState.Running)
                            {
                                row.Progress = null;
                                UpdateMetrics(row);
                            }
                            UpdateStatus(row);
                            AddLog(rowNumber, state.ToString());
                        }
                    }),
                    progress => OnUi(() =>
                    {
                        if (operation.IsCancellationRequested) return;
                        row.Progress = progress;
                        UpdateMetrics(row);
                    }),
                    code => OnUi(() =>
                    {
                        if (code is not "FfmpegWarning" && !code.StartsWith("FFmpeg exit: ", StringComparison.Ordinal))
                            row.LastError = code;
                        AddLog(rowNumber, code);
                    }), operation.Token));
            }
            catch (Exception)
            {
                row.LastError = "ProcessError";
                AddLog(rowNumber, "ProcessError");
                row.State = StreamState.Failed;
            }
            finally
            {
                if (operation.IsCancellationRequested) row.State = StreamState.Idle;
                row.Own(null);
                Changed(nameof(CanEditEndpoint));
                operation.Dispose();
                row.Progress = null;
                UpdateMetrics(row);
                UpdateStatus(row);
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    private void StopRows(IEnumerable<CameraRow> rows)
    {
        foreach (var row in rows)
        {
            if (row.Operation is not { IsCancellationRequested: false } operation) continue;
            row.State = StreamState.Stopping;
            AddLog((Cameras.IndexOf(row) + 1).ToString(CultureInfo.InvariantCulture), "Stopping");
            UpdateStatus(row);
            operation.Cancel();
        }
    }

    private Endpoint CurrentEndpoint()
    {
        if (!int.TryParse(Port, NumberStyles.None, CultureInfo.InvariantCulture, out var portNumber) ||
            !int.TryParse(Latency, NumberStyles.None, CultureInfo.InvariantCulture, out var latencyNumber))
            throw new ArgumentException("InvalidEndpoint");
        return new Endpoint(Host, portNumber, latencyNumber);
    }

    private void CopyUrl(CameraRow row)
    {
        try
        {
            var url = Streaming.Url(CurrentEndpoint(), row.Path.Trim(), new Credentials(ReaderUser, ReaderPassword), false);
            Clipboard.SetText(url);
            Notify("Copied");
        }
        catch (ArgumentException exception) { Notify(exception.Message); }
        catch (System.Runtime.InteropServices.ExternalException) { Notify("ClipboardError"); }
    }

    private void SaveProfile()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(ProfileName)) { Notify("ProfileError"); return; }
            var name = ProfileName.Trim();
            var id = Profiles.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Id ?? Guid.NewGuid();
            store.Save(id, new Profile(name, CurrentEndpoint(), Cameras.Select(c => c.Snapshot()).ToArray(), Language, ffmpegPath),
                new Credentials(PublishUser, PublishPassword));
            RefreshProfiles();
            SelectedProfile = Profiles.FirstOrDefault(p => p.Id == id);
            Notify("Saved");
        }
        catch (ArgumentException exception) { Notify(exception.Message); }
        catch (Exception) { Notify("ProfileError"); }
    }

    private async Task LoadProfileAsync()
    {
        try
        {
            var (profile, credentials) = store.Load(SelectedProfile!.Id);
            ProfileName = profile.Name;
            Host = profile.Endpoint.Host;
            Port = profile.Endpoint.Port.ToString(CultureInfo.InvariantCulture);
            Latency = profile.Endpoint.LatencyMs.ToString(CultureInfo.InvariantCulture);
            PublishUser = credentials.User;
            PublishPassword = credentials.Password;
            PublishingPasswordLoaded?.Invoke(credentials.Password);
            Language = profile.Language;
            ffmpegPath = profile.FfmpegPath;
            Cameras.Clear();
            attached.Clear();
            foreach (var camera in profile.Cameras.DistinctBy(c => c.DeviceId, StringComparer.OrdinalIgnoreCase)) AddRow(camera);
            Changed(nameof(Bandwidth));
            await InitializeAsync();
        }
        catch (Exception) { Notify("ProfileError"); }
    }

    private void RefreshProfiles()
    {
        Profiles.Clear();
        try
        {
            foreach (var entry in store.List().OrderBy(p => p.Name))
                Profiles.Add(new ProfileChoice(entry.Id, entry.Name));
        }
        catch (Exception) { Notify("ProfileError"); }
    }

    private Task WorkAsync(Func<Task> operation)
    {
        work = RunAsync();
        return work;

        async Task RunAsync()
        {
            busy = true;
            CommandManager.InvalidateRequerySuggested();
            try { await operation(); }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
            catch (InvalidDataException error) when (error.Message == "HashMismatch") { Notify("HashMismatch"); }
            catch (Exception) { Notify("SetupError"); }
            finally
            {
                busy = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public async Task CloseAsync()
    {
        StopRows(Cameras);
        shutdown.Cancel();
        await Task.WhenAll(Cameras.Select(r => r.Completion ?? Task.CompletedTask).Append(work ?? Task.CompletedTask));
        shutdown.Dispose();
    }

    private ActionCommand Command(Action<object?> action, Predicate<object?> available) =>
        new(p => { action(p); return Task.CompletedTask; }, available);
    private ActionCommand Command(Func<object?, Task> action, Predicate<object?> available) => new(action, available);
    private static void OnUi(Action action) => Application.Current.Dispatcher.Invoke(action);
    private string Text(string key) => Texts.Get(key, Language);
    private void Notify(string code) { noticeCode = code; Notice = Text(code); }
    private void UpdateStatus(CameraRow row)
    {
        var camera = row.Snapshot();
        var code = row.Operation is not null ? row.State.ToString() :
            !attached.Contains(row.DeviceId) ? "Disconnected" :
            camera.AudioId is not null && !attachedAudio.Contains(camera.AudioId) ? "MissingAudio" :
            row.ValidationError ?? row.State.ToString();
        row.Status = Text(code);
        row.FailureDetail = code == nameof(StreamState.Failed) && row.LastError is { } error ? Text(error) : "";
    }

    private void UpdateMetrics(CameraRow row)
    {
        if (row.Progress is not { } progress)
        {
            row.Metrics = "";
            return;
        }
        var culture = CultureInfo.GetCultureInfo(Language);
        var values = new List<string>();
        Add("Frames", progress.Frame);
        Add("MetricFps", progress.Fps);
        Add("MetricBitrate", progress.BitrateKbps);
        Add("MetricSpeed", progress.Speed);
        Add("MetricDup", progress.DuplicateFrames);
        Add("MetricDrop", progress.DroppedFrames);
        row.Metrics = values.Count == 0 ? "" : Text("Metrics") + "  ·  " + string.Join("  ·  ", values);

        void Add(string key, IFormattable? value)
        {
            if (value is not null)
                values.Add($"{Text(key)} {value.ToString(value is long ? "0" : "0.##", culture)}");
        }
    }

    private void AddLog(string path, string code)
    {
        if (log.Count == 120) log.Dequeue();
        log.Enqueue((DateTime.Now, path, code));
        RefreshLog();
    }

    private void RefreshLog() => Diagnostics = string.Join(Environment.NewLine,
        log.Select(e => $"{e.Time:HH:mm:ss}  {e.Path}  {Text(e.Code)}"));

    private void ReloadText()
    {
        foreach (var (key, value) in Texts.Values) Application.Current.Resources[key] = Language == "ru" ? value.Ru : value.En;
        Notify(noticeCode);
        foreach (var row in Cameras)
        {
            UpdateStatus(row);
            UpdateMetrics(row);
        }
        RefreshLog();
        Changed(nameof(Bandwidth));
        if (AudioDevices.Count > 0)
        {
            var selected = Cameras.Select(c => (Row: c, Id: c.Audio?.Id)).ToArray();
            AudioDevices = AudioDevices.Select(a => a.Id is null ? new AudioChoice(null, Text("NoAudio")) : a).ToArray();
            foreach (var item in selected) item.Row.Audio = AudioDevices.FirstOrDefault(a => a.Id == item.Id);
        }
    }
}
