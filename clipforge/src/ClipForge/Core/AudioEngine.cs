using System.Collections.Concurrent;
using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ClipForge.Core;

public sealed record AudioDeviceInfo(string Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// Captures desktop loopback + microphone, mixes them to 48 kHz stereo and emits a steady,
/// wall-clock paced s16le stream to every attached sink (one per running ffmpeg process).
///
/// The pacing matters: WASAPI loopback delivers nothing at all while the system is silent, so a
/// naive pass-through would hand ffmpeg a stream shorter than the video and drift out of sync.
/// Reading from a MixingSampleProvider with ReadFully on a stopwatch schedule produces exactly
/// one second of audio per second of wall clock, padding silence where no device delivered.
/// </summary>
public sealed class AudioEngine : IDisposable
{
    public const int SampleRate = 48000;
    public const int Channels = 2;
    public const int BytesPerSecond = SampleRate * Channels * 2;

    private readonly object _gate = new();
    private readonly List<PcmSink> _sinks = new();

    private WasapiLoopbackCapture? _loopback;
    private WasapiCapture? _microphone;
    private BufferedWaveProvider? _loopbackBuffer;
    private BufferedWaveProvider? _microphoneBuffer;
    private MixingSampleProvider? _mixer;
    private Thread? _pump;
    private volatile bool _running;

    public bool IsRunning => _running;
    public string? LastError { get; private set; }

    public static List<AudioDeviceInfo> GetInputDevices()
    {
        var devices = new List<AudioDeviceInfo>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                using (device) devices.Add(new AudioDeviceInfo(device.ID, device.FriendlyName));
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Unable to enumerate capture devices: " + ex.Message);
        }
        return devices;
    }

    public void Start(Settings settings)
    {
        lock (_gate)
        {
            if (_running) return;
            LastError = null;

            _mixer = new MixingSampleProvider(
                WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels)) { ReadFully = true };

            if (settings.CaptureSystemAudio) StartLoopback(settings);
            if (settings.CaptureMicrophone) StartMicrophone(settings);

            _running = true;
            _pump = new Thread(PumpLoop)
            {
                IsBackground = true,
                Name = "ClipForge.AudioPump",
                Priority = ThreadPriority.AboveNormal
            };
            _pump.Start();
            Log.Info($"Audio engine started (system={settings.CaptureSystemAudio}, mic={settings.CaptureMicrophone})");
        }
    }

    private void StartLoopback(Settings settings)
    {
        try
        {
            _loopback = new WasapiLoopbackCapture();
            _loopbackBuffer = new BufferedWaveProvider(_loopback.WaveFormat)
            {
                DiscardOnBufferOverflow = true,
                BufferDuration = TimeSpan.FromSeconds(3)
            };
            _loopback.DataAvailable += (_, e) =>
                _loopbackBuffer?.AddSamples(e.Buffer, 0, e.BytesRecorded);
            _loopback.RecordingStopped += (_, e) =>
            {
                if (e.Exception is not null) Log.Warn("Loopback stopped: " + e.Exception.Message);
            };

            _mixer!.AddMixerInput(Conform(_loopbackBuffer, settings.SystemVolumePercent));
            _loopback.StartRecording();
        }
        catch (Exception ex)
        {
            LastError = "System audio unavailable: " + ex.Message;
            Log.Error("Failed to start loopback capture", ex);
            _loopback = null;
            _loopbackBuffer = null;
        }
    }

    private void StartMicrophone(Settings settings)
    {
        try
        {
            MMDevice? device = null;
            using (var enumerator = new MMDeviceEnumerator())
            {
                if (!string.IsNullOrWhiteSpace(settings.MicDeviceId))
                {
                    try { device = enumerator.GetDevice(settings.MicDeviceId); }
                    catch (Exception) { device = null; }
                }
                device ??= enumerator.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)
                    ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)
                    : null;
            }
            if (device is null)
            {
                LastError = "No microphone found";
                return;
            }

            _microphone = new WasapiCapture(device);
            _microphoneBuffer = new BufferedWaveProvider(_microphone.WaveFormat)
            {
                DiscardOnBufferOverflow = true,
                BufferDuration = TimeSpan.FromSeconds(3)
            };
            _microphone.DataAvailable += (_, e) =>
                _microphoneBuffer?.AddSamples(e.Buffer, 0, e.BytesRecorded);

            _mixer!.AddMixerInput(Conform(_microphoneBuffer, settings.MicVolumePercent));
            _microphone.StartRecording();
        }
        catch (Exception ex)
        {
            LastError = "Microphone unavailable: " + ex.Message;
            Log.Error("Failed to start microphone capture", ex);
            _microphone = null;
            _microphoneBuffer = null;
        }
    }

    /// <summary>Brings an arbitrary device format to the mixer format: stereo, 48 kHz, float.</summary>
    private static ISampleProvider Conform(BufferedWaveProvider source, int volumePercent)
    {
        ISampleProvider sample = source.ToSampleProvider();

        if (sample.WaveFormat.Channels == 1)
            sample = new MonoToStereoSampleProvider(sample);
        else if (sample.WaveFormat.Channels > 2)
            sample = new MultiplexingSampleProvider(new[] { sample }, 2);

        if (sample.WaveFormat.SampleRate != SampleRate)
            sample = new WdlResamplingSampleProvider(sample, SampleRate);

        return new VolumeSampleProvider(sample) { Volume = Math.Clamp(volumePercent, 0, 200) / 100f };
    }

    private void PumpLoop()
    {
        const int maxFramesPerRead = SampleRate / 10;   // 100 ms
        const int minFramesPerRead = SampleRate / 100;  // 10 ms

        var floats = new float[maxFramesPerRead * Channels];
        var pcm = new byte[maxFramesPerRead * Channels * 2];
        var clock = Stopwatch.StartNew();
        long framesEmitted = 0;

        while (_running)
        {
            try
            {
                var target = (long)(clock.Elapsed.TotalSeconds * SampleRate);
                var behind = target - framesEmitted;

                if (behind < minFramesPerRead)
                {
                    Thread.Sleep(4);
                    continue;
                }

                var frames = (int)Math.Min(behind, maxFramesPerRead);
                var wanted = frames * Channels;

                int got;
                lock (_gate) got = _mixer?.Read(floats, 0, wanted) ?? 0;
                if (got < wanted) Array.Clear(floats, got, wanted - got);

                for (var i = 0; i < wanted; i++)
                {
                    var clipped = Math.Clamp(floats[i], -1f, 1f);
                    var value = (short)(clipped * short.MaxValue);
                    pcm[i * 2] = (byte)(value & 0xFF);
                    pcm[i * 2 + 1] = (byte)((value >> 8) & 0xFF);
                }

                Broadcast(pcm, wanted * 2);
                framesEmitted += frames;
            }
            catch (Exception ex)
            {
                Log.Error("Audio pump iteration failed", ex);
                Thread.Sleep(50);
            }
        }
    }

    private void Broadcast(byte[] buffer, int count)
    {
        lock (_gate)
        {
            foreach (var sink in _sinks) sink.Enqueue(buffer, count);
        }
    }

    /// <summary>Attaches an ffmpeg stdin stream. Dispose the handle to detach.</summary>
    public IDisposable Attach(Stream destination, string name)
    {
        var sink = new PcmSink(destination, name, Detach);
        lock (_gate) _sinks.Add(sink);
        return sink;
    }

    private void Detach(PcmSink sink)
    {
        lock (_gate) _sinks.Remove(sink);
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!_running) return;
            _running = false;
        }

        try { _pump?.Join(1000); } catch { /* shutting down */ }

        lock (_gate)
        {
            SafeStop(_loopback);
            SafeStop(_microphone);
            _loopback = null;
            _microphone = null;
            _loopbackBuffer = null;
            _microphoneBuffer = null;
            _mixer = null;
            _pump = null;
        }
        Log.Info("Audio engine stopped");
    }

    private static void SafeStop(IWaveIn? capture)
    {
        if (capture is null) return;
        try { capture.StopRecording(); } catch (Exception ex) { Log.Warn("StopRecording: " + ex.Message); }
        try { capture.Dispose(); } catch (Exception ex) { Log.Warn("Dispose capture: " + ex.Message); }
    }

    public void Dispose() => Stop();

    /// <summary>
    /// One attached ffmpeg stdin. Writes happen on a dedicated thread so a stalled encoder can
    /// never block the audio pump; if the queue backs up we drop the oldest audio instead.
    /// </summary>
    private sealed class PcmSink : IDisposable
    {
        private readonly BlockingCollection<byte[]> _queue = new(boundedCapacity: 64);
        private readonly Stream _destination;
        private readonly Action<PcmSink> _onDispose;
        private readonly Thread _writer;
        private volatile bool _closed;

        public PcmSink(Stream destination, string name, Action<PcmSink> onDispose)
        {
            _destination = destination;
            _onDispose = onDispose;
            _writer = new Thread(WriteLoop) { IsBackground = true, Name = "ClipForge.Audio." + name };
            _writer.Start();
        }

        public void Enqueue(byte[] buffer, int count)
        {
            if (_closed) return;
            var copy = new byte[count];
            Buffer.BlockCopy(buffer, 0, copy, 0, count);
            if (_queue.TryAdd(copy)) return;

            // Queue is full: shed the oldest chunk so live audio keeps flowing.
            if (_queue.TryTake(out _)) _queue.TryAdd(copy);
        }

        private void WriteLoop()
        {
            try
            {
                foreach (var chunk in _queue.GetConsumingEnumerable())
                {
                    if (_closed) break;
                    _destination.Write(chunk, 0, chunk.Length);
                }
            }
            catch (Exception ex)
            {
                // Expected when ffmpeg exits and closes the pipe.
                Log.Info("Audio sink closed: " + ex.GetType().Name);
            }
        }

        public void Dispose()
        {
            if (_closed) return;
            _closed = true;
            _onDispose(this);
            try { _queue.CompleteAdding(); } catch { /* already completed */ }
            try { _writer.Join(500); } catch { /* shutting down */ }
            try { _destination.Flush(); } catch { /* pipe may be gone */ }
            try { _destination.Close(); } catch { /* pipe may be gone */ }
            _queue.Dispose();
        }
    }
}
