"""生成 app.ico：蓝色圆角底 + 白色文档 + 绿色实时圆点。纯标准库，无依赖。"""
import math
import os
import struct
import zlib


def sd_rrect(px, py, x0, y0, x1, y1, r):
    cx, cy = (x0 + x1) / 2.0, (y0 + y1) / 2.0
    hx, hy = (x1 - x0) / 2.0, (y1 - y0) / 2.0
    dx = abs(px - cx) - (hx - r)
    dy = abs(py - cy) - (hy - r)
    if dx > 0 and dy > 0:
        return math.hypot(dx, dy) - r
    return max(dx, dy) - r


def cov(d):
    # 距离场 -> 覆盖率（1px 抗锯齿）
    a = 0.5 - d
    if a <= 0:
        return 0.0
    if a >= 1:
        return 1.0
    return a


def blend(dst, color, alpha):
    if alpha <= 0:
        return
    da = dst[3] / 255.0
    oa = alpha + da * (1.0 - alpha)
    if oa <= 0:
        return
    for i in range(3):
        dst[i] = (color[i] * alpha + dst[i] * da * (1.0 - alpha)) / oa
    dst[3] = oa * 255.0


def lerp(c1, c2, t):
    return tuple(c1[i] + (c2[i] - c1[i]) * t for i in range(3))


def render(S):
    px = [[[0.0, 0.0, 0.0, 0.0] for _ in range(S)] for _ in range(S)]

    # 1) 背景：深蓝圆角矩形（上浅下深渐变）
    for y in range(S):
        t = y / float(S - 1) if S > 1 else 0.0
        col = lerp((48, 98, 235), (24, 56, 165), t)
        for x in range(S):
            d = sd_rrect(x + 0.5, y + 0.5, 0.0, 0.0, float(S), float(S), 0.24 * S)
            a = cov(d)
            if a > 0:
                blend(px[y][x], col, a)

    # 2) 文档：白色圆角矩形
    x0, y0 = 0.255 * S, 0.185 * S
    x1, y1 = 0.745 * S, 0.815 * S
    r = 0.055 * S
    for y in range(S):
        for x in range(S):
            d = sd_rrect(x + 0.5, y + 0.5, x0, y0, x1, y1, r)
            a = cov(d)
            if a > 0:
                blend(px[y][x], (255, 255, 255), a)

    # 3) 文档里的文字横线（小尺寸省略）
    if S >= 40:
        line_col = (52, 104, 232)
        lx0, lx1 = 0.345 * S, 0.655 * S
        for i, ly in enumerate((0.345, 0.475, 0.605)):
            lyy = ly * S
            hh = 0.035 * S
            end = lx1 if i < 2 else 0.575 * S
            for y in range(S):
                for x in range(S):
                    d = sd_rrect(x + 0.5, y + 0.5, lx0, lyy, end, lyy + hh, hh / 2.0)
                    a = cov(d) * 0.95
                    if a > 0:
                        blend(px[y][x], line_col, a)

    # 4) 右下角绿色圆点（表示"实时"）
    ccx, ccy = 0.755 * S, 0.755 * S
    rad = 0.155 * S
    for y in range(S):
        for x in range(S):
            dist = math.hypot(x + 0.5 - ccx, y + 0.5 - ccy)
            a = cov(dist - rad)
            if a > 0:
                blend(px[y][x], (38, 190, 100), a)
            # 白色描边
            da = cov(abs(dist - rad) - 0.045 * S)
            if da > 0:
                blend(px[y][x], (255, 255, 255), da)

    # 输出 RGBA 字节
    rows = []
    for y in range(S):
        row = bytearray()
        for x in range(S):
            p = px[y][x]
            row += bytes((int(round(p[0])), int(round(p[1])), int(round(p[2])), int(round(p[3]))))
        rows.append(bytes(row))
    return rows


def make_png(S):
    rows = render(S)
    raw = b""
    for y in range(S):
        raw += b"\x00" + rows[y]

    def chunk(tag, data):
        return (struct.pack(">I", len(data)) + tag + data
                + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF))

    ihdr = struct.pack(">IIBBBBB", S, S, 8, 6, 0, 0, 0)
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr)
            + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


def build_ico(sizes, path):
    blobs = [(s, make_png(s)) for s in sizes]
    n = len(blobs)
    header = struct.pack("<HHH", 0, 1, n)
    offset = 6 + 16 * n
    dirs = b""
    data = b""
    for s, blob in blobs:
        dim = 0 if s >= 256 else s
        dirs += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(blob), offset)
        offset += len(blob)
        data += blob
    with open(path, "wb") as f:
        f.write(header + dirs + data)
    print("wrote %s (%d bytes, sizes=%s)" % (path, 6 + 16 * n + len(data), sizes))


if __name__ == "__main__":
    ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    build_ico([16, 24, 32, 48, 64, 128, 256], os.path.join(ROOT, "app.ico"))
