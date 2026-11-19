using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Security.Cryptography;
using MiRemote.Core;

namespace MiRemote.App
{
    /// <summary>
    /// 遥控器 ATVV 麦克风链路（Windows BLE GATT）。
    /// 只负责握手、切帧、ADPCM 解码；音频先落 WAV，后续接虚拟麦克风。
    /// </summary>
    public sealed class AtvvBridge
    {
        private static readonly Guid ServiceUuid = Guid.Parse("ab5e0001-5a21-4f05-bc7d-af01f617b664");
        private static readonly Guid TxUuid = Guid.Parse("ab5e0002-5a21-4f05-bc7d-af01f617b664");
        private static readonly Guid AudioUuid = Guid.Parse("ab5e0003-5a21-4f05-bc7d-af01f617b664");
        private static readonly Guid ControlUuid = Guid.Parse("ab5e0004-5a21-4f05-bc7d-af01f617b664");

        private readonly Logger _logger;
        private readonly object _gate = new();
        private readonly AdpcmDecoder _decoder = new();
        private readonly PcmPostprocessor _post = new(0);
        private readonly WavSessionSink _wav = new();
        private readonly WaveOutSink _output = new();

        private BluetoothLEDevice? _device;
        private GattCharacteristic? _tx;
        private GattCharacteristic? _audio;
        private GattCharacteristic? _control;

        private int _capsFrameLen = 120;
        private ushort _version;
        private ushort _sessionId;
        private bool _capsReady;
        private bool _micOpenPending;
        private bool _streaming;
        private bool _headerMode;
        private bool _frameProbed;
        private bool _waitingResync;
        private FrameAccumulator _accumulator = new(120);
        private readonly List<byte> _probe = new();
        private (short Predictor, int StepIndex)? _pendingSync;

        public string Status { get; private set; } = "未连接";
        public event Action<string>? Log;
        public event Action<bool>? VoiceActive;

        public AtvvBridge(Logger logger) => _logger = logger;

        public async Task StartAsync()
        {
            try
            {
                var infos = await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelector());
                var target = infos.FirstOrDefault(i => i.Name.Contains("小米蓝牙语音遥控器", StringComparison.OrdinalIgnoreCase))
                             ?? infos.FirstOrDefault(i => i.Name.Contains("MI RC", StringComparison.OrdinalIgnoreCase));
                if (target is null) { SetStatus("未找到遥控器 BLE 设备"); return; }
                LogMsg("BLE 设备: " + target.Name);
                _device = await BluetoothLEDevice.FromIdAsync(target.Id);
                if (_device is null) { SetStatus("BLE 连接失败"); return; }

                var services = await _device.GetGattServicesAsync(BluetoothCacheMode.Uncached);
                if (services.Status != GattCommunicationStatus.Success) { SetStatus("GATT 服务读取失败: " + services.Status); return; }
                var service = services.Services.FirstOrDefault(s => s.Uuid == ServiceUuid);
                if (service is null) { SetStatus("未找到 ATVV 服务"); return; }

                var chars = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
                foreach (var c in chars.Characteristics)
                {
                    if (c.Uuid == TxUuid) _tx = c;
                    else if (c.Uuid == AudioUuid) _audio = c;
                    else if (c.Uuid == ControlUuid) _control = c;
                }
                if (_tx is null || _audio is null || _control is null) { SetStatus("ATVV 特征不完整"); return; }

                _audio.ValueChanged += OnAudio;
                _control.ValueChanged += OnControl;
                var audioStatus = await _audio.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);
                var ctrlStatus = await _control.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);
                if (audioStatus != GattCommunicationStatus.Success || ctrlStatus != GattCommunicationStatus.Success)
                {
                    SetStatus($"订阅失败: {audioStatus}/{ctrlStatus}");
                    return;
                }
                SetStatus("已连接，等待语音键");
                LogMsg("ATVV 订阅成功，发送 GET_CAPS");
                await WriteAsync(new byte[] { 0x0A, 0x01, 0x00, 0x00, 0x03, 0x03 });
            }
            catch (Exception ex)
            {
                SetStatus("ATVV 异常: " + ex.Message);
                LogMsg(ex.ToString());
            }
        }

        private async Task WriteAsync(byte[] payload)
        {
            if (_tx is null) return;
            try
            {
                var buffer = CryptographicBuffer.CreateFromByteArray(payload);
                await _tx.WriteValueWithResultAsync(buffer, GattWriteOption.WriteWithoutResponse);
            }
            catch (Exception ex) { LogMsg("写入失败: " + ex.Message); }
        }

        private void OnControl(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            var data = ToBytes(args.CharacteristicValue);
            if (data.Length == 0) return;
            lock (_gate)
            {
                switch (data[0])
                {
                    case 0x0B: HandleCaps(data); break;
                    case 0x08:
                        LogMsg("MIC_REQUEST");
                        if (_capsReady) _ = WriteAsync(MicOpenCommand());
                        else _micOpenPending = true;
                        break;
                    case 0x04: HandleStreamStart(data); break;
                    case 0x0A: HandleSync(data); break;
                    case 0x00: HandleStreamStop(); break;
                    default: LogMsg($"CTL 0x{data[0]:X2}"); break;
                }
            }
        }

        private void HandleCaps(byte[] data)
        {
            if (data.Length < 7 || data[0] != 0x0B) return;
            ushort version = (ushort)((data[1] << 8) | data[2]);
            ushort codec = data[3];
            if ((codec & 0x02) == 0 && data.Length > 4 && (data[4] & 0x02) != 0) codec = data[4];
            if ((codec & 0x02) == 0) { LogMsg("能力帧不支持 16kHz"); return; }
            int frame = (data[5] << 8) | data[6];
            _version = version;
            _capsFrameLen = frame == 0 ? 120 : frame;
            _accumulator = new FrameAccumulator(_capsFrameLen);
            _capsReady = true;
            LogMsg($"CAPS version=0x{version:X4} codec=0x{codec:X2} frame={_capsFrameLen}");
            SetStatus("已连接，等待语音键");
            if (_micOpenPending)
            {
                _micOpenPending = false;
                _ = WriteAsync(MicOpenCommand());
            }
        }

        private byte[] MicOpenCommand() => _version >= 0x0100 ? new byte[] { 0x0C, 0x00 } : new byte[] { 0x0C, 0x00, 0x02 };

        private byte[] MicCloseCommand()
        {
            var payload = new List<byte> { 0x0D };
            if (_version >= 0x0100) payload.Add((byte)(_sessionId & 0xFF));
            return payload.ToArray();
        }

        private void HandleStreamStart(byte[] data)
        {
            _sessionId = data.Length >= 4 ? (ushort)data[3] : (ushort)0;
            _streaming = true;
            _headerMode = false;
            _frameProbed = false;
            _waitingResync = false;
            _probe.Clear();
            _pendingSync = null;
            _accumulator = new FrameAccumulator(_capsFrameLen);
            _decoder.Reset(0, 0);
            _post.Reset();
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MiRemote");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "remote-mic.wav");
            _wav.Start(path);
            bool outputStarted = _output.Start("CABLE Input");
            LogMsg($"AUDIO_START session={_sessionId} -> {path}, 虚拟麦克风={outputStarted}");
            VoiceActive?.Invoke(true);
        }

        private void HandleSync(byte[] data)
        {
            if (data.Length < 7 || data[0] != 0x0A) return;
            short predictor = (short)((data[4] << 8) | data[5]);
            int step = data[6];
            _accumulator.Reset();
            _probe.Clear();
            if (_waitingResync) { _waitingResync = false; _frameProbed = false; }
            _pendingSync = (predictor, step);
        }

        private void HandleStreamStop()
        {
            if (!_streaming) return;
            _streaming = false;
            LogMsg("AUDIO_STOP");
            VoiceActive?.Invoke(false);
            _output.Stop();
            _wav.Stop();
            _ = WriteAsync(MicCloseCommand());
        }

        private void OnAudio(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            var data = ToBytes(args.CharacteristicValue);
            if (data.Length == 0) return;
            lock (_gate)
            {
                if (!_streaming || _waitingResync) return;
                if (!_frameProbed)
                {
                    _probe.AddRange(data);
                    if (_probe.Count < _capsFrameLen) return;
                    int plain = _capsFrameLen;
                    int headered = plain + 6;
                    if (_probe.Count == headered || (_probe.Count % headered == 0 && _probe.Count % plain != 0))
                    {
                        _headerMode = true;
                        _accumulator = new FrameAccumulator(headered);
                    }
                    else if (_probe.Count % plain == 0)
                    {
                        _headerMode = false;
                        _accumulator = new FrameAccumulator(plain);
                    }
                    else
                    {
                        _probe.Clear();
                        _waitingResync = true;
                        LogMsg("音频帧长不匹配，等待同步帧");
                        return;
                    }
                    _frameProbed = true;
                    var seed = _probe.ToArray();
                    _probe.Clear();
                    EmitFrames(_accumulator.Append(seed));
                    return;
                }
                EmitFrames(_accumulator.Append(data));
            }
        }

        private void EmitFrames(List<byte[]> frames)
        {
            foreach (var frame in frames)
            {
                byte[] payload = frame;
                var sync = TakeSync();
                if (_headerMode && frame.Length == _capsFrameLen + 6)
                {
                    short pred = (short)((frame[3] << 8) | frame[4]);
                    int step = frame[5];
                    payload = frame.Skip(6).ToArray();
                    sync = (pred, step);
                }
                if (sync.HasValue) _decoder.Reset(sync.Value.Predictor, sync.Value.StepIndex);
                var pcm = _post.Process(_decoder.Decode(payload));
                _wav.Write(pcm);
                _output.Write(pcm);
            }
        }

        private (short Predictor, int StepIndex)? TakeSync()
        {
            var s = _pendingSync;
            _pendingSync = null;
            return s;
        }

        private static byte[] ToBytes(Windows.Storage.Streams.IBuffer buffer)
        {
            CryptographicBuffer.CopyToByteArray(buffer, out var bytes);
            return bytes;
        }

        private void SetStatus(string status)
        {
            Status = status;
            LogMsg(status);
        }

        private void LogMsg(string msg)
        {
            _logger.Info("ATVV " + msg);
            Log?.Invoke(msg);
        }
    }
}
