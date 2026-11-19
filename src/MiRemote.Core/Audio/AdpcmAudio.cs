using System;
using System.Collections.Generic;
using System.IO;

namespace MiRemote.Core
{
    /// <summary>IMA/DVI ADPCM 解码（16kHz 单声道，4:1）。</summary>
    public sealed class AdpcmDecoder
    {
        private static readonly int[] StepTable =
        {
            7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31,
            34, 37, 41, 45, 50, 55, 60, 66, 73, 80, 88, 97, 107, 118, 130, 143,
            157, 173, 190, 209, 230, 253, 279, 307, 337, 371, 408, 449, 494, 544, 598, 658,
            724, 796, 876, 963, 1060, 1166, 1282, 1411, 1552, 1707, 1878, 2066, 2272, 2499, 2749, 3024,
            3327, 3660, 4026, 4428, 4871, 5358, 5894, 6484, 7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899,
            15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767,
        };
        private static readonly int[] IndexTable = { -1, -1, -1, -1, 2, 4, 6, 8 };

        private int _predictor;
        private int _stepIndex;

        public void Reset(short predictor, int stepIndex)
        {
            _predictor = predictor;
            _stepIndex = Math.Clamp(stepIndex, 0, 88);
        }

        public short[] Decode(ReadOnlySpan<byte> data)
        {
            var outSamples = new short[data.Length * 2];
            int o = 0;
            foreach (var b in data)
            {
                outSamples[o++] = DecodeNibble((byte)(b >> 4));
                outSamples[o++] = DecodeNibble((byte)(b & 0x0F));
            }
            return outSamples;
        }

        private short DecodeNibble(byte nibble)
        {
            int step = StepTable[_stepIndex];
            int sign = nibble & 0x08;
            int mag = nibble & 0x07;
            int diff = step >> 3;
            if ((mag & 4) != 0) diff += step;
            if ((mag & 2) != 0) diff += step >> 1;
            if ((mag & 1) != 0) diff += step >> 2;
            _predictor += sign != 0 ? -diff : diff;
            _predictor = Math.Clamp(_predictor, -32768, 32767);
            _stepIndex += IndexTable[nibble & 0x07];
            _stepIndex = Math.Clamp(_stepIndex, 0, 88);
            return (short)_predictor;
        }
    }

    /// <summary>按帧长把 BLE 分包重组为完整帧。</summary>
    public sealed class FrameAccumulator
    {
        private readonly int _frameSize;
        private readonly List<byte> _buffer = new();
        public FrameAccumulator(int frameSize) => _frameSize = Math.Max(1, frameSize);

        public List<byte[]> Append(ReadOnlySpan<byte> data)
        {
            foreach (var b in data) _buffer.Add(b);
            var frames = new List<byte[]>();
            while (_buffer.Count >= _frameSize)
            {
                var frame = new byte[_frameSize];
                _buffer.CopyTo(0, frame, 0, _frameSize);
                _buffer.RemoveRange(0, _frameSize);
                frames.Add(frame);
            }
            return frames;
        }

        public void Reset() => _buffer.Clear();
    }

    /// <summary>[1,2,1]/4 三点平滑 + 线性增益。</summary>
    public sealed class PcmPostprocessor
    {
        private double _gain;
        private short? _prev1;
        private short? _prev2;

        public PcmPostprocessor(double gainDb = 0) => _gain = Math.Pow(10, gainDb / 20);

        public void Reset()
        {
            _prev1 = null;
            _prev2 = null;
        }

        public short[] Process(short[] samples)
        {
            if (samples.Length == 0) return samples;
            var result = new short[samples.Length];
            for (int i = 0; i < samples.Length; i++)
            {
                int x0 = _prev2 ?? 0;
                int x1 = _prev1 ?? samples[i];
                int x2 = samples[i];
                int smoothed = _prev2 is null ? x2 : (x0 + 2 * x1 + x2) / 4;
                int scaled = (int)(smoothed * _gain);
                result[i] = (short)Math.Clamp(scaled, -32768, 32767);
                _prev2 = _prev1;
                _prev1 = samples[i];
            }
            return result;
        }
    }

    /// <summary>把一次语音会话写成 WAV，用于验证遥控器麦克风链路。</summary>
    public sealed class WavSessionSink : IDisposable
    {
        private FileStream? _file;
        private BinaryWriter? _writer;
        private int _dataBytes;
        public string? Path { get; private set; }

        public void Start(string path, int sampleRate = 16000)
        {
            Stop();
            Path = path;
            _file = File.Create(path);
            _writer = new BinaryWriter(_file);
            _writer.Write(new[] { 'R', 'I', 'F', 'F' });
            _writer.Write(0);
            _writer.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
            _writer.Write(16);
            _writer.Write((short)1);
            _writer.Write((short)1);
            _writer.Write(sampleRate);
            _writer.Write(sampleRate * 2);
            _writer.Write((short)2);
            _writer.Write((short)16);
            _writer.Write(new[] { 'd', 'a', 't', 'a' });
            _writer.Write(0);
            _dataBytes = 0;
        }

        public void Write(short[] samples)
        {
            if (_writer is null) return;
            foreach (var s in samples) _writer.Write(s);
            _dataBytes += samples.Length * 2;
        }

        public void Stop()
        {
            if (_writer is null) return;
            _writer.Flush();
            long end = _file!.Position;
            _file.Position = 4;
            _writer.Write(36 + _dataBytes);
            _file.Position = 40;
            _writer.Write(_dataBytes);
            _writer.Flush();
            _writer.Dispose();
            _file = null;
            _writer = null;
        }

        public void Dispose() => Stop();
    }
}
