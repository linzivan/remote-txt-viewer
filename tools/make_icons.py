"""生成三种风格的候选图标 + 一张对比图。纯标准库。"""
import math
import os
import struct
import zlib


# ---------------- 基础绘图 ----------------

def blank(S):
    return [[[0.0, 0.0, 0.0, 0.0] for _ in range(S)] for _ in range(S)]


def sd_rrect(px, py, x0, y0, x1, y1, r):
    cx, cy = (x0 + x1) / 2.0, (y0 + y1) / 2.0
    hx, hy = (x1 - x0) / 2.0, (y1 - y0) / 2.0
    dx = abs(px - cx) - (hx - r)
    dy = abs(py - cy) - (hy - r)
    if dx > 0 and dy > 0:
        return math.hypot(dx, dy) - r
    return max(dx, dy) - r


def cov(d):
    a = 0.5 - d
    if a <= 0:
        return 0.0
    if a >= 1:
        return 1.0
    return a


def blend(dst, color, alpha):
    if alpha <= 0.0:
        return
    da = dst[3] / 255.0
    oa = alpha + da * (1.0 - alpha)
    if oa <= 0:
        return
    for i in range(3):
        dst[i] = (color[i] * alpha + dst[i] * da * (1.0 - alpha)) / oa
    dst[3] = oa * 255.0


def lerp(c1, c2, t):
    return (c1[0] + (c2[0] - c1[0]) * t,
            c1[1] + (c2[1] - c1[1]) * t,
            c1[2] + (c2[2] - c1[2]) * t)


def region(S, x0, y0, x1, y1):
    """把归一化包围盒转成整数像素范围（带 2px 余量，给抗锯齿留边）。"""
    ix0 = max(0, int(math.floor(x0 * S)) - 2)
    iy0 = max(0, int(math.floor(y0 * S)) - 2)
    ix1 = min(S, int(math.ceil(x1 * S)) + 3)
    iy1 = min(S, int(math.ceil(y1 * S)) + 3)
    return ix0, iy0, ix1, iy1


def put_rrect(px, S, x0, y0, x1, y1, r, color, alpha=1.0, grad_to=None):
    ix0, iy0, ix1, iy1 = region(S, x0, y0, x1, y1)
    for y in range(iy0, iy1):
        if grad_to is None:
            col = color
        else:
            t = (y - iy0) / max(1.0, float(iy1 - iy0 - 1))
            col = lerp(color, grad_to, t)
        for x in range(ix0, ix1):
            d = sd_rrect(x + 0.5, y + 0.5, x0 * S, y0 * S, x1 * S, y1 * S, r * S)
            a = cov(d) * alpha
            if a > 0:
                blend(px[y][x], col, a)


def put_circle(px, S, cx, cy, rad, color, alpha=1.0):
    ix0, iy0, ix1, iy1 = region(S, cx - rad, cy - rad, cx + rad, cy + rad)
    for y in range(iy0, iy1):
        for x in range(ix0, ix1):
            d = math.hypot(x + 0.5 - cx * S, y + 0.5 - cy * S) - rad * S
            a = cov(d) * alpha
            if a > 0:
                blend(px[y][x], color, a)


def put_ring(px, S, cx, cy, rad, thick, color, alpha=1.0):
    ix0, iy0, ix1, iy1 = region(S, cx - rad - thick, cy - rad - thick,
                                cx + rad + thick, cy + rad + thick)
    for y in range(iy0, iy1):
        for x in range(ix0, ix1):
            d = abs(math.hypot(x + 0.5 - cx * S, y + 0.5 - cy * S) - rad * S) - (thick * S) / 2.0
            a = cov(d) * alpha
            if a > 0:
                blend(px[y][x], color, a)


def put_bar(px, S, x0, y, x1, h, color, alpha=1.0, min_px=1.8):
    """一条横向文本行；小尺寸自动加粗保证可见。"""
    hh = max(h, min_px / S)
    r = hh / 2.0
    put_rrect(px, S, x0, y - hh / 2.0, x1, y + hh / 2.0, r, color, alpha)


# ---------------- 三种设计 ----------------

def design_a(S):
    """A: 深色终端 —— 日志行 + 闪烁光标 + 实时状态灯"""
    px = blank(S)
    # 圆角深色底（上浅下深的靛蓝黑）
    put_rrect(px, S, 0.02, 0.02, 0.98, 0.98, 0.20,
              (44, 56, 84), 1.0, grad_to=(14, 18, 27))

    # 顶部一条极淡的高光，营造玻璃感
    put_rrect(px, S, 0.06, 0.05, 0.94, 0.13, 0.04, (255, 255, 255), 0.07)

    # 三行文本
    put_bar(px, S, 0.17, 0.335, 0.70, 0.055, (176, 192, 214), 0.95)
    put_bar(px, S, 0.17, 0.485, 0.82, 0.055, (176, 192, 214), 0.95)
    put_bar(px, S, 0.17, 0.635, 0.52, 0.060, (77, 226, 176), 1.0)   # 最新一行：亮青

    # 光标块
    put_rrect(px, S, 0.575, 0.605, 0.655, 0.665, 0.012, (77, 226, 176), 0.9)

    # 右下角实时脉冲点（带光晕）
    put_circle(px, S, 0.775, 0.795, 0.115, (58, 210, 120), 0.16)
    put_circle(px, S, 0.775, 0.795, 0.072, (58, 214, 124), 1.0)
    return px


def design_b(S):
    """B: 亮色日志卡 —— 白色卡片 + 蓝色文本 + 橙色最新行"""
    px = blank(S)
    # 浅蓝灰底
    put_rrect(px, S, 0.02, 0.02, 0.98, 0.98, 0.20,
              (226, 234, 246), 1.0, grad_to=(205, 218, 238))
    # 白色卡片
    put_rrect(px, S, 0.10, 0.09, 0.90, 0.91, 0.10, (255, 255, 255), 1.0)
    # 卡片描边
    put_rrect(px, S, 0.10, 0.09, 0.90, 0.91, 0.10, (170, 188, 214), 0.9)
    put_rrect(px, S, 0.113, 0.103, 0.887, 0.897, 0.088, (255, 255, 255), 1.0)

    # 文本行
    put_bar(px, S, 0.185, 0.285, 0.66, 0.055, (104, 134, 184), 0.95)
    put_bar(px, S, 0.185, 0.435, 0.80, 0.055, (104, 134, 184), 0.95)
    put_bar(px, S, 0.185, 0.585, 0.58, 0.055, (104, 134, 184), 0.95)
    put_bar(px, S, 0.185, 0.745, 0.50, 0.062, (245, 146, 48), 1.0)   # 最新行：橙

    # 右上角实时点
    put_circle(px, S, 0.795, 0.225, 0.075, (255, 255, 255), 1.0)
    put_circle(px, S, 0.795, 0.225, 0.058, (54, 196, 118), 1.0)
    return px


def design_c(S):
    """C: 实时脉冲 —— 同心波纹 + 中心亮点 + 底部文本"""
    px = blank(S)
    # 深海军蓝底（径向感：用两层近似）
    put_rrect(px, S, 0.02, 0.02, 0.98, 0.98, 0.22,
              (32, 62, 128), 1.0, grad_to=(9, 14, 28))

    # 同心波纹（外到内）
    put_ring(px, S, 0.50, 0.44, 0.365, 0.030, (96, 190, 255), 0.20)
    put_ring(px, S, 0.50, 0.44, 0.265, 0.032, (110, 205, 255), 0.36)
    put_ring(px, S, 0.50, 0.44, 0.170, 0.034, (130, 220, 255), 0.60)

    # 中心亮点
    put_circle(px, S, 0.50, 0.44, 0.125, (110, 230, 190), 0.30)
    put_circle(px, S, 0.50, 0.44, 0.082, (72, 226, 168), 1.0)

    # 底部两条文本线
    put_bar(px, S, 0.235, 0.795, 0.765, 0.042, (150, 178, 216), 0.85)
    put_bar(px, S, 0.335, 0.895, 0.665, 0.038, (150, 178, 216), 0.55)
    return px


DESIGNS = {"A": design_a, "B": design_b, "C": design_c}


# ---------------- 输出 ----------------

def to_rows(px, S):
    rows = []
    for y in range(S):
        row = bytearray()
        for x in range(S):
            p = px[y][x]
            row += bytes((int(round(p[0])), int(round(p[1])), int(round(p[2])), int(round(p[3]))))
        rows.append(bytes(row))
    return rows


def png_bytes(px, S):
    rows = to_rows(px, S)
    raw = b""
    for y in range(S):
        raw += b"\x00" + rows[y]

    def chunk(tag, data):
        return (struct.pack(">I", len(data)) + tag + data
                + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF))

    ihdr = struct.pack(">IIBBBBB", S, S, 8, 6, 0, 0, 0)
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr)
            + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


def ico_bytes(sizes, func):
    blobs = [(s, png_bytes(func(s), s)) for s in sizes]
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
    return header + dirs + data


def composite(specs, path):
    """把多组图片拼到一张浅灰背景的对比图上。"""
    W, H = 1080, 470
    buf = [[[238.0, 241.0, 246.0, 255.0] for _ in range(W)] for _ in range(H)]

    def paste(px_src, s, left, top, border=False):
        for y in range(s):
            for x in range(s):
                p = px_src[y][x]
                a = p[3] / 255.0
                if a <= 0:
                    continue
                ty, tx = top + y, left + x
                if 0 <= ty < H and 0 <= tx < W:
                    d = buf[ty][tx]
                    for i in range(3):
                        d[i] = p[i] * a + d[i] * (1 - a)
                    d[3] = 255.0

    col = 0
    for name, func in specs:
        base_x = 20 + col * 352
        big = func(256)
        paste(big, 256, base_x + 48, 30)
        # 小尺寸
        small_x = base_x + 48
        for s in (64, 32, 16):
            im = func(s)
            paste(im, s, small_x, 320)
            small_x += s + 22
        col += 1

    # 分割线
    for i in range(1, 3):
        x = 20 + i * 352 - 16
        for y in range(20, 440):
            buf[y][x] = [214.0, 219.0, 227.0, 255.0]

    rows = []
    for y in range(H):
        row = bytearray()
        for x in range(W):
            p = buf[y][x]
            row += bytes((int(round(p[0])), int(round(p[1])), int(round(p[2])), 255))
        rows.append(bytes(row))

    raw = b""
    for y in range(H):
        raw += b"\x00" + rows[y]

    def chunk(tag, data):
        return (struct.pack(">I", len(data)) + tag + data
                + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF))

    ihdr = struct.pack(">IIBBBBB", W, H, 8, 6, 0, 0, 0)
    blob = (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr)
            + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))
    open(path, "wb").write(blob)
    print("compare ->", path)


ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


if __name__ == "__main__":
    for name, func in DESIGNS.items():
        blob = ico_bytes([16, 24, 32, 48, 64, 128, 256], func)
        p = os.path.join(ROOT, "icons", "icon_%s.ico" % name)
        open(p, "wb").write(blob)
        print("%s -> %s (%d bytes)" % (name, p, len(blob)))

    composite([("A", design_a), ("B", design_b), ("C", design_c)],
              os.path.join(ROOT, "icons", "compare.png"))
