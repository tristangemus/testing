using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace ClipForge.Core;

public enum RecorderStatus { Idle, Starting, Buffering, Recording, BufferingAndRecording, Error }

public sealed class RecorderStatusEventArgs(RecorderStatus status, string message) : EventArgs
{
    public RecorderStatus Status { get; } = status;
    public string Message { get; } = message;
}

public sealed class ClipSavedEventArgs(ClipInfo clip, bool fromReplayBuffer) : EventArgs
{
    public ClipInfo Clip { get; } = clip;
    public bool FromReplayBuffer { get; } = fromReplayBuffer;
}

/// <summary>
/// Owns the ffmpeg capture processes. Two independent pipelines can run at once: a continuous
/// replay-buffer ring and an explicit recording. Both write mpegts, which survives an abrupt
/// process kill; the mp4 the user keeps is always produced by a fast stream-copy remux.
/// </summary>
public sealed class RecorderEngine : IDisposable
{
    private const int SegmentSeconds = 2;

    private readonly object _gate = new();
    private readonly AudioEngine _audio = new();
    private Settings _settings;

    private CaptureSession? _buffer;
    private CaptureSession? _recording;
    private int _audioUsers;
    private bool _ddagrabAvailable;
    private string? _ffmpeg;
    private string? _ffprobe;
    private string _encoderId = "libx264";

    // Probing encoders spawns a test encode per candidate, so the result is cached against the
    // binary it was measured with and only redone when that binary changes.
    private string? _probedBinary;
    private string? _probedBestEncoder;

    public event EventHandler<RecorderStatusEventArgs>? StatusChanged;
    public event EventHandler<ClipSavedEventArgs>? ClipSaved;
    public event EventHandler<string>? Failed;

    public RecorderEngine(Settings settings) => _settings = settings;

    public bool IsBuffering { get { lock (_gate) return _buffer is not null; } }
    public bool IsRecording { get { lock (_gate) return _recording is not null; } }
    public string EncoderId => _encoderId;
    public DateTime? RecordingStartedUtc { get { lock (_gate) return _recording?.StartedUtc; } }

    public void ApplySettings(Settings settings) => _settings = settings;

    /// <summary>Resolves ffmpeg and picks an encoder. Safe to call repeatedly.</summary>
    public async Task<bool> InitializeAsync()
    {
        _ffmpeg = FFmpegManager.Resolve(_settings);
        _ffprobe = FFmpegManager.ResolveProbe(_settings);
        if (_ffmpeg is null)
        {
            RaiseFailed("ffmpeg was not found. Open Settings to download it.");
            return false;
        }

        try
        {
            if (_probedBinary != _ffmpeg)
            {
                _ddagrabAvailable = await FFmpegManager.SupportsDdagrabAsync(_ffmpeg);
                var encoders = await FFmpegManager.DetectEncodersAsync(_ffmpeg);
                _probedBestEncoder = encoders.First().Id;
                _probedBinary = _ffmpeg;
            }

            _encoderId = string.Equals(_settings.Encoder, "auto", StringComparison.OrdinalIgnoreCase)
                ? _probedBestEncoder ?? "libx264"
                : _settings.Encoder;
            Log.Info($"Recorder ready: encoder={_encoderId}, ddagrab={_ddagrabAvailable}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Recorder initialization failed", ex);
            RaiseFailed("Could not initialise ffmpeg: " + ex.Message);
            return false;
        }
    }

    // ---------------------------------------------------------------- replay buffer

    public async Task<bool> StartBufferAsync()
    {
        if (IsBuffering) return true;
        if (_ffmpeg is null && !await InitializeAsync()) return false;

        AppPaths.EnsureAll();
        ClearDirectory(AppPaths.BufferDir);

        var segments = (int)Math.Ceiling(_settings.ReplayLengthSeconds / (double)SegmentSeconds) + 2;
        var pattern = Path.Combine(AppPaths.BufferDir, "seg%04d.ts");

        var output = new List<string>
        {
            "-f", "segment",
            "-segment_time", SegmentSeconds.ToString(CultureInfo.InvariantCulture),
            "-segment_format", "mpegts",
            "-segment_wrap", segments.ToString(CultureInfo.InvariantCulture),
            "-reset_timestamps", "1",
            "-flush_packets", "1",
            pattern
        };

        var session = await StartSessionAsync("replay-buffer", output);
        if (session is null) return false;

        lock (_gate) _buffer = session;
        RaiseStatus();
        return true;
    }

    public void StopBuffer()
    {
        CaptureSession? session;
        lock (_gate) { session = _buffer; _buffer = null; }
        if (session is null) return;

        session.Stop();
        ReleaseAudio();
        ClearDirectory(AppPaths.BufferDir);
        RaiseStatus();
    }

    /// <summary>Writes the last N seconds of the ring buffer to a clip.</summary>
    public async Task<ClipInfo?> SaveReplayAsync()
    {
        if (!IsBuffering)
        {
            RaiseFailed("The replay buffer is not running.");
            return null;
        }
        if (_ffmpeg is null) return null;

        var stopwatch = Stopwatch.StartNew();
        var work = Path.Combine(AppPaths.TempDir, "replay-" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            Directory.CreateDirectory(work);

            var wanted = (int)Math.Ceiling(_settings.ReplayLengthSeconds / (double)SegmentSeconds) + 1;
            var parts = Directory.GetFiles(AppPaths.BufferDir, "seg*.ts")
                .Select(path => new FileInfo(path))
                .Where(info => info.Length > 1024)
                .OrderBy(info => info.LastWriteTimeUtc)
                .TakeLast(wanted)
                .ToList();

            if (parts.Count == 0)
            {
                RaiseFailed("The replay buffer has not captured anything yet.");
                return null;
            }

            // Copy out of the ring before concatenating: ffmpeg is still writing the newest segment
            // and may recycle the oldest one at any moment.
            var copied = new List<string>();
            for (var i = 0; i < parts.Count; i++)
            {
                var destination = Path.Combine(work, $"part{i:D4}.ts");
                if (CopyShared(parts[i].FullName, destination)) copied.Add(destination);
            }
            if (copied.Count == 0)
            {
                RaiseFailed("Could not read the replay buffer segments.");
                return null;
            }

            var listFile = Path.Combine(work, "concat.txt");
            await File.WriteAllTextAsync(listFile, BuildConcatList(copied), Encoding.UTF8);

            var joined = Path.Combine(work, "joined.ts");
            var concatArgs = new List<string>
            {
                "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
                "-f", "concat", "-safe", "0", "-i", listFile,
                "-c", "copy", "-fflags", "+genpts", joined
            };
            if (!await RunToCompletionAsync(concatArgs, TimeSpan.FromMinutes(3)))
            {
                RaiseFailed("Failed to assemble the replay clip.");
                return null;
            }

            var outputPath = BuildClipPath("Replay");
            var start = 0.0;
            if (_ffprobe is not null)
            {
                var duration = await FFmpegManager.GetDurationSecondsAsync(_ffprobe, joined);
                if (duration > _settings.ReplayLengthSeconds + 0.5)
                    start = duration - _settings.ReplayLengthSeconds;
            }

            var trimArgs = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin", "-y" };
            if (start > 0.5)
            {
                trimArgs.Add("-ss");
                trimArgs.Add(start.ToString("0.###", CultureInfo.InvariantCulture));
            }
            trimArgs.AddRange(new[]
            {
                "-i", joined, "-c", "copy",
                "-avoid_negative_ts", "make_zero",
                "-movflags", "+faststart", outputPath
            });

            if (!await RunToCompletionAsync(trimArgs, TimeSpan.FromMinutes(3)))
            {
                RaiseFailed("Failed to write the replay clip.");
                return null;
            }

            var clip = await DescribeClipAsync(outputPath);
            Log.Info($"Saved replay {outputPath} in {stopwatch.ElapsedMilliseconds} ms");
            ClipSaved?.Invoke(this, new ClipSavedEventArgs(clip, fromReplayBuffer: true));
            return clip;
        }
        catch (Exception ex)
        {
            Log.Error("SaveReplay failed", ex);
            RaiseFailed("Saving the replay failed: " + ex.Message);
            return null;
        }
        finally
        {
            TryDeleteDirectory(work);
        }
    }

    // ---------------------------------------------------------------- manual recording

    public async Task<bool> StartRecordingAsync()
    {
        if (IsRecording) return true;
        if (_ffmpeg is null && !await InitializeAsync()) return false;

        AppPaths.EnsureAll();
        var scratch = Path.Combine(AppPaths.TempDir, "rec-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".ts");
        var output = new List<string> { "-f", "mpegts", "-flush_packets", "1", scratch };

        var session = await StartSessionAsync("recording", output);
        if (session is null) return false;
        session.ScratchFile = scratch;

        lock (_gate) _recording = session;
        RaiseStatus();
        return true;
    }

    public async Task<ClipInfo?> StopRecordingAsync()
    {
        CaptureSession? session;
        lock (_gate) { session = _recording; _recording = null; }
        if (session is null) return null;

        session.Stop();
        ReleaseAudio();
        RaiseStatus();

        var scratch = session.ScratchFile;
        if (scratch is null || !File.Exists(scratch) || new FileInfo(scratch).Length < 1024)
        {
            RaiseFailed("The recording produced no data.");
            TryDelete(scratch);
            return null;
        }

        try
        {
            var outputPath = BuildClipPath(session.SourceLabel);
            var remux = new List<string>
            {
                "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
                "-i", scratch, "-c", "copy",
                "-movflags", "+faststart", outputPath
            };
            if (!await RunToCompletionAsync(remux, TimeSpan.FromMinutes(10)))
            {
                RaiseFailed("Failed to finalise the recording. The raw capture was kept: " + scratch);
                return null;
            }

            TryDelete(scratch);
            var clip = await DescribeClipAsync(outputPath);
            ClipSaved?.Invoke(this, new ClipSavedEventArgs(clip, fromReplayBuffer: false));
            return clip;
        }
        catch (Exception ex)
        {
            Log.Error("StopRecording failed", ex);
            RaiseFailed("Finalising the recording failed: " + ex.Message);
            return null;
        }
    }

    // ---------------------------------------------------------------- session plumbing

    private async Task<CaptureSession?> StartSessionAsync(string name, List<string> outputArgs)
    {
        var backend = CaptureCommand.ChooseBackend(_settings, _ddagrabAvailable);
        var session = await TryStartAsync(name, outputArgs, backend);

        // Desktop Duplication fails on some driver/permission combinations (RDP, locked GPU,
        // protected content). Fall back to GDI rather than leaving the user with nothing.
        if (session is null && backend == CaptureCommand.BackendDesktopDuplication)
        {
            Log.Warn("ddagrab pipeline failed to start; retrying with gdigrab");
            session = await TryStartAsync(name, outputArgs, CaptureCommand.BackendGdi);
        }

        if (session is null)
            RaiseFailed("Could not start the capture pipeline. See the log for the ffmpeg error.");
        return session;
    }

    private async Task<CaptureSession?> TryStartAsync(string name, List<string> outputArgs, string backend)
    {
        if (_ffmpeg is null) return null;

        var useAudio = _settings.CaptureSystemAudio || _settings.CaptureMicrophone;
        var args = new List<string> { "-hide_banner", "-loglevel", "warning" };
        args.AddRange(CaptureCommand.BuildVideoInput(_settings, backend));
        if (useAudio) args.AddRange(CaptureCommand.BuildAudioInput());

        args.Add("-map"); args.Add("0:v");
        if (useAudio) { args.Add("-map"); args.Add("1:a"); }

        args.Add("-vf"); args.Add(CaptureCommand.BuildVideoFilter(_settings, backend));
        args.AddRange(CaptureCommand.BuildEncoderArgs(_settings, _encoderId));
        if (useAudio) args.AddRange(CaptureCommand.BuildAudioEncoderArgs(_settings));
        args.Add("-y");
        args.AddRange(outputArgs);

        Log.Info($"Starting {name} [{backend}]: ffmpeg {string.Join(' ', args)}");

        CaptureSession session;
        try
        {
            var process = FFmpegManager.StartHidden(_ffmpeg, args, redirectStdIn: useAudio);
            session = new CaptureSession(process, name, DescribeSource());
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to spawn ffmpeg for {name}", ex);
            return null;
        }

        session.BeginDrainingStderr();

        if (useAudio)
        {
            AcquireAudio();
            session.AudioAttachment = _audio.Attach(session.Process.StandardInput.BaseStream, name);
        }

        // A bad capture source makes ffmpeg exit almost immediately; give it a moment to fail.
        await Task.Delay(1500);
        if (session.Process.HasExited)
        {
            Log.Warn($"{name} exited early (code {session.Process.ExitCode}): {session.StderrTail()}");
            session.Stop();
            if (useAudio) ReleaseAudio();
            return null;
        }
        return session;
    }

    private void AcquireAudio()
    {
        lock (_gate)
        {
            if (_audioUsers++ == 0) _audio.Start(_settings);
        }
    }

    private void ReleaseAudio()
    {
        lock (_gate)
        {
            if (_audioUsers > 0 && --_audioUsers == 0) _audio.Stop();
        }
    }

    // ---------------------------------------------------------------- helpers

    private string DescribeSource()
    {
        if (_settings.CaptureMode == CaptureMode.Window && !string.IsNullOrWhiteSpace(_settings.WindowTitle))
            return _settings.WindowTitle;
        var game = CaptureTargets.DetectForegroundGame();
        return game?.Title ?? "Desktop";
    }

    private string BuildClipPath(string label)
    {
        Directory.CreateDirectory(_settings.ClipFolder);
        var safe = SanitizeFileName(label);
        if (safe.Length > 48) safe = safe[..48].TrimEnd();
        if (string.IsNullOrWhiteSpace(safe)) safe = "Clip";

        var stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var path = Path.Combine(_settings.ClipFolder, $"{safe}_{stamp}.mp4");

        var counter = 2;
        while (File.Exists(path))
            path = Path.Combine(_settings.ClipFolder, $"{safe}_{stamp}_{counter++}.mp4");
        return path;
    }

    public static string SanitizeFileName(string value)
    {
        var builder = new StringBuilder(value.Length);
        var invalid = Path.GetInvalidFileNameChars();
        foreach (var c in value)
            builder.Append(Array.IndexOf(invalid, c) >= 0 ? '-' : c);
        return builder.ToString().Trim();
    }

    private async Task<ClipInfo> DescribeClipAsync(string path)
    {
        double duration = 0;
        if (_ffprobe is not null)
        {
            try { duration = await FFmpegManager.GetDurationSecondsAsync(_ffprobe, path); }
            catch (Exception ex) { Log.Warn("ffprobe duration failed: " + ex.Message); }
        }

        var clip = new ClipInfo
        {
            Path = path,
            Name = Path.GetFileNameWithoutExtension(path),
            CreatedUtc = File.GetCreationTimeUtc(path),
            DurationSeconds = duration,
            SizeBytes = new FileInfo(path).Length
        };

        if (_ffmpeg is not null)
            clip.ThumbnailPath = await ClipLibrary.EnsureThumbnailAsync(_ffmpeg, clip);
        return clip;
    }

    private async Task<bool> RunToCompletionAsync(List<string> args, TimeSpan timeout)
    {
        if (_ffmpeg is null) return false;
        try
        {
            using var process = FFmpegManager.StartHidden(_ffmpeg, args);
            var stderr = process.StandardError.ReadToEndAsync();
            using var cts = new CancellationTokenSource(timeout);
            await process.WaitForExitAsync(cts.Token);
            if (process.ExitCode != 0)
                Log.Warn($"ffmpeg exited {process.ExitCode}: {await stderr}");
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Log.Error("ffmpeg helper invocation failed", ex);
            return false;
        }
    }

    private static string BuildConcatList(IEnumerable<string> files)
    {
        var builder = new StringBuilder();
        foreach (var file in files)
        {
            // concat demuxer: single-quote the path and escape embedded quotes.
            var escaped = file.Replace("'", @"'\''");
            builder.Append("file '").Append(escaped).Append('\'').Append('\n');
        }
        return builder.ToString();
    }

    /// <summary>Copies a file ffmpeg still holds open for writing.</summary>
    private static bool CopyShared(string source, string destination)
    {
        try
        {
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var output = File.Create(destination);
            input.CopyTo(output);
            return output.Length > 0;
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not copy buffer segment {source}: {ex.Message}");
            return false;
        }
    }

    private static void ClearDirectory(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            foreach (var file in Directory.GetFiles(directory)) TryDelete(file);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not clear {directory}: {ex.Message}");
        }
    }

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try { if (File.Exists(path)) File.Delete(path); } catch { /* in use */ }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { /* in use */ }
    }

    private void RaiseFailed(string message)
    {
        Log.Warn("Recorder: " + message);
        Failed?.Invoke(this, message);
    }

    private void RaiseStatus()
    {
        var (status, text) = (IsBuffering, IsRecording) switch
        {
            (true, true) => (RecorderStatus.BufferingAndRecording, "Recording + replay buffer armed"),
            (true, false) => (RecorderStatus.Buffering,
                $"Replay buffer armed - last {_settings.ReplayLengthSeconds}s"),
            (false, true) => (RecorderStatus.Recording, "Recording"),
            _ => (RecorderStatus.Idle, "Idle")
        };
        StatusChanged?.Invoke(this, new RecorderStatusEventArgs(status, text));
    }

    public void Dispose()
    {
        try { StopBuffer(); } catch { /* shutting down */ }
        CaptureSession? recording;
        lock (_gate) { recording = _recording; _recording = null; }
        recording?.Stop();
        _audio.Dispose();
    }

    /// <summary>A single running ffmpeg capture process.</summary>
    private sealed class CaptureSession(Process process, string name, string sourceLabel)
    {
        private readonly Queue<string> _stderr = new();

        public Process Process { get; } = process;
        public string Name { get; } = name;
        public string SourceLabel { get; } = sourceLabel;
        public DateTime StartedUtc { get; } = DateTime.UtcNow;
        public string? ScratchFile { get; set; }
        public IDisposable? AudioAttachment { get; set; }

        public void BeginDrainingStderr()
        {
            // ffmpeg blocks once the stderr pipe fills, so it must always be read.
            Process.ErrorDataReceived += (_, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data)) return;
                lock (_stderr)
                {
                    _stderr.Enqueue(e.Data);
                    while (_stderr.Count > 40) _stderr.Dequeue();
                }
                Log.Info($"[ffmpeg:{Name}] {e.Data}");
            };
            Process.BeginErrorReadLine();
        }

        public string StderrTail()
        {
            lock (_stderr) return string.Join(" | ", _stderr);
        }

        public void Stop()
        {
            try { AudioAttachment?.Dispose(); } catch { /* pipe closing */ }
            try
            {
                if (!Process.HasExited)
                {
                    Process.Kill(entireProcessTree: true);
                    Process.WaitForExit(5000);
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"Stopping {Name}: {ex.Message}");
            }
            finally
            {
                try { Process.Dispose(); } catch { /* already disposed */ }
            }
        }
    }
}
