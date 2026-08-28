"""合成一组简短、柔和的萌系提示音效（16-bit PCM WAV），供桌宠互动/照顾/升级使用。
纯标准库，无第三方依赖。运行：python tools/make_sounds.py
输出到 assets/sounds/ 下。"""
import math
import os
import struct
import wave

SR = 44100
OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "assets", "sounds")


def _tone(freq, dur, vol=1.0, glide_to=None, attack=0.008):
    """生成一段带线性滑频 + 指数衰减包络的正弦波。attack 秒内淡入避免爆音。"""
    n = int(SR * dur)
    phase = 0.0
    out = []
    for i in range(n):
        t = i / SR
        f = freq if glide_to is None else freq + (glide_to - freq) * (i / n)
        phase += 2 * math.pi * f / SR
        # 包络：快速淡入 + 指数衰减
        a = min(1.0, t / attack) if attack > 0 else 1.0
        env = a * math.exp(-t * 7.0)
        out.append(math.sin(phase) * env * vol)
    return out


def _mix(*tracks):
    """把多段波形按时间偏移叠加（长度取最长）。"""
    n = max((len(t) for t in tracks), default=0)
    out = [0.0] * n
    for t in tracks:
        for i, v in enumerate(t):
            out[i] += v
    return out


def _offset(samples, sec):
    """把波形整体后移 sec 秒（前面补 0）。"""
    pad = int(SR * sec)
    return [0.0] * pad + list(samples)


def _write(name, samples, peak=0.5):
    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, name)
    m = max(1e-9, max(abs(s) for s in samples))
    scale = peak / m
    with wave.open(path, "w") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        frames = b"".join(
            struct.pack("<h", int(max(-1.0, min(1.0, s * scale)) * 32767)) for s in samples
        )
        w.writeframes(frames)
    print(f"  {name}  ({len(samples) / SR:.2f}s)")


def main():
    print("合成音效 ->", OUT)

    # 摸头/轻点：短促的"叮"（880Hz，快衰减）
    _write("tap.wav", _tone(880, 0.16, glide_to=1046, attack=0.004))

    # 戳肚子：短"啵"（250Hz 低沉短促）
    _write("pop.wav", _tone(250, 0.10, glide_to=180, attack=0.003))

    # 喂食：两声"咕嘟"（250→200，间隔 0.06s）
    eat = _mix(_tone(250, 0.10, glide_to=210),
               _offset(_tone(210, 0.12, glide_to=170), 0.10))
    _write("eat.wav", eat)

    # 玩耍：欢快上行琶音 C5-E5-G5
    play = _mix(_tone(523, 0.14, vol=0.9),
                _offset(_tone(659, 0.14, vol=0.9), 0.12),
                _offset(_tone(784, 0.20, vol=0.9), 0.24))
    _write("play.wav", play)

    # 升级：上行琶音 C5-E5-G5-C6 + 尾音
    levelup = _mix(_tone(523, 0.14, vol=0.9),
                   _offset(_tone(659, 0.14, vol=0.9), 0.11),
                   _offset(_tone(784, 0.14, vol=0.9), 0.22),
                   _offset(_tone(1047, 0.30, vol=0.9), 0.33))
    _write("levelup.wav", levelup)

    # 受惊：短促高频下滑（900→400）
    _write("startle.wav", _tone(900, 0.24, glide_to=400, attack=0.003))

    # 问候/签到：柔和双音（E5 起，回落）
    greet = _mix(_tone(659, 0.18, vol=0.85),
                 _offset(_tone(523, 0.22, vol=0.85), 0.15))
    _write("greet.wav", greet)


if __name__ == "__main__":
    main()
