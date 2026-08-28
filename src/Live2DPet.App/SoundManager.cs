using System;
using System.Collections.Generic;
using System.IO;
using System.Media;

namespace Live2DPet.App;

/// <summary>
/// 桌宠音效播放器：播放 assets/sounds 下的 WAV。
/// 带总开关 + 音量（对 16-bit PCM 做振幅缩放，无需外部依赖）+ 同名最小播放间隔（防高频刷屏）。
/// 文件缺失/播放失败时静默忽略，绝不致命。
/// </summary>
public sealed class SoundManager : IDisposable
{
    private readonly string _dir;
    private readonly Dictionary<string, byte[]> _raw = new();          // 原始 WAV 字节（不缩放）
    private readonly Dictionary<string, SoundPlayer> _players = new(); // 按当前音量构建的播放器（缓存）
    private readonly Dictionary<string, DateTime> _lastPlay = new();
    private const int MinIntervalMs = 70;   // 同名音效最小播放间隔

    /// <summary>音效总开关（设置里可关）。</summary>
    public bool Enabled { get; set; } = true;

    private int _volume = 80;
    /// <summary>音量 0..100。变更后清空已构建的播放器，下次播放按新音量重建。</summary>
    public int Volume
    {
        get => _volume;
        set
        {
            int v = Math.Clamp(value, 0, 100);
            if (_volume != v) { _volume = v; _players.Clear(); }
        }
    }

    public SoundManager(string dir) => _dir = dir;

    public void Play(string name)
    {
        if (!Enabled || string.IsNullOrEmpty(name)) return;

        var now = DateTime.UtcNow;
        if (_lastPlay.TryGetValue(name, out var last) && (now - last).TotalMilliseconds < MinIntervalMs)
            return;
        _lastPlay[name] = now;

        try
        {
            var player = GetPlayer(name);
            player?.Play();
        }
        catch { /* 播放失败不致命 */ }
    }

    private SoundPlayer? GetPlayer(string name)
    {
        if (_players.TryGetValue(name, out var p)) return p;
        var bytes = GetRaw(name);
        if (bytes == null) return null;
        byte[] data = _volume >= 100 ? bytes : ScaleVolume((byte[])bytes.Clone(), _volume / 100f);
        p = new SoundPlayer(new MemoryStream(data));
        _players[name] = p;
        return p;
    }

    private byte[]? GetRaw(string name)
    {
        if (_raw.TryGetValue(name, out var b)) return b;
        var path = Path.Combine(_dir, name + ".wav");
        if (!File.Exists(path)) return null;
        b = File.ReadAllBytes(path);
        _raw[name] = b;
        return b;
    }

    /// <summary>对 16-bit PCM 的采样做线性振幅缩放（就地写回副本）。其它格式原样返回。</summary>
    private static byte[] ScaleVolume(byte[] wav, float vol)
    {
        try
        {
            if (wav.Length < 44) return wav;
            if (BitConverter.ToInt32(wav, 0) != 0x46464952) return wav;   // "RIFF"
            if (BitConverter.ToInt32(wav, 8) != 0x45564157) return wav;   // "WAVE"
            if (BitConverter.ToInt16(wav, 34) != 16) return wav;          // 仅 16-bit PCM
            int pos = 12;
            while (pos + 8 <= wav.Length)
            {
                int id = BitConverter.ToInt32(wav, pos);
                int size = BitConverter.ToInt32(wav, pos + 4);
                if (id == 0x61746164)   // "data"
                {
                    int start = pos + 8;
                    int end = Math.Min(start + size, wav.Length);
                    for (int i = start; i + 1 < end; i += 2)
                    {
                        short s = (short)(wav[i] | (wav[i + 1] << 8));
                        int v = (int)(s * vol);
                        if (v > 32767) v = 32767; else if (v < -32768) v = -32768;
                        wav[i] = (byte)v;
                        wav[i + 1] = (byte)(v >> 8);
                    }
                    return wav;
                }
                pos += 8 + size + (size & 1);
            }
        }
        catch { /* 解析失败 → 原样播放 */ }
        return wav;
    }

    public void Dispose()
    {
        foreach (var p in _players.Values)
        {
            try { p.Stop(); } catch { }
            p.Dispose();
        }
        _players.Clear();
        _raw.Clear();
    }
}
