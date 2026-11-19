using System;
using NAudio.Wave;

namespace MiRemote.Core
{
    /// <summary>把 16kHz/16bit/单声道 PCM 实时播放到指定输出设备（默认 VB-CABLE）。</summary>
    public sealed class WaveOutSink : IDisposable
    {
        private WaveOutEvent? _out;
        private BufferedWaveProvider? _buffer;
        public string DeviceName { get; private set; } = "";
        public bool IsRunning => _out is not null;

        public bool Start(string deviceName, int sampleRate = 16000)
        {
            Stop();
            int index = FindDevice(deviceName);
            if (index < 0) return false;
            try
            {
                _buffer = new BufferedWaveProvider(new WaveFormat(sampleRate, 16, 1))
                {
                    BufferDuration = TimeSpan.FromSeconds(2),
                    DiscardOnBufferOverflow = true,
                };
                _out = new WaveOutEvent { DeviceNumber = index, DesiredLatency = 80 };
                _out.Init(_buffer);
                _out.Play();
                DeviceName = deviceName;
                return true;
            }
            catch
            {
                Stop();
                return false;
            }
        }

        public void Write(short[] samples)
        {
            if (_buffer is null || samples.Length == 0) return;
            var bytes = new byte[samples.Length * 2];
            Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
            _buffer.AddSamples(bytes, 0, bytes.Length);
        }

        public void Stop()
        {
            try { _out?.Stop(); } catch { }
            _out?.Dispose();
            _out = null;
            _buffer = null;
        }

        private static int FindDevice(string name)
        {
            for (int i = 0; i < WaveOut.DeviceCount; i++)
            {
                var caps = WaveOut.GetCapabilities(i);
                if (caps.ProductName.Contains(name, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }

        public void Dispose() => Stop();
    }
}
