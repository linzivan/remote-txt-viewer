"""圆润清爽风格的三版图标 + 对比图。纯标准库。"""
import math
import os
import struct
import zlib


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
    ix0 = max(0, int(math.floor(x0 * S)) - 2)
    iy0 = max(0, int(math.floor(y0 * S)) - 2)
    ix1 = min(S, int(math.ceil(x1 * S)) + 3)
    iy1 = min(S, int(math.ceil(y1 * S)) + 3)
    return ix0, iy0, ix1, iy1


def put_rrect(px, S, x0, y0, x1, y1, r, color, alpha=1.0, grad_to=None, grad_vertical=True):
    ix0, iy0, ix1, iy1 = region(S, x0, y0, x1, y1)
    for y in range(iy0, iy1):
        if grad_to is None:
            col = color
        elif grad_vertical:
            t = (y - iy0) / max(1.0, float(iy1 - iy0 - 1))
            col = lerp(color, grad_to, t)
        else:
            col = lerp(color, grad_to, (x - ix0) / max(1.0, float(ix1 - ix0 - 1)))
        for x in range(ix0, ix1):
            d = sd_rrect(x + 0.5, y + 0.5, x0 * S, y0 * S, x1 * S, y1 * S, r * S)
            a = cov(d) * alpha
            if a > 0:
                blend(px[y][x], col, a)


def put_ring_rrect(px, S, x0, y0, x1, y1, r, thick, color, alpha=1.0):
    ix0, iy0, ix1, iy1 = region(S, x0, y0, x1, y1)
    for y in range(iy0, iy1):
        for x in range(ix0, ix1):
            d = abs(sd_rrect(x + 0.5, y + 0.5, x0 * S, y0 * S, x1 * S, y1 * S, r * S)) - thick * S / 2
            a = cov(d) * alpha
            if a > 0:
                blend(px[y][x], color, a)


def put_circle(px, S, cx, cy, rad, color, alpha=1.0):
    ix0, iy0, ix1, iy1 = region(S, cx - rad, cy - rad, cx + rad, cy + rad)
    for y in range(iy0, iy1):
        for x in range(ix0, ix1):
            d = math.hypot(x + 0.5 - cx * S, y + 0.5 - cy * S) - rad * S
            a = cov(d) * alpha
            if a > 0:
                blend(px[y][x], color, a)


def _dist_seg(px_, py_, x0, y0, x1, y1):
    vx = x1 - x0
    vy = y1 - y0
    L2 = vx * vx + vy * vy
    if L2 <= 1e-9:
        return math.hypot(px_ - x0, py_ - y0)
    t = ((px_ - x0) * vx + (py_ - y0) * vy) / L2
    if t < 0.0:
        t = 0.0
    elif t > 1.0:
        t = 1.0
    return math.hypot(px_ - (x0 + t * vx), py_ - (y0 + t * vy))


def put_line(px, S, x0, y0, x1, y1, width, color, alpha=1.0, min_px=1.7):
    """圆头胶囊线段。"""
    w = max(width, min_px / S)
    r = w / 2.0
    ax, ay, bx, by = x0 * S, y0 * S, x1 * S, y1 * S
    ix0, iy0, ix1, iy1 = region(S, min(x0, x1) - w, min(y0, y1) - w,
                                max(x0, x1) + w, max(y0, y1) + w)
    for y in range(iy0, iy1):
        for x in range(ix0, ix1):
            d = _dist_seg(x + 0.5, y + 0.5, ax, ay, bx, by) - r * S
            a = cov(d) * alpha
            if a > 0:
                blend(px[y][x], color, a)


def put_polyline(px, S, pts, width, color, alpha=1.0):
    for i in range(len(pts) - 1):
        put_line(px, S, pts[i][0], pts[i][1], pts[i + 1][0], pts[i + 1][1], width, color, alpha)


R_BIG = 0.30   # 大圆角，接近 squircle 的观感


# ---------------- D: 柔和蓝 + 三条圆头线 + 在线点 ----------------
def design_d(S):
    px = blank(S)
    put_rrect(px, S, 0.03, 0.03, 0.97, 0.97, R_BIG,
              (116, 190, 246), 1.0, grad_to=(44, 106, 208))
    # 顶部一层淡淡的柔光
    put_rrect(px, S, 0.10, 0.075, 0.90, 0.30, 0.11, (255, 255, 255), 0.10)

    put_line(px, S, 0.225, 0.375, 0.615, 0.375, 0.072, (255, 255, 255), 0.97)
    put_line(px, S, 0.225, 0.535, 0.780, 0.535, 0.072, (255, 255, 255), 0.97)
    put_line(px, S, 0.225, 0.695, 0.520, 0.695, 0.072, (255, 255, 255), 0.62)

    # 右下角在线点（白圈 + 绿心）
    put_circle(px, S, 0.755, 0.725, 0.105, (255, 255, 255), 0.95)
    put_circle(px, S, 0.755, 0.725, 0.072, (56, 205, 122), 1.0)
    return px


# ---------------- E: 心跳波形（实时） ----------------
def design_e(S):
    px = blank(S)
    put_rrect(px, S, 0.03, 0.03, 0.97, 0.97, R_BIG,
              (132, 215, 246), 1.0, grad_to=(56, 130, 224))

    wave = [(0.150, 0.520), (0.330, 0.520), (0.418, 0.248),
            (0.528, 0.780), (0.612, 0.520), (0.850, 0.520)]

    if S <= 24:
        # 极小尺寸：简化为两段，避免糊成一团
        put_polyline(px, S, [(0.140, 0.520), (0.400, 0.520), (0.520, 0.250),
                             (0.640, 0.780), (0.860, 0.520)], 0.10, (255, 255, 255), 1.0)
    else:
        put_polyline(px, S, wave, 0.062, (255, 255, 255), 0.97)
    return px


# ---------------- F: 极简浅色 + 深蓝线条 ----------------
def design_f(S):
    px = blank(S)
    put_rrect(px, S, 0.03, 0.03, 0.97, 0.97, R_BIG,
              (255, 255, 255), 1.0, grad_to=(224, 236, 252))
    put_ring_rrect(px, S, 0.045, 0.045, 0.955, 0.955, R_BIG - 0.01, 0.016,
                   (196, 214, 238), 0.95)

    put_line(px, S, 0.225, 0.385, 0.590, 0.385, 0.070, (62, 116, 196), 0.98)
    put_line(px, S, 0.225, 0.545, 0.775, 0.545, 0.070, (62, 116, 196), 0.98)
    put_line(px, S, 0.225, 0.705, 0.500, 0.705, 0.070, (62, 116, 196), 0.55)

    put_circle(px, S, 0.755, 0.305, 0.078, (255, 255, 255), 1.0)
    put_circle(px, S, 0.755, 0.305, 0.058, (48, 196, 114), 1.0)
    return px


DESIGNS = [("D", design_d), ("E", design_e), ("F", design_f)]


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
    W, H = 1080, 470
    buf = [[[238.0, 241.0, 246.0] for _ in range(W)] for _ in range(H)]

    def paste(src, s, left, top):
        for y in range(s):
            for x in range(s):
                p = src[y][x]
                a = p[3] / 255.0
                if a <= 0:
                    continue
                ty, tx = top + y, left + x
                if 0 <= ty < H and 0 <= tx < W:
                    d = buf[ty][tx]
                    for i in range(3):
                        d[i] = p[i] * a + d[i] * (1 - a)

    col = 0
    for name, func in specs:
        base_x = 20 + col * 352
        paste(func(256), 256, base_x + 48, 30)
        small_x = base_x + 48
        for s in (64, 32, 16):
            paste(func(s), s, small_x, 320)
            small_x += s + 22
        col += 1

    for i in range(1, 3):
        x = 20 + i * 352 - 16
        for y in range(20, 440):
            buf[y][x] = [214.0, 219.0, 227.0]

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
    for name, func in DESIGNS:
        blob = ico_bytes([16, 24, 32, 48, 64, 128, 256], func)
        p = os.path.join(ROOT, "icons", "icon_%s.ico" % name)
        open(p, "wb").write(blob)
        print("%s -> %s (%d bytes)" % (name, p, len(blob)))

    composite(DESIGNS, os.path.join(ROOT, "icons", "compare2.png"))
