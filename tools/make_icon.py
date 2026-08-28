#!/usr/bin/env python3
"""生成桌宠图标：一只可爱的橘猫脸，打包成多尺寸 .ico。"""
import struct
from PIL import Image, ImageDraw

SIZE = 1024
img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
d = ImageDraw.Draw(img)

FUR = (246, 169, 107, 255)
FUR_DARK = (238, 148, 84, 255)
INNER_EAR = (249, 201, 182, 255)
EYE = (74, 46, 34, 255)
NOSE = (224, 122, 95, 255)
BLUSH = (242, 162, 155, 110)
WHISKER = (120, 84, 64, 220)
HIGHLIGHT = (255, 255, 255, 235)

# 耳朵
d.polygon([(230, 360), (250, 130), (470, 330)], fill=FUR)
d.polygon([(554, 330), (774, 130), (794, 360)], fill=FUR)
# 内耳
d.polygon([(280, 330), (290, 180), (430, 320)], fill=INNER_EAR)
d.polygon([(594, 320), (734, 180), (744, 330)], fill=INNER_EAR)

# 头
d.ellipse([170, 330, 854, 900], fill=FUR)

# 腮红
d.ellipse([290, 660, 400, 730], fill=BLUSH)
d.ellipse([624, 660, 734, 730], fill=BLUSH)

# 眼睛
d.ellipse([340, 540, 470, 690], fill=EYE)
d.ellipse([554, 540, 684, 690], fill=EYE)
# 高光
d.ellipse([388, 560, 436, 610], fill=HIGHLIGHT)
d.ellipse([588, 560, 636, 610], fill=HIGHLIGHT)

# 鼻子
d.polygon([(512, 690), (486, 736), (538, 736)], fill=NOSE)
# 嘴
d.line([(512, 736), (512, 762)], fill=WHISKER, width=10)
d.arc([462, 740, 512, 800], start=0, end=180, fill=WHISKER, width=10)
d.arc([512, 740, 562, 800], start=0, end=180, fill=WHISKER, width=10)

# 胡须
d.line([(300, 660), (150, 630)], fill=WHISKER, width=8)
d.line([(300, 700), (150, 700)], fill=WHISKER, width=8)
d.line([(300, 740), (150, 770)], fill=WHISKER, width=8)
d.line([(724, 660), (874, 630)], fill=WHISKER, width=8)
d.line([(724, 700), (874, 700)], fill=WHISKER, width=8)
d.line([(724, 740), (874, 770)], fill=WHISKER, width=8)

sizes = [16, 24, 32, 48, 64, 128, 256]
pngs = []
for s in sizes:
    resized = img.resize((s, s), Image.LANCZOS)
    import io
    buf = io.BytesIO()
    resized.save(buf, "PNG")
    pngs.append(buf.getvalue())

# 打包 ICO（PNG 压缩条目，Vista+ 支持）
out = bytearray()
out += struct.pack("<HHH", 0, 1, len(sizes))
offset = 6 + 16 * len(sizes)
for s, data in zip(sizes, pngs):
    w = 0 if s >= 256 else s
    h = 0 if s >= 256 else s
    out += struct.pack("<BBBBHHII", w, h, 0, 0, 1, 32, len(data), offset)
    offset += len(data)
for data in pngs:
    out += data

dst = r"C:\Users\25314\WorkBuddy\2026-08-24-20-50-03\src\Live2DPet.App\icon.ico"
with open(dst, "wb") as f:
    f.write(out)
print("wrote", dst, len(out), "bytes,", len(sizes), "sizes")
