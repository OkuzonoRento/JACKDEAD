"""スクラップのモデル（JackDead-Scrap.blend）に UV とテクスチャを付ける。元の .blend は保存しない。

実行:
  blender --factory-startup --disable-autoexec -b <JackDead-Scrap.blend> --python Docs/ScrapModels/build_scrap.py -- Docs/ScrapModels

出力: textures/<名前>.png、JackDead-Scrap_textured.blend（モディファイアを適用し、テクスチャを貼ったもの。大きさは元のまま）、preview.png
テクスチャは、面の上の各画素の「立体の位置」から色を決めて描いている（部品や画面の位置は、元のモデルの座標）。
"""
import bpy, bmesh, sys, os, math
import numpy as np
from mathutils import Vector

sys.dont_write_bytecode = True
out_dir = os.path.abspath(sys.argv[sys.argv.index("--") + 1])
tex_dir = os.path.join(out_dir, "textures")
os.makedirs(tex_dir, exist_ok=True)
SIZE = 256
NAMES = {"battery": "Battery", "BigGear": "BigGear", "chain": "Chain", "flashlight": "Flashlight", "game": "GameConsole",
         "Gear": "Gear", "iron plate": "IronPlate", "phone": "Phone", "propeller": "Propeller", "Screw": "Screw",
         "stun gun": "StunGun"}


# ───────── 色と模様の道具（画素の位置の配列をまとめて処理する） ─────────
def rgb(r, g, b):
    return np.array([r, g, b], dtype=np.float32) / 255.0


def _hash(ix, iy, iz, seed):
    h = (ix * 374761393 + iy * 668265263 + iz * 1274126177 + seed * 1442695041) & 0xFFFFFFFF
    h = ((h ^ (h >> 13)) * 1103515245) & 0xFFFFFFFF
    return ((h ^ (h >> 16)) & 0xFFFF).astype(np.float32) / 65535.0


def noise(p, scale, seed=0):
    """3次元の値ノイズ（0〜1）"""
    q = p * scale
    i = np.floor(q).astype(np.int64)
    f = (q - i).astype(np.float32)
    f = f * f * (3 - 2 * f)
    out = 0.0
    for dx in (0, 1):
        for dy in (0, 1):
            for dz in (0, 1):
                w = (f[:, 0] if dx else 1 - f[:, 0]) * (f[:, 1] if dy else 1 - f[:, 1]) * (f[:, 2] if dz else 1 - f[:, 2])
                out = out + w * _hash(i[:, 0] + dx, i[:, 1] + dy, i[:, 2] + dz, seed)
    return out


def fbm(p, scale, seed=0):
    return noise(p, scale, seed) * 0.6 + noise(p, scale * 2.3, seed + 1) * 0.28 + noise(p, scale * 5.1, seed + 2) * 0.12


def mix(a, b, t):
    t = np.clip(np.asarray(t, dtype=np.float32), 0, 1)
    if t.ndim == 1:
        t = t[:, None]
    return a * (1 - t) + b * t


class Px:
    """1つの三角形の画素。p: 位置、n: 面の向き、part: かたまりの番号、face: 面の番号、edge: 角までの距離、s: 物の大きさ"""

    def __init__(self, p, n, part, face, edge, s):
        self.p, self.n, self.part, self.face, self.edge, self.s = p, n, part, face, edge, s
        self.x, self.y, self.z = p[:, 0], p[:, 1], p[:, 2]
        self.q = p / s  # 大きさで割った位置（模様の細かさを、物の大きさに合わせる）

    def base(self, color, vary=0.10, seed=1):
        c = np.tile(color, (len(self.p), 1))
        return c * (1 + (fbm(self.q, 9, seed) - 0.5)[:, None] * vary * 2) * (1 + (noise(self.q, 70, seed + 5) - 0.5)[:, None] * vary)

    def rust(self, c, amount, seed=11, color=None, scale=5.0):
        color = rgb(140, 70, 32) if color is None else color
        r = fbm(self.q, scale, seed)
        t = np.clip((r - (1 - amount)) * 6, 0, 1)
        speck = noise(self.q, 45, seed + 3)
        tone = mix(color * 0.65, color * 1.2, speck)
        return mix(c, tone, t * 0.92)

    def grime(self, c, strength=0.45, seed=31):
        g = np.clip((fbm(self.q, 4, seed) - 0.5) * 3, 0, 1)
        return mix(c, c * 0.45, g * strength)

    def wear(self, c, color, width=0.012, strength=0.8, seed=41):
        """角に沿った、こすれて下地が見える所"""
        t = np.clip(1 - self.edge / (width * self.s), 0, 1) * np.clip((noise(self.q, 28, seed) - 0.3) * 2.2, 0, 1)
        return mix(c, color, t * strength)

    def scratches(self, c, color, density=0.025, seed=51):
        a = noise(np.stack([self.q[:, 0] * 6 + self.q[:, 1] * 3, self.q[:, 1] * 90 - self.q[:, 0] * 40, self.q[:, 2] * 60], 1), 1.0, seed)
        return mix(c, color, (a > 1 - density * 4) * 0.55)

    def where(self, c, mask, other):
        return np.where(mask[:, None], other, c)


STEEL = rgb(122, 124, 126)
DARK_STEEL = rgb(68, 68, 72)
BLACK = rgb(22, 22, 24)


# ───────── 物ごとの色の決まり ─────────
def paint_battery(k):
    c = k.base(rgb(38, 66, 78))
    c = k.where(c, k.z > 0.297, k.base(rgb(46, 46, 50), 0.08, 3))
    front = (np.abs(k.x) < 0.15) & (k.z > 0.065) & (k.z < 0.235)
    if np.abs(k.n[1]) > 0.8:
        label = k.base(rgb(196, 158, 38), 0.08, 7)
        stripe = (((k.x + k.z) / 0.022).astype(np.int64) % 2 == 0) & (k.z > 0.207)
        label = k.where(label, stripe, np.tile(BLACK, (len(k.x), 1)))
        for cx, plus in ((-0.085, True), (0.085, False)):
            bar = (np.abs(k.x - cx) < 0.028) & (np.abs(k.z - 0.12) < 0.007)
            if plus:
                bar |= (np.abs(k.x - cx) < 0.007) & (np.abs(k.z - 0.12) < 0.028)
            label = k.where(label, bar, np.tile(BLACK, (len(k.x), 1)))
        text = (np.abs(k.z - 0.175) < 0.009) & (np.abs(k.x) < 0.11) & (noise(k.p, 130, 9) > 0.45)
        label = k.where(label, text, np.tile(rgb(60, 40, 20), (len(k.x), 1)))
        c = k.where(c, front, label)
    if k.part in (1, 2):
        c = k.base(rgb(160, 38, 30) if k.x.mean() < 0 else rgb(30, 30, 34), 0.08, 13)
    if k.part in (3, 4):
        c = k.base(rgb(150, 150, 142))
        c = k.rust(c, 0.42, 17, rgb(150, 176, 150), 14)   # 白緑のふき出し
    c = k.grime(c, 0.5)
    c = k.scratches(c, rgb(150, 160, 160), 0.018)
    return k.wear(c, rgb(130, 140, 142), 0.014, 0.6)


def paint_phone(k):
    c = k.base(rgb(132, 134, 138), 0.06)                       # 側面（金属）
    front = k.z > 0.0085
    back = k.z < -0.0145
    if k.n[2] < -0.8 or k.z.mean() < -0.012:
        body = k.base(rgb(188, 182, 170), 0.07, 3)
        lens = np.hypot(k.x - 0.11, k.y - 0.25) < 0.028
        ring = np.hypot(k.x - 0.11, k.y - 0.25) < 0.036
        body = k.where(body, ring, np.tile(DARK_STEEL, (len(k.x), 1)))
        body = k.where(body, lens, np.tile(rgb(12, 14, 26), (len(k.x), 1)))
        logo = np.abs(np.hypot(k.x, k.y - 0.03) - 0.03) < 0.006
        body = k.where(body, logo, np.tile(rgb(110, 106, 98), (len(k.x), 1)))
        c = k.where(c, k.z < -0.0085, body)
    glass = np.tile(rgb(14, 14, 18), (len(k.x), 1))
    screen = (np.abs(k.x) < 0.1535) & (k.y > -0.2575) & (k.y < 0.2855)
    shine = np.clip(1 - np.abs((k.x * 1.3 + k.y - 0.08)) / 0.07, 0, 1)
    lit = mix(k.base(rgb(26, 34, 52), 0.06, 5), rgb(74, 90, 118), shine * 0.55)
    glass = k.where(glass, screen, lit)
    origin = np.array([0.07, -0.13])
    dx, dy = k.x - origin[0], k.y - origin[1]
    angle, radius = np.arctan2(dy, dx), np.hypot(dx, dy)
    crack = np.zeros(len(k.x), dtype=bool)
    for a, ln in ((0.5, 0.2), (1.9, 0.36), (2.6, 0.24), (-2.2, 0.16), (-0.7, 0.13), (3.05, 0.2)):
        d = np.abs(np.angle(np.exp(1j * (angle - a - (noise(k.p, 45, 8) - 0.5) * 0.35))))
        crack |= (d * radius < 0.0022) & (radius < ln)
    crack |= (np.abs(radius - 0.035) < 0.0018) & (noise(k.p, 60, 2) > 0.45)
    glass = k.where(glass, crack & screen, np.tile(rgb(190, 205, 215), (len(k.x), 1)))
    button = np.abs(np.hypot(k.x, k.y + 0.2825) - 0.014) < 0.004
    glass = k.where(glass, button, np.tile(rgb(90, 90, 96), (len(k.x), 1)))
    c = k.where(c, front, glass)
    c = k.grime(c, 0.35)
    return k.wear(c, rgb(200, 200, 200), 0.008, 0.4)


def paint_game(k):
    shell = k.base(rgb(122, 30, 38), 0.08)
    c = shell
    if k.part == 1 and k.face == 101:
        c = k.base(rgb(30, 44, 40), 0.06, 3)
    if k.part == 10 and k.face == 700:
        c = k.base(rgb(26, 32, 46), 0.06, 4)
    if k.face in (101, 700):
        shine = np.clip(1 - np.abs((k.x * 0.8 + k.y + k.z * 0.6 - (0.9 if k.face == 101 else 2.4))) / 0.2, 0, 1)
        c = mix(c, rgb(96, 118, 130), shine * 0.35)
        c = mix(c, c * 0.65, (((k.y + k.z) / 0.022).astype(np.int64) % 2 == 0) * 0.5)
        if k.face == 700:
            d = np.abs((k.x - 0.35) * 0.9 - (k.z - 0.55) + (noise(k.p, 14, 6) - 0.5) * 0.18)
            c = k.where(c, (d < 0.008) & (k.x > -0.1), np.tile(rgb(185, 205, 215), (len(k.x), 1)))
    buttons = {2: rgb(200, 48, 40), 3: rgb(210, 175, 40), 4: rgb(52, 140, 70), 5: rgb(50, 96, 180),
               6: rgb(70, 70, 76), 7: rgb(70, 70, 76), 9: rgb(60, 170, 90), 0: rgb(34, 34, 38), 8: rgb(52, 52, 58)}
    if k.part in buttons:
        c = k.base(buttons[k.part], 0.07, 20 + k.part)
    c = k.grime(c, 0.45)
    c = k.scratches(c, rgb(190, 150, 150), 0.012)
    return k.wear(c, rgb(205, 180, 172), 0.007, 0.55)


def paint_flashlight(k):
    radius = np.hypot(k.x, k.y)
    c = k.base(rgb(172, 132, 34), 0.09)                          # 胴（黄色の塗装）
    grip = (k.z > 0.55) & (k.z < 1.15) & (((k.z / 0.075).astype(np.int64)) % 2 == 0)
    c = k.where(c, grip, k.base(rgb(34, 32, 30), 0.1, 4))
    c = k.where(c, (k.z > 1.40) & (k.z < 1.47), np.tile(BLACK, (len(k.x), 1)))
    c = k.where(c, k.z < 0.19, k.base(rgb(30, 30, 32), 0.1, 5))   # 底のゴム
    c = k.where(c, k.z > 1.645, k.base(rgb(58, 58, 64), 0.08, 6))  # 首と頭
    if k.face == 28:
        lens = mix(rgb(236, 226, 170), rgb(96, 110, 104), np.clip(radius / 0.40, 0, 1))
        lens = np.where((np.abs(radius - 0.19) < 0.014)[:, None], rgb(190, 190, 160), lens)
        lens = np.where((radius < 0.06)[:, None], rgb(255, 250, 215), lens)
        crack = np.abs(k.y - k.x * 0.45 - 0.1 + (noise(k.p, 11, 3) - 0.5) * 0.12) < 0.008
        c = np.where(crack[:, None], rgb(205, 215, 205), lens)
        return c * (1 + (noise(k.q, 60, 8) - 0.5)[:, None] * 0.12)
    inner = (k.z > 2.2) & (radius < 0.425) & (k.x * k.n[0] + k.y * k.n[1] < 0)   # 筒の内側（反射板）
    c = k.where(c, inner, k.base(rgb(160, 162, 158), 0.08, 9))
    c = k.rust(c, 0.30, 13)
    c = k.grime(c, 0.5)
    c = k.scratches(c, rgb(190, 190, 180), 0.02)
    return k.wear(c, STEEL, 0.008, 0.75)


def paint_stungun(k):
    c = k.base(rgb(30, 30, 34), 0.12)
    if abs(k.n[0]) > 0.8:
        stripe = (k.z > 0.99) & (k.z < 1.16) & (np.abs(k.y) < 0.23)
        diagonal = (((k.y + k.z) / 0.075).astype(np.int64)) % 2 == 0
        c = k.where(c, stripe, k.base(rgb(214, 170, 30), 0.08, 3))
        c = k.where(c, stripe & diagonal, np.tile(BLACK, (len(k.x), 1)))
        bolt = (np.abs(k.y - (k.z - 0.8) * 0.55 + np.where(k.z > 0.8, 0.035, -0.035)) < 0.026) & (np.abs(k.z - 0.8) < 0.12)
        c = k.where(c, bolt, np.tile(rgb(230, 190, 40), (len(k.x), 1)))
    c = k.where(c, (k.z < 0.62) & (k.z > 0.16) & (((k.z / 0.062).astype(np.int64)) % 2 == 0), k.base(rgb(58, 58, 62), 0.1, 4))
    button = (k.n[1] < -0.7) & (k.z > 0.70) & (k.z < 0.92) & (np.abs(k.x) < 0.11)
    c = k.where(c, button, k.base(rgb(180, 34, 28), 0.08, 5))
    if k.part == 2:
        c = k.rust(k.base(rgb(180, 120, 52), 0.1, 6), 0.3, 8, rgb(60, 130, 110))
    if k.part in (0, 3, 4, 5):
        c = k.base(rgb(188, 192, 196), 0.1, 7)
    c = k.grime(c, 0.35)
    c = k.scratches(c, rgb(140, 140, 146), 0.016)
    return k.wear(c, rgb(120, 118, 116), 0.006, 0.6)


def paint_gear(k):
    c = k.base(rgb(96, 88, 80), 0.16)
    c = k.rust(c, 0.52, 11)
    c = k.rust(c, 0.30, 23, rgb(84, 40, 22), 9)
    c = k.where(c, np.hypot(k.x, k.y) < 0.22, c * 0.45)
    c = k.grime(c, 0.55)
    return k.wear(c, rgb(176, 170, 160), 0.016, 0.85)


def paint_biggear(k):
    if k.part == 1:
        c = k.base(rgb(168, 128, 54), 0.14, 3)                   # くすんだ真ちゅう
        c = k.rust(c, 0.35, 29, rgb(70, 104, 80))
    else:
        c = paint_gear_body(k)
        hub = (np.hypot(k.x, k.y) < 0.36) & (k.z > 0.14)
        c = k.where(c, hub, k.rust(k.base(DARK_STEEL, 0.1, 5), 0.25, 15))
    c = k.grime(c, 0.5)
    return k.wear(c, rgb(190, 178, 150), 0.014, 0.85)


def paint_gear_body(k):
    c = k.base(rgb(98, 94, 90), 0.16)
    c = k.rust(c, 0.50, 11)
    return k.rust(c, 0.28, 23, rgb(84, 40, 22), 9)


def paint_chain(k):
    tone = 0.85 + 0.3 * ((k.part * 37) % 7) / 6.0
    c = k.base(STEEL * 0.8 * tone, 0.14)
    c = k.rust(c, 0.44 + 0.05 * (k.part % 3), 11 + k.part, None, 7)
    c = k.grime(c, 0.55)
    return k.wear(c, rgb(180, 180, 182), 0.006, 0.55)


def paint_plate(k):
    c = k.base(rgb(104, 104, 106), 0.12)
    u, v = (k.x + k.y) / 0.34, (k.x - k.y) / 0.34
    iu, iv = np.floor(u).astype(np.int64), np.floor(v).astype(np.int64)
    fu, fv = u - iu - 0.5, v - iv - 0.5
    flip = (iu + iv) % 2 == 0
    bar = (np.abs(np.where(flip, fu, fv)) < 0.34) & (np.abs(np.where(flip, fv, fu)) < 0.09)
    top = (k.n[2] > 0.8) & (np.abs(k.x) < 0.92) & (np.abs(k.y) < 1.02)
    c = k.where(c, bar & top, c * 1.35)
    shadow = (np.abs(np.where(flip, fu, fv)) < 0.34) & (np.abs(np.where(flip, fv, fu) - 0.13) < 0.04)
    c = k.where(c, shadow & top, c * 0.6)
    c = k.rust(c, 0.46, 11, None, 4)
    for sx in (-0.86, 0.86):
        for sy in (-0.96, 0.96):
            d = np.hypot(k.x - sx, k.y - sy)
            c = k.where(c, d < 0.085, c * 0.45)
            c = k.where(c, d < 0.058, k.base(rgb(150, 140, 128), 0.1, 9))
    c = k.grime(c, 0.55)
    return k.wear(c, rgb(178, 176, 170), 0.016, 0.8)


def paint_propeller(k):
    radius = np.hypot(k.x, k.y)
    if k.part in (3, 7):
        c = k.rust(k.base(DARK_STEEL, 0.12), 0.45, 11)
    else:
        c = k.base(rgb(54, 112, 108), 0.10)                       # 色あせた青緑の塗装
        band = (radius > 2.05) & (radius < 2.3)
        c = k.where(c, band, k.base(rgb(188, 150, 44), 0.08, 4))
        chip = np.clip((fbm(k.q, 11, 17) - 0.62) * 9, 0, 1)
        c = mix(c, k.base(STEEL, 0.15, 19), chip)
        c = k.rust(c, 0.30, 21, None, 6)
    c = k.grime(c, 0.5)
    c = k.scratches(c, rgb(180, 190, 190), 0.015)
    return k.wear(c, rgb(170, 172, 170), 0.012, 0.85)


def paint_screw(k):
    c = k.base(rgb(134, 134, 128), 0.12)
    shaft = (k.y > 0.15) & (np.hypot(k.x, k.z) < 0.8)
    turn = np.arctan2(k.z, k.x) / (2 * math.pi)
    phase = (k.y / 0.2 + turn) % 1.0
    c = k.where(c, shaft & (phase < 0.28), c * 0.45)
    c = k.where(c, shaft & (phase > 0.28) & (phase < 0.42), np.clip(c * 1.3, 0, 1))
    c = k.rust(c, 0.42, 11)
    c = k.grime(c, 0.45)
    return k.wear(c, rgb(190, 188, 178), 0.014, 0.8)


PAINTERS = {"battery": paint_battery, "phone": paint_phone, "game": paint_game, "flashlight": paint_flashlight,
            "stun gun": paint_stungun, "Gear": paint_gear, "BigGear": paint_biggear, "chain": paint_chain,
            "iron plate": paint_plate, "propeller": paint_propeller, "Screw": paint_screw}


# ───────── メッシュの準備と描き込み ─────────
def loose_parts(me):
    """面ごとの、つながったかたまりの番号"""
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    part = [-1] * len(bm.faces)
    sharp = [[] for _ in bm.faces]
    count = 0
    for f in bm.faces:
        if part[f.index] >= 0:
            continue
        stack = [f]
        part[f.index] = count
        while stack:
            g = stack.pop()
            for e in g.edges:
                for h in e.link_faces:
                    if part[h.index] < 0:
                        part[h.index] = count
                        stack.append(h)
        count += 1
    for e in bm.edges:
        if len(e.link_faces) == 2 and e.calc_face_angle(0) < math.radians(35):
            continue
        a, b = e.verts[0].co.copy(), e.verts[1].co.copy()
        for f in e.link_faces:
            sharp[f.index].append((np.array(a, dtype=np.float32), np.array(b, dtype=np.float32)))
    bm.free()
    return part, sharp


def bake(name, me):
    part, sharp = loose_parts(me)
    size = max(max(v.co[i] for v in me.vertices) - min(v.co[i] for v in me.vertices) for i in range(3))
    img = np.zeros((SIZE, SIZE, 3), dtype=np.float32)
    filled = np.zeros((SIZE, SIZE), dtype=bool)
    strict = np.zeros((SIZE, SIZE), dtype=bool)
    uv = me.uv_layers.active.data
    me.calc_loop_triangles()
    painter = PAINTERS[name]
    for tri in me.loop_triangles:
        t = np.array([uv[i].uv for i in tri.loops], dtype=np.float64) * SIZE
        p = np.array([me.vertices[i].co for i in tri.vertices], dtype=np.float32)
        lo = np.maximum(np.floor(t.min(0) - 1).astype(int), 0)
        hi = np.minimum(np.ceil(t.max(0) + 1).astype(int), SIZE - 1)
        if (hi < lo).any():
            continue
        xs, ys = np.meshgrid(np.arange(lo[0], hi[0] + 1), np.arange(lo[1], hi[1] + 1))
        px = np.stack([xs.ravel() + 0.5, ys.ravel() + 0.5], 1)
        d = (t[1, 1] - t[2, 1]) * (t[0, 0] - t[2, 0]) + (t[2, 0] - t[1, 0]) * (t[0, 1] - t[2, 1])
        if abs(d) < 1e-9:
            continue
        b0 = ((t[1, 1] - t[2, 1]) * (px[:, 0] - t[2, 0]) + (t[2, 0] - t[1, 0]) * (px[:, 1] - t[2, 1])) / d
        b1 = ((t[2, 1] - t[0, 1]) * (px[:, 0] - t[2, 0]) + (t[0, 0] - t[2, 0]) * (px[:, 1] - t[2, 1])) / d
        b = np.stack([b0, b1, 1 - b0 - b1], 1)
        # 三角形の中（にじみ止めに、少し外側も含める）
        area = abs(d)
        heights = np.array([area / max(np.linalg.norm(t[(i + 1) % 3] - t[(i + 2) % 3]), 1e-6) for i in range(3)])
        dist = (b * heights).min(1)
        sel = dist > -0.9
        if not sel.any():
            continue
        ix, iy = xs.ravel()[sel], ys.ravel()[sel]
        inside = dist[sel] >= -1e-6
        keep = inside | ~strict[iy, ix]
        if not keep.any():
            continue
        bb = np.clip(b[sel][keep], 0, 1)
        bb /= bb.sum(1, keepdims=True)
        pos = (bb @ p).astype(np.float32)
        poly = tri.polygon_index
        edge = np.full(len(pos), 1e9, dtype=np.float32)
        for a, c in sharp[poly]:
            ac = c - a
            h = np.clip(((pos - a) @ ac) / max(float(ac @ ac), 1e-12), 0, 1)
            edge = np.minimum(edge, np.linalg.norm(pos - a - h[:, None] * ac, axis=1))
        k = Px(pos, np.array(me.polygons[poly].normal, dtype=np.float32), part[poly], poly, edge, size)
        color = np.clip(painter(k), 0, 1)
        img[iy[keep], ix[keep]] = color
        filled[iy[keep], ix[keep]] = True
        strict[iy[keep], ix[keep]] |= inside[keep]
    for _ in range(6):                                          # すき間を、周りの色で埋める
        for axis, shift in ((0, 1), (0, -1), (1, 1), (1, -1)):
            src = np.roll(filled, shift, axis)
            fill = ~filled & src
            img[fill] = np.roll(img, shift, axis)[fill]
            filled |= fill
    img[~filled] = img[filled].mean(0)
    return img


def save_png(path, arr):
    h, w = arr.shape[:2]
    im = bpy.data.images.new(os.path.splitext(os.path.basename(path))[0], w, h, alpha=False)
    rgba = np.ones((h, w, 4), dtype=np.float32)
    rgba[..., :3] = arr
    im.pixels.foreach_set(rgba.ravel())
    im.filepath_raw = path
    im.file_format = "PNG"
    im.save()
    return im


# ───────── 本体 ─────────
originals = [o for o in bpy.data.objects if o.type == "MESH" and o.name in NAMES]
missing = set(NAMES) - {o.name for o in originals}
if missing:
    print("SCRAP missing", missing)
for o in originals:
    o.hide_viewport = False
    o.hide_set(False)
meshes = {}
for o in originals:
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(o.evaluated_get(dg))
    me.transform(o.matrix_world)
    if o.matrix_world.determinant() < 0:
        me.flip_normals()
    me.update()
    meshes[o.name] = me
for o in list(bpy.data.objects):
    bpy.data.objects.remove(o)

scene = bpy.context.scene
results = []
for name in sorted(NAMES, key=str.lower):
    if name not in meshes:
        continue
    nice = NAMES[name]
    me = meshes[name]
    me.name = nice
    ob = bpy.data.objects.new(nice, me)
    scene.collection.objects.link(ob)
    while me.uv_layers:
        me.uv_layers.remove(me.uv_layers[0])
    me.uv_layers.new(name="UVMap")
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(50), island_margin=0.015)
    bpy.ops.object.mode_set(mode="OBJECT")
    me = ob.data
    image = save_png(os.path.join(tex_dir, nice + ".png"), bake(name, me))
    mat = bpy.data.materials.new("Scrap_" + nice)
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    tex = nodes.new("ShaderNodeTexImage")
    tex.image = image
    tex.interpolation = "Closest"
    bsdf = next(n for n in nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Roughness"].default_value = 0.8
    mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    nodes.active = tex
    me.materials.clear()
    me.materials.append(mat)
    for poly in me.polygons:
        poly.material_index = 0
    results.append(ob)
    ext = [max(v.co[i] for v in me.vertices) - min(v.co[i] for v in me.vertices) for i in range(3)]
    print(f"SCRAP {nice:12s} tris={sum(len(p.vertices) - 2 for p in me.polygons)} size={[round(e, 3) for e in ext]}")

for im in bpy.data.images:
    if im.filepath_raw.startswith(tex_dir):
        im.pack()
# 横に並べて保存する（大きさは元のまま）
x = 0.0
for ob in results:
    xs = [v.co.x for v in ob.data.vertices]
    ob.location = (x - min(xs), 0, 0)
    x += max(xs) - min(xs) + 0.6
bpy.context.preferences.filepaths.save_version = 0
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out_dir, "JackDead-Scrap_textured.blend"), copy=True)

# ───────── 確認画像 ─────────
scene.render.engine = "BLENDER_WORKBENCH"
sh = scene.display.shading
sh.light = "STUDIO"
sh.color_type = "TEXTURE"
sh.show_specular_highlight = False
sh.show_cavity = True
world = scene.world or bpy.data.worlds.new("w")
scene.world = world
world.color = (0.05, 0.05, 0.06)
scene.view_settings.view_transform = "Standard"
S = 360
scene.render.resolution_x = scene.render.resolution_y = S
scene.render.image_settings.file_format = "PNG"
cam = bpy.data.cameras.new("pv_cam")
cam.type = "ORTHO"
cob = bpy.data.objects.new("pv_cam", cam)
scene.collection.objects.link(cob)
scene.camera = cob
VIEW = {"Phone": (0.35, -0.6, 1.0), "IronPlate": (0.5, -0.8, 1.0), "Propeller": (0.4, -0.6, 1.0), "Chain": (0.25, -1.0, 0.7),
        "GameConsole": (0.45, -1.0, 0.85), "Screw": (0.9, -0.8, 0.7), "Flashlight": (0.7, -1.0, 0.8), "StunGun": (1.0, -0.75, 0.55),
        "Battery": (0.55, -1.0, 0.7)}
tmp = os.path.join(out_dir, "_shot.png")
tiles = []
for ob in results:
    for other in results:
        other.hide_render = other is not ob
    bpy.context.view_layer.update()
    pts = [ob.matrix_world @ v.co for v in ob.data.vertices]
    lo = Vector([min(p[i] for p in pts) for i in range(3)])
    hi = Vector([max(p[i] for p in pts) for i in range(3)])
    center = (lo + hi) / 2
    r = max((p - center).length for p in pts)
    d = Vector(VIEW.get(ob.name, (0.7, -1.0, 0.75))).normalized()
    cob.location = center + d * r * 4
    cob.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    cam.ortho_scale = r * 2.05
    cam.clip_end = r * 20
    scene.render.filepath = tmp
    bpy.ops.render.render(write_still=True)
    im = bpy.data.images.load(tmp)
    a = np.empty(S * S * 4, dtype=np.float32)
    im.pixels.foreach_get(a)
    tiles.append(a.reshape(S, S, 4).copy())
    bpy.data.images.remove(im)
os.remove(tmp)
cols, g = 4, 4
rows = math.ceil(len(tiles) / cols)
sheet = np.zeros((rows * (S + g), cols * (S + g), 4), dtype=np.float32)
sheet[..., 3] = 1
sheet[..., :3] = (0.12, 0.12, 0.13)
for i, t in enumerate(tiles):
    y = (rows - 1 - i // cols) * (S + g)
    sheet[y:y + S, (i % cols) * (S + g):(i % cols) * (S + g) + S] = t
img = bpy.data.images.new("pv_sheet", sheet.shape[1], sheet.shape[0], alpha=True)
img.pixels.foreach_set(sheet.ravel())
img.filepath_raw = os.path.join(out_dir, "preview.png")
img.file_format = "PNG"
img.save()
print("SCRAP done", [o.name for o in results])
