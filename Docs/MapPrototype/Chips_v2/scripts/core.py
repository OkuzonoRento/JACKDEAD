"""ローポリの旅館マップチップ v2（テクスチャ・ドア枠・天井つき）。
作る単位は m。Blender の 1 単位 = ゲーム内の 10m（プレハブが 10 倍で配置される）。部品の大きさは元と同じ 20m 四方。
向きの対応（Blender → ゲーム）: +X=左、-X=右、-Y=上、+Y=下
引数: 出力フォルダ、元の FBX があるフォルダ、このスクリプトの置き場所"""
import bpy, math, random, sys, os
from mathutils import Vector, Euler
from mathutils.bvhtree import BVHTree
from io_scene_fbx import parse_fbx

out_dir, fbx_dir, script_dir = sys.argv[sys.argv.index("--") + 1:]
sys.path.insert(0, script_dir)
import textures

os.makedirs(os.path.join(out_dir, "fbx"), exist_ok=True)
S = 0.1
C = 20.0 / 3.0          # 3x3 のマスの 1 辺
TS = C / 4              # 床の穴を決める細かいマス
H = 6.0                 # 天井の高さ
T = 0.2                 # 壁の厚み
DOOR_W, DOOR_H = 4.0, 3.4
scene = bpy.context.scene
originals = list(scene.objects)
MATS = textures.build(os.path.join(out_dir, "textures"))
MAT_ORDER = list(MATS)


def cells_of(text):
    return {tuple(int(v) for v in c.split(",")) for c in text.split()}


ALL = {(x, y) for x in (-1, 0, 1) for y in (-1, 0, 1)}
CHIPS = {
    "+":  (cells_of("0,0 1,0 -1,0 0,1 0,-1"), [(1, 0), (-1, 0), (0, 1), (0, -1)]),
    "-":  (cells_of("-1,0 0,0 1,0"), [(1, 0), (-1, 0)]),
    "-O": (cells_of("-1,0 0,-1 0,0 0,1 1,-1 1,0 1,1"), [(-1, 0)]),
    "=":  (cells_of("-1,0 0,0 1,0 -1,1 0,1 1,1"), [(0, 1), (1, 0), (-1, 0)]),
    "_":  (cells_of("0,0 1,0 0,-1"), [(0, -1), (1, 0)]),
    "f":  (cells_of("0,-1 0,0 0,1 1,0"), [(0, -1), (0, 1), (1, 0)]),
    "G":  (set(ALL), [(1, 0), (-1, 0), (0, 1), (0, -1)]),
    "O":  (ALL - {(0, 0)}, [(1, 0), (-1, 0), (0, 1), (0, -1)]),
}
FACE_NAMES = ["bottom", "top", "front", "right", "back", "left"]
BOX_FACES = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]


class Mesh:
    def __init__(self):
        self.verts, self.faces, self.uvs, self.mats = [], [], [], []

    def face(self, pts, uvs, mat="Props"):
        n = len(self.verts)
        self.verts += [Vector(p) * S for p in pts]
        self.faces.append([n + i for i in range(len(pts))])
        self.uvs.append(uvs)
        self.mats.append(MAT_ORDER.index(mat))

    def skin(self, pts, name):
        """家具用テクスチャの範囲 name を、四角形（左下・右下・右上・左上）や三角形に貼る"""
        u0, v0, u1, v1 = textures.region(name)
        if len(pts) == 4:
            uvs = [(u0, v0), (u1, v0), (u1, v1), (u0, v1)]
        else:
            uvs = [(u0 + (u1 - u0) * (0.5 + 0.5 * math.cos(2 * math.pi * i / len(pts))),
                    v0 + (v1 - v0) * (0.5 + 0.5 * math.sin(2 * math.pi * i / len(pts)))) for i in range(len(pts))]
        self.face(pts, uvs)

    def quad(self, pts, name, both=False):
        self.skin(pts, name)
        if both:
            self.skin([pts[1], pts[0], pts[3], pts[2]], name)

    def box(self, center, size, mat, rz=0.0, rx=0.0, ry=0.0, faces=None, skip=()):
        hx, hy, hz = (s / 2 for s in size)
        local = [(-hx, -hy, -hz), (hx, -hy, -hz), (hx, hy, -hz), (-hx, hy, -hz),
                 (-hx, -hy, hz), (hx, -hy, hz), (hx, hy, hz), (-hx, hy, hz)]
        rot = Euler((math.radians(rx), math.radians(ry), math.radians(rz))).to_matrix()
        pts = [rot @ Vector(v) + Vector(center) for v in local]
        keep = set(FACE_NAMES) - set(skip)
        # 見えない面は作らない：薄い板は表の面だけ、床に置いた物の底と天井に付く上の面は省く
        if size[2] <= 0.03:
            keep &= {"top"}
        elif size[0] <= 0.03:
            keep &= {"left", "right"}
        elif size[1] <= 0.03:
            keep &= {"front", "back"}
        if not rx and not ry and center[2] - hz <= 0.02:
            keep.discard("bottom")
        if center[2] + hz >= H - 0.01:
            keep.discard("top")
        for name, f in zip(FACE_NAMES, BOX_FACES):
            if name in keep:
                self.skin([pts[i] for i in f], (faces or {}).get(name, mat))

    def prism(self, center, r, h, mat, n=6, r_top=None):
        n = min(n, 6)
        r_top = r if r_top is None else r_top
        cx, cy, cz = center
        pts = []
        for rad, z in ((r, cz - h / 2), (r_top, cz + h / 2)):
            pts += [(cx + rad * math.cos(2 * math.pi * i / n), cy + rad * math.sin(2 * math.pi * i / n), z) for i in range(n)]
        for i in range(n):
            self.skin([pts[i], pts[(i + 1) % n], pts[n + (i + 1) % n], pts[n + i]], mat)
        if cz + h / 2 < H - 0.01 and r_top > 0.03:
            self.skin(pts[n:], mat)
        if cz - h / 2 > 0.02:
            self.skin(list(reversed(pts[:n])), mat)

    def lump(self, center, rx, ry, rz, mat):
        cx, cy, cz = center
        p = [(cx + rx, cy, cz), (cx, cy + ry, cz), (cx - rx, cy, cz), (cx, cy - ry, cz), (cx, cy, cz + rz), (cx, cy, cz - rz)]
        # 角の位置を少しずらして、石や瓦礫らしくする
        jit = random.Random(int(cx * 977 + cy * 131) % 100003)
        p = [(v[0] + jit.uniform(-0.25, 0.25) * rx, v[1] + jit.uniform(-0.25, 0.25) * ry, max(0.0, v[2] + (jit.uniform(-0.3, 0.2) * rz if i == 4 else 0))) for i, v in enumerate(p)]
        faces = [(0, 1, 4), (1, 2, 4), (2, 3, 4), (3, 0, 4)]
        if cz - rz > 0.1:
            faces += [(1, 0, 5), (2, 1, 5), (3, 2, 5), (0, 3, 5)]
        for f in faces:
            self.skin([p[i] for i in f], mat)

    def make(self, name):
        me = bpy.data.meshes.new(name)
        me.from_pydata(self.verts, [], self.faces)
        used = sorted(set(self.mats))
        for i in used:
            me.materials.append(MATS[MAT_ORDER[i]])
        layer = me.uv_layers.new(name="UVMap")
        for poly, mi, uvs in zip(me.polygons, self.mats, self.uvs):
            poly.material_index = used.index(mi)
            for li, uv in zip(poly.loop_indices, uvs):
                layer.data[li].uv = uv
        me.update()
        return me


def merge_tiles(tiles):
    """マス目の集まりを、なるべく大きい長方形にまとめる (i0, j0, i1, j1)"""
    left = set(tiles)
    out = []
    while left:
        i0, j0 = min(left, key=lambda t: (t[1], t[0]))
        i1 = i0
        while (i1 + 1, j0) in left:
            i1 += 1
        j1 = j0
        while all((i, j1 + 1) in left for i in range(i0, i1 + 1)):
            j1 += 1
        left -= {(i, j) for i in range(i0, i1 + 1) for j in range(j0, j1 + 1)}
        out.append((i0, j0, i1 + 1, j1 + 1))
    return out


class Room:
    def __init__(self, chip, floor="Plank", wall="Plaster", pits=(), inner="wall", seed=1):
        self.chip = chip
        self.cells, self.doors = CHIPS[chip]
        self.wall = wall
        self.inner = inner
        self.solid, self.deco = Mesh(), Mesh()   # 当たり判定あり／飾り
        self.rng = random.Random(seed)
        self.hides = []
        self.tiles, self.pit_tiles = set(), set()
        for i in range(12):
            for j in range(12):
                if (i // 4 - 1, j // 4 - 1) not in self.cells:
                    continue
                x, y = self.tile_center(i, j)
                if any(a < x < c and b < y < d for a, b, c, d in pits):
                    self.pit_tiles.add((i, j))
                else:
                    self.tiles.add((i, j))
        self.open_center = chip == "O" and inner != "wall"
        self.build_floor(floor)
        self.build_walls()
        self.build_ceiling()
        self.build_pits()

    # ------------------------------------------------------------ 土台
    @staticmethod
    def tile_center(i, j):
        return -10 + (i + 0.5) * TS, -10 + (j + 0.5) * TS

    def on_floor(self, x, y, margin=0.0):
        pts = [(x, y)] + [(x + a, y + b) for a in (-margin, margin) for b in (-margin, margin)]
        return all((int((px + 10) // TS), int((py + 10) // TS)) in self.tiles for px, py in pts)

    def build_floor(self, floor):
        for i0, j0, i1, j1 in merge_tiles(self.tiles):
            pts = [(-10 + i * TS, -10 + j * TS, 0) for i, j in ((i0, j0), (i1, j0), (i1, j1), (i0, j1))]
            self.solid.face(pts, [((p[0] + 10) / C, (p[1] + 10) / C) for p in pts], floor)

    def build_ceiling(self):
        tiles = self.tiles | self.pit_tiles
        if self.open_center:
            tiles |= {(i, j) for i in range(4, 8) for j in range(4, 8)}
        for i0, j0, i1, j1 in merge_tiles(tiles):
            # 下向きの面。下からは当たり、上からの床の判定は通り抜ける
            pts = [(-10 + i * TS, -10 + j * TS, H) for i, j in ((i0, j0), (i0, j1), (i1, j1), (i1, j0))]
            self.solid.face(pts, [((p[0] + 10) / C, (p[1] + 10) / C) for p in pts], "Ceiling")

    def wall_face(self, mid, normal, length, z0, z1):
        """mid を中心に、normal の向きを表にした壁の面"""
        rx, ry = -normal[1], normal[0]
        a = (mid[0] - rx * length / 2, mid[1] - ry * length / 2)
        b = (mid[0] + rx * length / 2, mid[1] + ry * length / 2)
        pts = [(a[0], a[1], z0), (b[0], b[1], z0), (b[0], b[1], z1), (a[0], a[1], z1)]
        self.solid.face(pts, [((p[0] * rx + p[1] * ry + 10) / C, p[2] / H) for p in pts], self.wall)

    def wall_span(self, edge, d, along, a, b, z0=0.0, z1=H, ext=(0.0, 0.0)):
        """境目 edge の上で、along 方向の a〜b を、厚みのある壁（内と外の面）にする"""
        m = (a + b) / 2
        ex, ey = edge[0] + along[0] * m, edge[1] + along[1] * m
        # 内側の面は、へこんだ角では厚みの分だけ伸ばして、隣の壁とつなげる（伸ばさないと、角に隙間ができて外が見える）
        shift = (ext[1] - ext[0]) / 2
        self.wall_face((ex - d[0] * T + along[0] * shift, ey - d[1] * T + along[1] * shift), (-d[0], -d[1]), b - a + ext[0] + ext[1], z0, z1)
        self.wall_face((ex, ey), d, b - a, z0, z1)

    def build_walls(self):
        self.wall_sides = []
        for cx, cy in sorted(self.cells):
            for d in [(1, 0), (-1, 0), (0, 1), (0, -1)]:
                dx, dy = d
                if (cx + dx, cy + dy) in self.cells:
                    continue
                is_inner = self.chip == "O" and (cx + dx, cy + dy) == (0, 0)
                outer = (dx != 0 and abs(cx + dx) == 2) or (dy != 0 and abs(cy + dy) == 2)
                middle = (cy == 0) if dx != 0 else (cx == 0)
                edge = (cx * C + dx * C / 2, cy * C + dy * C / 2)
                along = (1, 0) if dy != 0 else (0, 1)
                if outer and middle and d in self.doors:
                    self.door(edge, d, along)
                    continue
                if is_inner and self.inner != "wall":
                    if self.inner == "rail":
                        self.railing(edge[0] - dx * 0.1, edge[1] - dy * 0.1, along, broken=d in ((1, 0), (0, -1)))
                    continue
                # へこんだ角：この辺の端で、壁が手前（部屋の内側から見て出っ張る向き）へ折れる
                ext = tuple(T if (cx + s * along[0], cy + s * along[1]) in self.cells
                            and (cx + s * along[0] + dx, cy + s * along[1] + dy) in self.cells else 0.0 for s in (-1, 1))
                self.wall_span(edge, d, along, -C / 2, C / 2, ext=ext)
                self.wall_sides.append((edge[0] - dx * (T + 0.02), edge[1] - dy * (T + 0.02), dx, dy, along))

    def door(self, edge, d, along):
        """部屋の境目の、扉が付きそうな枠（開口 幅 4m・高さ 3.4m）"""
        w = DOOR_W / 2
        self.wall_span(edge, d, along, -C / 2, -w)
        self.wall_span(edge, d, along, w, C / 2)
        self.wall_span(edge, d, along, -w, w, DOOR_H, H)
        depth = 0.45

        def block(mesh, offset, z, length, height, thick, mat="DarkWood", **kw):
            x = edge[0] + along[0] * offset - d[0] * thick / 2
            y = edge[1] + along[1] * offset - d[1] * thick / 2
            size = (length, thick, height) if along[0] else (thick, length, height)
            mesh.box((x, y, z), size, mat, **kw)

        for s in (-1, 1):
            block(self.solid, s * (w + 0.15), DOOR_H / 2, 0.3, DOOR_H, depth)               # 柱
            block(self.deco, s * (w + 0.15), 0.18, 0.42, 0.36, depth + 0.06, "BlackLacquer")  # 根元の金具
        block(self.solid, 0, DOOR_H + 0.17, DOOR_W + 0.9, 0.34, depth + 0.04)                # 鴨居
        block(self.deco, 0, 0.012, DOOR_W, 0.014, depth)                                     # 敷居

    def railing(self, x, y, along, broken):
        spans = [(-C / 2, -1.2), (1.2, C / 2)] if broken else [(-C / 2, C / 2)]
        for a, b in spans:
            mid, ln = (a + b) / 2, b - a
            self.solid.box((x + along[0] * mid, y + along[1] * mid, 1.12),
                           (ln, 0.14, 0.12) if along[0] else (0.14, ln, 0.12), "DarkWood")
            q = [(x + along[0] * a, y + along[1] * a, 0.56), (x + along[0] * b, y + along[1] * b, 0.56),
                 (x + along[0] * b, y + along[1] * b, 0.66), (x + along[0] * a, y + along[1] * a, 0.66)]
            self.deco.quad(q, "DarkWood", both=True)
            for p in (a + 0.08, mid, b - 0.08):
                self.solid.box((x + along[0] * p, y + along[1] * p, 0.53), (0.14, 0.14, 1.06), "DarkWood", skip=("top",))
        if broken:
            self.deco.box((x + along[0] * 0.5, y + along[1] * 0.5, -0.3), (0.12, 1.5, 0.1) if along[1] else (1.5, 0.12, 0.1),
                          "DarkWood", rx=18 if along[1] else 0, ry=22 if along[0] else 0)

    def build_pits(self):
        holes = set(self.pit_tiles)
        if self.open_center:
            holes |= {(i, j) for i in range(4, 8) for j in range(4, 8)}
        depth = 12.0
        for i0, j0, i1, j1 in merge_tiles(holes):
            self.deco.quad([(-10 + i * TS, -10 + j * TS, -depth) for i, j in ((i0, j0), (i1, j0), (i1, j1), (i0, j1))], "Void")
        for i, j in sorted(holes):
            x, y = self.tile_center(i, j)
            h = TS / 2
            for di, dj in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                if (i + di, j + dj) in holes:
                    continue
                ex, ey = x + di * h, y + dj * h
                rx, ry = -(-dj), -di                     # 穴の中から縁を見たときの右向き
                a, b = (ex - rx * h, ey - ry * h), (ex + rx * h, ey + ry * h)
                self.deco.quad([(a[0], a[1], -depth), (b[0], b[1], -depth), (b[0], b[1], -0.01), (a[0], a[1], -0.01)], "pit")
                if (i + di, j + dj) not in self.tiles:
                    continue
                # 割れた床板の先が、穴へはみ出す（三角形）
                for k in range(2):
                    off = (k - 0.5) * TS / 2 + self.rng.uniform(-0.2, 0.2)
                    ln, wd = self.rng.uniform(0.35, 0.8), 0.2
                    bx, by = ex + (0 if di else off), ey + (off if di else 0)
                    tx, ty = (0, wd) if di else (wd, 0)
                    tip = (bx - di * ln, by - dj * ln, -ln * self.rng.uniform(0.1, 0.35))
                    p0, p1 = (bx - tx, by - ty, 0.004), (bx + tx, by + ty, 0.004)
                    tri = [p0, p1, tip]
                    n = (Vector(p1) - Vector(p0)).cross(Vector(tip) - Vector(p0))
                    self.deco.skin(tri if n.z > 0 else [p1, p0, tip], "Plank2")

    # ------------------------------------------------------------ 家具
    def local(self, x, y, rz, dx, dy):
        a = math.radians(rz)
        return x + dx * math.cos(a) - dy * math.sin(a), y + dx * math.sin(a) + dy * math.cos(a)

    def part(self, mesh, x, y, rz, dx, dy, z, size, mat, **kw):
        px, py = self.local(x, y, rz, dx, dy)
        mesh.box((px, py, z), size, mat, rz=rz, **kw)

    def sheet(self, mesh, x, y, rz, dx, z0, z1, width, name, dy=0.0, both=True):
        """立てた薄い板（布・額など）。1 面か 2 面だけ"""
        a, b = self.local(x, y, rz, dx - width / 2, dy), self.local(x, y, rz, dx + width / 2, dy)
        mesh.quad([(a[0], a[1], z0), (b[0], b[1], z0), (b[0], b[1], z1), (a[0], a[1], z1)], name, both=both)

    def long_table(self, x, y, rz=0, cloth="RedCloth"):
        top = {"RedCloth": "table_red", "BlueCloth": "table_blue", "PaleCloth": "table_pale"}.get(cloth, "table_plain")
        self.part(self.solid, x, y, rz, 0, 0, 1.26, (3.4, 1.8, 0.12), "DarkWood", faces={"top": top})
        for sx in (-1.62, 1.62):
            for sy in (-0.82, 0.82):
                self.part(self.solid, x, y, rz, sx, sy, 0.60, (0.12, 0.12, 1.20), "DarkWood", skip=("top",))
        self.hides.append((x, y))

    def shelf(self, x, y, rz=0, w=2.4, h=2.2, items=True):
        self.part(self.solid, x, y, rz, 0, 0, h / 2, (w, 0.6, h), "DarkWood", faces={"front": "shelf"})

    def crates(self, x, y, rz=0, stack=True):
        every = {n: "crate" for n in FACE_NAMES}
        self.part(self.solid, x, y, rz, 0, 0, 0.6, (1.3, 1.3, 1.2), "crate", faces=every)
        if stack:
            self.part(self.solid, x, y, rz + 17, 0.1, -0.05, 1.6, (0.85, 0.85, 0.8), "crate", faces=every, skip=("bottom",))

    def barrel(self, x, y):
        self.solid.prism((x, y, 0.62), 0.5, 1.24, "Plank2")
        self.deco.prism((x, y, 0.95), 0.52, 0.09, "Rust")

    def pillar(self, x, y, w=0.5):
        self.solid.box((x, y, H / 2), (w, w, H), "DarkWood")

    def counter(self, x, y, rz=0, w=4.0, d=0.9, top="Plank", body="DarkWood"):
        self.part(self.solid, x, y, rz, 0, 0, 0.59, (w, d, 1.18), body, faces={"top": top})

    def byobu(self, x, y, rz=0, panels=4):
        px = -0.35 * panels
        for i in range(panels):
            ang = 28 if i % 2 == 0 else -28
            dx = px + 0.4 * math.cos(math.radians(ang))
            cx, cy = self.local(x, y, rz, dx, 0)
            self.sheet(self.solid, cx, cy, rz + ang, 0, 0.0, 2.2, 0.84, "byobu")
            px += 0.8 * math.cos(math.radians(ang))

    def partition(self, x, y, rz=0, w=3.0, torn=True):
        self.part(self.solid, x, y, rz, 0, 0, 1.35, (w, 0.12, 2.7), "DarkWood", faces={"front": "shoji", "back": "shoji"})

    def toppled_table(self, x, y, rz=0):
        self.part(self.solid, x, y, rz, 0, 0, 0.9, (3.0, 0.14, 1.8), "DarkWood", faces={"front": "table_plain", "back": "table_plain"})
        for sx in (-1.3, 1.3):
            self.part(self.deco, x, y, rz, sx, 0.6, 1.55, (0.12, 1.1, 0.12), "DarkWood")

    def cabinet(self, x, y, rz=0, neon="NeonCyan", dead=False):
        screen = "screen_dead" if dead else {"NeonCyan": "screen_cyan", "NeonRed": "screen_red"}.get(neon, "screen_yellow")
        body = self.rng.choice(["Metal", "BlueCloth", "Rust"])
        self.part(self.solid, x, y, rz, 0, 0.05, 1.0, (1.0, 0.8, 2.0), body)
        self.sheet(self.deco, x, y, rz, 0, 1.0, 1.75, 0.78, screen, dy=-0.37, both=False)
        self.sheet(self.deco, x, y, rz, 0, 1.78, 1.96, 0.9, "BlackLacquer" if dead else "NeonYellow", dy=-0.37, both=False)

    def bath(self, x, y, w=8.0, d=6.0):
        side = (w - 2.0) / 2   # 手前の縁は壊れていて、歩いて中に入れる
        for dx, dy, sx, sy in [(0, d / 2, w, 0.4), (-(w - side) / 2, -d / 2, side, 0.4), ((w - side) / 2, -d / 2, side, 0.4),
                               (w / 2, 0, 0.4, d - 0.4), (-w / 2, 0, 0.4, d - 0.4)]:
            self.solid.box((x + dx, y + dy, 0.55), (sx, sy, 1.1), "Stone")
        self.deco.box((x, y, 0.16), (w - 0.4, d - 0.4, 0.02), "Water")
        for k in range(3):
            self.deco.lump((x - 0.7 + k * 0.7, y - d / 2 - self.rng.uniform(0.2, 0.7), 0.05), 0.28, 0.22, 0.2, "Rubble")

    def stone_lantern(self, x, y):
        self.solid.prism((x, y, 0.55), 0.22, 1.1, "Stone", n=4)
        self.solid.box((x, y, 1.3), (0.7, 0.7, 0.45), "Stone")
        self.sheet(self.deco, x, y - 0.36, 0, 0, 1.18, 1.42, 0.3, "Void", both=False)
        self.solid.prism((x, y, 1.72), 0.7, 0.35, "Stone", n=4, r_top=0.02)

    def tree(self, x, y):
        self.solid.prism((x, y, 1.6), 0.36, 3.2, "DarkWood", n=5, r_top=0.18)
        for ang, z, ln in [(20, 2.4, 1.8), (150, 2.9, 1.5), (280, 2.1, 1.3)]:
            a = math.radians(ang)
            self.deco.box((x + math.cos(a) * ln * 0.42, y + math.sin(a) * ln * 0.42, z + 0.25), (ln, 0.1, 0.1), "DarkWood", rz=ang, ry=-30)

    def mikoshi(self, x, y):
        self.solid.box((x, y, 0.75), (1.8, 1.8, 1.5), "BlackLacquer", faces={n: "byobu" for n in ("front", "back", "left", "right")})
        self.solid.prism((x, y, 1.85), 1.45, 0.7, "curtain", n=4, r_top=0.15)
        self.deco.prism((x, y, 2.45), 0.14, 0.5, "OldGold", n=4, r_top=0.02)
        for dy in (-1.15, 1.15):
            self.deco.box((x, y + dy, 0.25), (3.4, 0.16, 0.16), "DarkWood", rz=3)

    # ------------------------------------------------------------ 飾り
    def cushion(self, x, y, rz=0, mat="RedCloth"):
        self.deco.box((x, y, 0.07), (0.72, 0.72, 0.12), mat, rz=rz)

    def tray(self, x, y, rz=0):
        self.part(self.deco, x, y, rz, 0, 0, 0.15, (0.5, 0.5, 0.3), "BlackLacquer", faces={"top": "tray"})

    def futon(self, x, y, rz=0, messy=False):
        self.part(self.deco, x, y, rz + (14 if messy else 0), 0, 0, 0.09, (1.25, 2.2, 0.16), "PaleCloth", faces={"top": "futon"})

    def yukata(self, x, y, rz=0):
        self.sheet(self.deco, x, y, rz, 0, 3.12, 3.19, 1.7, "DarkWood")
        self.sheet(self.deco, x, y, rz, 0, 1.4, 3.12, 0.75, "PaleCloth")
        for s in (-1, 1):
            self.sheet(self.deco, x, y, rz, s * 0.62, 2.45, 3.1, 0.5, "PaleCloth")
        self.sheet(self.deco, x, y, rz, 0, 1.4, 1.65, 0.8, "BlueCloth", dy=-0.02)
        self.sheet(self.deco, x, y, rz, 0, 1.4, 1.65, 0.8, "BlueCloth", dy=0.02)

    def lantern(self, x, y, z=3.2, mat="Paper"):
        self.deco.prism((x, y, z), 0.3, 0.6, mat)
        self.sheet(self.deco, x, y, 0, 0, z + 0.3, H, 0.04, "BlackLacquer")

    def noren(self, x, y, rz=0, mat="BlueCloth"):
        name = "noren" if mat == "BlueCloth" else "curtain"
        for k in range(4):
            if self.rng.random() < 0.2:
                continue
            self.sheet(self.deco, x, y, rz, -1.12 + k * 0.75, DOOR_H - self.rng.uniform(0.65, 1.0), DOOR_H, 0.68, name)

    def pot(self, x, y, broken=False):
        if not broken:
            self.deco.prism((x, y, 0.35), 0.2, 0.7, "BlueCloth", r_top=0.34)
            self.deco.prism((x, y, 0.77), 0.34, 0.14, "BlueCloth", r_top=0.18)
            return
        for _ in range(3):
            self.deco.lump((x + self.rng.uniform(-0.4, 0.4), y + self.rng.uniform(-0.4, 0.4), 0.0), 0.22, 0.14, 0.14, "BlueCloth")

    def stool(self, x, y, fallen=False):
        if fallen:
            self.deco.box((x, y, 0.2), (0.6, 0.38, 0.38), "Plank", rz=self.rng.uniform(0, 180))
        else:
            self.deco.box((x, y, 0.25), (0.45, 0.45, 0.5), "Metal", faces={"top": "RedCloth"})

    def bags(self, x, y, n=3):
        for _ in range(n):
            px, py = x + self.rng.uniform(-0.6, 0.6), y + self.rng.uniform(-0.6, 0.6)
            if self.on_floor(px, py, 0.4):
                self.deco.lump((px, py, 0.05), self.rng.uniform(0.3, 0.45), self.rng.uniform(0.3, 0.45), 0.55, self.rng.choice(["BlackLacquer", "BlueCloth", "Rubble"]))

    def frame(self, x, y, rz=0, tilt=9):
        a, b = self.local(x, y, rz, -0.6, 0), self.local(x, y, rz, 0.6, 0)
        lift = math.tan(math.radians(tilt)) * 0.6
        self.deco.quad([(a[0], a[1], 1.7 - lift), (b[0], b[1], 1.7 + lift), (b[0], b[1], 2.55 + lift), (a[0], a[1], 2.55 - lift)], "painting", both=True)

    def stage(self, x, y, w, d):
        self.deco.box((x, y, 0.016), (w, d, 0.014), "BlackLacquer")

    def curtain(self, x, y, rz, n=10):
        total = n * 0.5
        parts = 3
        for k in range(parts):
            width = total / parts
            self.sheet(self.deco, x, y, rz, -total / 2 + width * (k + 0.5), 2.4 if k == 1 else 0.3, H - 0.1, width, "curtain", dy=0.05 * (k % 2))

    def drag(self, x, y, rz, n=7):
        for i in range(0, n, 2):
            px, py = self.local(x, y, rz, i * 0.95, 0.04 * i)
            if self.on_floor(px, py, 0.5):
                self.deco.box((px, py, 0.014), (1.5, 0.17 - i * 0.012, 0.006), "Stain", rz=rz + 3)

    def water_patch(self, x, y, w, d, rz=0):
        self.deco.box((x, y, 0.011), (w, d, 0.006), "Water", rz=rz)

    def tuft(self, x, y):
        for k in range(3):
            a = k * 2.1 + self.rng.uniform(0, 1)
            p = [(x - 0.1 * math.cos(a), y - 0.1 * math.sin(a), 0), (x + 0.1 * math.cos(a), y + 0.1 * math.sin(a), 0),
                 (x + self.rng.uniform(-0.15, 0.15), y + self.rng.uniform(-0.15, 0.15), self.rng.uniform(0.2, 0.4))]
            self.deco.skin(p, "Moss")
            self.deco.skin([p[1], p[0], p[2]], "Moss")

    def decay(self, clusters=3, papers=0, boards=3, holes=0):
        """瓦礫と落ちた板（紙くず・汚れ・壁の穴は、テクスチャに描いてある）"""
        r = self.rng
        sides = list(self.wall_sides)
        r.shuffle(sides)
        for inx, iny, dx, dy, along in sides[:min(clusters, 4)]:
            off = r.uniform(-2.6, 2.6)
            bx, by = inx + along[0] * off - dx * 0.5, iny + along[1] * off - dy * 0.5
            for _ in range(4):
                s = r.uniform(0.16, 0.45)
                px, py = bx + r.gauss(0, 0.4), by + r.gauss(0, 0.4)
                if self.on_floor(px, py, 0.4):
                    self.deco.lump((px, py, 0.02), s, s * r.uniform(0.5, 1.2), s * 0.7, "Rubble")
        for inx, iny, dx, dy, along in sides[4:4 + min(boards, 3)]:
            px, py = inx - dx * 1.0, iny - dy * 1.0
            if self.on_floor(px, py, 0.8):
                self.deco.box((px, py, 0.18), (1.6, 0.45, 0.06), "Plank2", rz=r.uniform(0, 180), rx=r.uniform(-10, 10))


