"""トラックのテクスチャと、玄関前の通路（トラックとマップの間）のモデルを作る。

実行（どの .blend も保存しない）:
  blender --factory-startup --disable-autoexec -b Docs/MapPrototype/Chips_v2/MapChips_v2.blend
          --python Docs/MapPrototype/Truck/build_truck.py -- <プロジェクトの場所> <確認画像の出力先>

出力は Assets/Models/TruckEntrance（textures と approach.json）。Unity 側は Assets/Editor/TruckEntranceImporter が読み込む。
座標は「トラックのプレハブの中の位置（m）」: X はトラックの奥が +（部屋との境目が -10、トラックの後ろが 0）、Y が上、Z が横。
"""
import bpy, sys, os, json, math, random
sys.dont_write_bytecode = True
import numpy as np
from mathutils import Vector, Matrix

project, preview_dir = sys.argv[sys.argv.index("--") + 1:][:2]
here = os.path.dirname(os.path.abspath(__file__))
sys.path[:0] = [here, os.path.join(here, "..", "Chips_v2", "scripts")]
import textures, textures_truck

out_dir = os.path.join(project, "Assets", "Models", "TruckEntrance")
ryokan_textures = os.path.join(project, "Assets", "Models", "Map", "Ryokan", "textures")
textures_truck.build(os.path.join(out_dir, "textures"))
rng = random.Random(7)

X0, X1 = -10.0, 0.0      # 部屋との境目 〜 トラックの後ろ
W = 3.25                 # 通路の内側の幅の半分（部屋の腕の壁の厚みの中に収まり、境目に隙間ができない）
H = 6.0                  # 天井の高さ（部屋と同じ）
FH = 3.4                 # 板塀の高さ（その上は土壁）
HOLE_W, HOLE_H = 2.6, 3.9    # トラックの後ろの開口（元のモデルと同じ）


def V(*p):
    return Vector(p)


class Mesh:
    def __init__(self):
        self.parts = {}

    def quad(self, mat, pts, uvs, normal, both=False):
        pts = [Vector(p) for p in pts]
        normal = Vector(normal)
        # Unity は、座標の数値で cross(p1-p0, p2-p0) が向く側が表
        if (pts[1] - pts[0]).cross(pts[2] - pts[0]).dot(normal) < 0:
            pts, uvs = pts[::-1], uvs[::-1]
        part = self.parts.setdefault(mat, {"v": [], "t": [], "i": []})
        n = len(part["v"])
        part["v"] += pts
        part["t"] += [tuple(u) for u in uvs]
        part["i"] += [n, n + 1, n + 2, n, n + 2, n + 3]
        if both:
            self.quad(mat, pts, uvs, -normal)

    def plane(self, mat, pts, normal, tile=2.0, swatch=None, both=False):
        """向いている軸に合わせて、くり返しの UV を貼る。swatch を指定すると、家具用テクスチャの色見本を貼る"""
        n = Vector(normal)
        axis = max(range(3), key=lambda k: abs(n[k]))
        a, b = {0: (2, 1), 1: (0, 2), 2: (0, 1)}[axis]
        tu, tv = tile if isinstance(tile, tuple) else (tile, tile)
        if swatch:
            u0, v0, u1, v1 = textures.region(swatch)
            uvs = [(u0, v0), (u1, v0), (u1, v1), (u0, v1)]
        else:
            uvs = [(p[a] / tu, p[b] / tv) for p in pts]
        self.quad(mat, pts, uvs, normal, both)

    def box(self, mat, center, size, tile=2.0, swatch=None, rot=(0, 0, 0)):
        c = Vector(center)
        R = (Matrix.Rotation(math.radians(rot[2]), 3, "Z") @ Matrix.Rotation(math.radians(rot[1]), 3, "Y")
             @ Matrix.Rotation(math.radians(rot[0]), 3, "X"))
        hx, hy, hz = (s / 2 for s in size)
        k = [c + R @ Vector((x, y, z)) for x in (-hx, hx) for y in (-hy, hy) for z in (-hz, hz)]
        for idx, n in (((0, 1, 3, 2), (-1, 0, 0)), ((4, 5, 7, 6), (1, 0, 0)), ((0, 1, 5, 4), (0, -1, 0)),
                       ((2, 3, 7, 6), (0, 1, 0)), ((0, 2, 6, 4), (0, 0, -1)), ((1, 3, 7, 5), (0, 0, 1))):
            pts = [k[i] for i in idx]
            nn = R @ Vector(n)
            if swatch:
                self.plane(mat, pts, nn, swatch=swatch)
            else:
                axis = max(range(3), key=lambda q: abs(n[q]))
                a, b = {0: (2, 1), 1: (0, 2), 2: (0, 1)}[axis]
                local = [Vector((-hx if i < 4 else hx, -hy if (i // 2) % 2 == 0 else hy, -hz if i % 2 == 0 else hz)) for i in idx]
                self.quad(mat, pts, [((p[a] + c[a]) / tile, (p[b] + c[b]) / tile) for p in local], nn)

    def prism(self, mat, center, radius, height, n=8, swatch=None):
        cx, cy, cz = center
        ring = [(math.cos(2 * math.pi * i / n), math.sin(2 * math.pi * i / n)) for i in range(n)]
        u0, v0, u1, v1 = textures.region(swatch) if swatch else (0, 0, 1, 1)
        for i in range(n):
            (ax, az), (bx, bz) = ring[i], ring[(i + 1) % n]
            pts = [(cx + ax * radius, cy - height / 2, cz + az * radius), (cx + bx * radius, cy - height / 2, cz + bz * radius),
                   (cx + bx * radius, cy + height / 2, cz + bz * radius), (cx + ax * radius, cy + height / 2, cz + az * radius)]
            ua, ub = u0 + (u1 - u0) * i / n, u0 + (u1 - u0) * (i + 1) / n
            self.quad(mat, pts, [(ua, v0), (ub, v0), (ub, v1), (ua, v1)], (ax + bx, 0, az + bz))
        for s in (-1, 1):
            y = cy + s * height / 2
            for i in range(0, n - 2, 2):
                pts = [(cx + ring[j % n][0] * radius, y, cz + ring[j % n][1] * radius) for j in (0, i + 1, i + 2, i + 3 if i + 3 < n else 0)]
                self.quad(mat, pts, [(u0, v0), (u1, v0), (u1, v1), (u0, v1)], (0, s, 0))

    def tris(self):
        return sum(len(p["i"]) // 3 for p in self.parts.values())


col, vis = Mesh(), Mesh()    # 当たり判定 / 見た目
DARK = "DarkWood"
PROPS = "Ryokan_Props"


def rect_x(mesh, mat, x, z0, z1, y0, y1, nx, tile):
    mesh.plane(mat, [(x, y0, z0), (x, y0, z1), (x, y1, z1), (x, y1, z0)], (nx, 0, 0), tile)


# ───────── 当たり判定：床・左右の壁・天井・トラック側の壁 ─────────
col.plane("c", [(X0, 0, -W), (X1, 0, -W), (X1, 0, W), (X0, 0, W)], (0, 1, 0))
col.plane("c", [(X0, H, -W), (X1, H, -W), (X1, H, W), (X0, H, W)], (0, -1, 0))
for s in (-1, 1):
    col.plane("c", [(X0, 0, s * W), (X1, 0, s * W), (X1, H, s * W), (X0, H, s * W)], (0, 0, -s))
    rect_x(col, "c", X1, s * HOLE_W, s * W, 0, H, -1, 1)
rect_x(col, "c", X1, -HOLE_W, HOLE_W, HOLE_H, H, -1, 1)
rect_x(col, "c", X1, -HOLE_W, HOLE_W, HOLE_H, 4.45, 1, 1)

# ───────── 石畳 ─────────
vis.plane("Entrance_Stone", [(X0, 0, -W), (X1, 0, -W), (X1, 0, W), (X0, 0, W)], (0, 1, 0), 3.6)

# ───────── 左右の板塀と土壁、柱 ─────────
POSTS = [-9.82, -7.4, -4.9, -2.5, -0.18]
for s in (-1, 1):
    z = s * W
    vis.plane("Entrance_Fence", [(X0, 0, z), (X1, 0, z), (X1, FH, z), (X0, FH, z)], (0, 0, -s), FH)
    vis.plane("Ryokan_Plaster", [(X0, FH, z), (X1, FH, z), (X1, H, z), (X0, H, z)], (0, 0, -s), (20 / 3, H))
    vis.box("Ryokan_WoodWall", (-5.0, FH + 0.05, s * (W - 0.07)), (10.0, 0.16, 0.14))
    for x in POSTS:
        vis.box(PROPS, (x, H / 2, s * (W - 0.09)), (0.2, H, 0.18), swatch=DARK)

    # 壊れかけの庇（柱の間ごと。抜けた所と、垂れ下がった所がある）
    for k in range(len(POSTS) - 1):
        a, b = POSTS[k] + 0.12, POSTS[k + 1] - 0.12
        if (s, k) in ((1, 2), (-1, 0)):
            vis.box(PROPS, ((a + b) / 2, 4.1, s * (W - 0.45)), (0.08, 0.08, 0.9), swatch=DARK, rot=(s * -22, 0, 0))
            continue
        drop_a, drop_b = (0.0, 0.62) if (s, k) in ((1, 0), (-1, 2)) else (0.0, 0.0)
        top, low, inner = 4.42, 3.95, s * (W - 0.85)
        pts = [(a, top - drop_a * 0.4, z), (b, top - drop_b * 0.4, z), (b, low - drop_b, inner), (a, low - drop_a, inner)]
        n = (Vector(pts[1]) - Vector(pts[0])).cross(Vector(pts[3]) - Vector(pts[0]))
        if n.y < 0:
            n = -n
        vis.plane("Ryokan_Ceiling", pts, n, 2.0, both=True)
        vis.box(PROPS, ((a + b) / 2, low - (drop_a + drop_b) / 2 - 0.05, inner), (b - a, 0.09, 0.09), swatch=DARK,
                rot=(0, 0, math.degrees(math.atan2(drop_a - drop_b, b - a))))

# ───────── 屋根（ところどころ抜けて、暗い空が見える。当たり判定は全面にある） ─────────
HOLES = {(1, 1), (1, 2), (4, 3), (5, 3), (6, 0), (3, 2)}
for i in range(8):
    for j in range(5):
        if (i, j) in HOLES:
            continue
        ax, bx = X0 + i * 1.25, X0 + (i + 1) * 1.25
        az, bz = -W + j * 1.3, -W + (j + 1) * 1.3
        vis.plane("Ryokan_Ceiling", [(ax, H, az), (bx, H, az), (bx, H, bz), (ax, H, bz)], (0, -1, 0), 3.0)
for x in (-7.4, -4.9, -2.5):
    vis.box(PROPS, (x, H - 0.14, 0), (0.22, 0.26, 2 * W), swatch=DARK)
for i, j in sorted(HOLES):
    x, zc = X0 + (i + 0.5) * 1.25, -W + (j + 0.5) * 1.3
    vis.box("Ryokan_Ceiling", (x + rng.uniform(-0.3, 0.3), H - 0.42, zc), (0.16, 0.06, 1.0),
            rot=(rng.uniform(25, 50), rng.uniform(-25, 25), rng.uniform(-8, 8)))

# ───────── トラックの後ろの壁と、門の枠 ─────────
for s in (-1, 1):
    rect_x(vis, "Entrance_Fence", X1, s * HOLE_W, s * W, 0, FH, -1, FH)
    rect_x(vis, "Ryokan_Plaster", X1, s * HOLE_W, s * W, FH, H, -1, (20 / 3, H))
    vis.box(PROPS, (-0.12, HOLE_H / 2, s * (HOLE_W + 0.12)), (0.22, HOLE_H, 0.2), swatch=DARK)
rect_x(vis, "Ryokan_Plaster", X1, -HOLE_W, HOLE_W, HOLE_H, H, -1, (20 / 3, H))
rect_x(vis, "Ryokan_Plaster", X1, -HOLE_W, HOLE_W, HOLE_H, 4.45, 1, (20 / 3, H))
vis.box(PROPS, (-0.13, HOLE_H + 0.16, 0), (0.24, 0.32, 5.9), swatch=DARK)
vis.box("Ryokan_WoodWall", (-0.14, 4.95, 0.15), (0.08, 0.85, 2.8), rot=(5, 0, 0))       # 傾いた看板（無地）
for dz in (-1.1, 1.4):
    vis.box(PROPS, (-0.2, 4.95, dz), (0.05, 0.95, 0.07), swatch="Rust", rot=(5, 0, 0))

# ───────── 部屋側の入口の、破れた暖簾（扉の高さ 3.4m より上） ─────────
vis.box(PROPS, (-9.8, 4.42, 0), (0.1, 0.1, 5.2), swatch=DARK)
for k in range(7):
    z = -2.16 + k * 0.72
    low = [3.5, 3.78, 3.52, 4.05, 3.62, 3.5, 3.9][k]
    vis.plane(PROPS, [(-9.78, low + rng.uniform(-0.1, 0.1), z - 0.32), (-9.78, low, z + 0.32),
                      (-9.78, 4.38, z + 0.32), (-9.78, 4.38, z - 0.32)], (1, 0, 0), swatch="Curtain", both=True)

# ───────── 提灯 ─────────
lights = []


def lamp(x, y, z, radius, height, intensity, reach):
    vis.prism("Entrance_Lantern", (x, y, z), radius, height)
    for s in (-1, 1):
        vis.prism(PROPS, (x, y + s * (height / 2 + 0.03), z), radius * 0.68, 0.07, swatch="BlackLacquer")
    lights.append({"x": x, "y": y, "z": z, "intensity": intensity, "range": reach})


for x, s in ((-7.4, 1), (-2.5, -1)):
    z = s * 2.68
    vis.box(PROPS, (x, 3.36, s * (W - 0.42)), (0.07, 0.07, 0.8), swatch=DARK)
    vis.box(PROPS, (x, 3.26, z), (0.025, 0.2, 0.025), swatch="BlackLacquer")
    lamp(x, 2.9, z, 0.24, 0.5, 4.5, 9.0)
for x, y, z, power, reach in ((2.7, 3.85, -0.7, 2.6, 6.5), (7.4, 3.75, 0.6, 3.0, 7.0)):    # 荷台の裸電球
    vis.box(PROPS, (x, y + 0.32, z), (0.02, 0.5, 0.02), swatch="BlackLacquer")
    lamp(x, y, z, 0.09, 0.14, power, reach)

# ───────── 足元の瓦礫と、立てかけた板（当たり判定なし。道の端に寄せる） ─────────
for _ in range(22):
    s = rng.choice((-1, 1))
    size = rng.uniform(0.08, 0.28)
    vis.box(PROPS, (rng.uniform(X0 + 0.3, X1 - 0.4), size * 0.25, s * rng.uniform(2.8, 3.1)),
            (size, size * 0.55, size * rng.uniform(0.6, 1.3)), swatch="Rubble",
            rot=(rng.uniform(-20, 20), rng.uniform(0, 180), rng.uniform(-20, 20)))
for x, s in ((-8.5, -1), (-8.15, -1), (-3.6, 1)):
    vis.box("Ryokan_WoodWall", (x, 1.05, s * 2.95), (0.2, 2.2, 0.06), rot=(s * 13, 0, rng.uniform(-9, 9)))


# ───────── 書き出し ─────────
def dump(mesh, name):
    parts = []
    for mat, p in mesh.parts.items():
        parts.append({"material": mat, "vertices": [round(float(c), 4) for v in p["v"] for c in v],
                      "uvs": [round(float(c), 5) for u in p["t"] for c in u], "triangles": p["i"]})
    return {"name": name, "parts": parts}


with open(os.path.join(out_dir, "approach.json"), "w", encoding="utf-8") as f:
    json.dump({"meshes": [dump(vis, "Approach_Visual"), dump(col, "Approach_Collision")], "lights": lights}, f)
print("BUILD visual tris", vis.tris(), "collision tris", col.tris(), "lights", len(lights))


# ───────── 確認画像（ゲームと同じ位置に、部屋とトラックを並べる） ─────────
def to_blender(p):      # プレハブ内の位置(m) → Blender（1 = 10m）
    return Vector((-p[0] / 10, -p[2] / 10, p[1] / 10))


def material(name):
    folder = ryokan_textures if name.startswith("Ryokan_") else os.path.join(out_dir, "textures")
    m = bpy.data.materials.new("pv_" + name)
    m.use_nodes = True
    nodes = m.node_tree.nodes
    tex = nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(os.path.join(folder, name + ".png"))
    tex.interpolation = "Closest"
    bsdf = next(n for n in nodes if n.type == "BSDF_PRINCIPLED")
    m.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    nodes.active = tex
    return m


preview_mats = {}


def mat_of(name):
    if name not in preview_mats:
        preview_mats[name] = material(name)
    return preview_mats[name]


def add_object(name, faces):
    """faces: (材質名, [位置...](Blender), [uv...])"""
    verts, polys, uvs, slots, mats = [], [], [], [], []
    for mat, pts, uv in faces:
        if mat not in mats:
            mats.append(mat)
        polys.append(list(range(len(verts), len(verts) + len(pts))))
        verts += pts
        uvs += uv
        slots.append(mats.index(mat))
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], polys)
    layer = me.uv_layers.new(name="UVMap")
    for i, u in enumerate(uvs):
        layer.data[i].uv = u
    for mat in mats:
        me.materials.append(mat_of(mat))
    for p, slot in zip(me.polygons, slots):
        p.material_index = slot
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


# 通路（Unity と Blender は左右が反対なので、面の向きを保つために頂点の順を逆にする）
faces = []
for mat, p in vis.parts.items():
    for k in range(0, len(p["i"]), 3):
        tri = p["i"][k:k + 3][::-1]
        faces.append((mat, [to_blender(p["v"][i]) for i in tri], [p["t"][i] for i in tri]))
add_object("pv_approach", faces)


def truck_material(c, n):
    """TruckEntranceImporter と同じ決まりで、面ごとの材質と UV の基準を決める（c: 面の中心(m)、n: 面の向き）"""
    if n.x < -0.8 and abs(c.x - 9.58) < 0.05 and 1.45 < c.y < 3.05 and -1.25 < c.z < 2.5:
        return "Truck_Board", ((-1.2, 2.48), (1.49, 3.03))
    if n.dot(Vector((0.91, 0, -0.42))) > 0.95 and 1.34 < c.x < 1.76 and 2.58 < c.y < 3.10 and 1.45 < c.z < 2.45:
        return "Truck_Board", ((1.40, 2.50), (2.55, 3.13))
    if n.y > 0.7 and c.y < 1.35:
        return "Truck_Floor", None
    return "Truck_Metal", None


with bpy.data.libraries.load(os.path.join(project, "Assets", "Models", "Truck.blend")) as (src, dst):
    dst.objects = ["TruckMesh", "Lever"]
counts = {}
for source_name, o in zip(["TruckMesh", "Lever"], dst.objects):
    bpy.context.scene.collection.objects.link(o)
    bpy.context.view_layer.update()
    M = o.matrix_world.copy()
    faces = []
    o.data.calc_loop_triangles()
    for tri in o.data.loop_triangles:
        pb = [M @ o.data.vertices[i].co for i in tri.vertices]
        pu = [Vector((-p.x * 10, p.z * 10, -p.y * 10)) for p in pb]
        nb = (M.to_3x3() @ tri.normal).normalized()
        n = Vector((-nb.x, nb.z, -nb.y))
        c = (pu[0] + pu[1] + pu[2]) / 3
        mat, area = ("Truck_Red", None) if source_name == "Lever" else truck_material(c, n)
        counts[mat] = counts.get(mat, 0) + 1
        if area:
            (a0, a1), (b0, b1) = area
            horizontal = 2 if abs(n.x) > abs(n.z) else 0
            uv = [((p[horizontal] - a0) / (a1 - a0), (p.y - b0) / (b1 - b0)) for p in pu]
        else:
            axis = max(range(3), key=lambda k: abs(n[k]))
            a, b = {0: (2, 1), 1: (0, 2), 2: (0, 1)}[axis]
            tile = 0.6 if mat == "Truck_Red" else 2.5
            uv = [(p[a] / tile, p[b] / tile) for p in pu]
        faces.append((mat, pb, uv))
    add_object("pv_" + source_name, faces)
    bpy.context.scene.collection.objects.unlink(o)
print("BUILD truck triangles", counts)

scene = bpy.context.scene
keep_rooms = {"G_A", "G_A_Deco"}
for o in list(scene.collection.all_objects):
    if o.name.startswith("pv_"):
        continue
    o.hide_render = o.name not in keep_rooms
    if o.name in keep_rooms:
        if o.parent is None:
            o.location = (2.0, 0.0, 0.0)
        for c in o.users_collection:
            c.hide_render = False

scene.render.engine = "BLENDER_WORKBENCH"
sh = scene.display.shading
sh.light = "STUDIO"
sh.color_type = "TEXTURE"
sh.show_backface_culling = True
sh.show_specular_highlight = False
world = scene.world or bpy.data.worlds.new("w")
scene.world = world
world.color = (0.015, 0.015, 0.02)
scene.view_settings.view_transform = "Standard"
Wp, Hp = 720, 450
scene.render.resolution_x, scene.render.resolution_y = Wp, Hp
scene.render.image_settings.file_format = "PNG"
cam = bpy.data.cameras.new("pv_cam")
cam.clip_start = 0.005
cam_ob = bpy.data.objects.new("pv_cam", cam)
scene.collection.objects.link(cam_ob)
scene.camera = cam_ob
os.makedirs(preview_dir, exist_ok=True)
shots = [  # 名前, 目の位置(m), 見る点(m), レンズ
    ("1_from_truck", (5.6, 1.9, -0.2), (-9.0, 2.0, 0.0), 17),
    ("2_from_room", (-10.9, 1.8, 0.4), (0.0, 2.2, -0.1), 17),
    ("3_corridor", (-0.9, 2.0, 1.6), (-7.5, 2.6, -1.4), 15),
    ("4_truck_inside", (2.4, 2.5, -1.4), (9.6, 2.0, 0.7), 17),
]
tiles = []
for name, eye, aim, lens in shots:
    cam.lens = lens
    cam_ob.location = to_blender(eye)
    cam_ob.rotation_euler = (to_blender(aim) - to_blender(eye)).to_track_quat("-Z", "Y").to_euler()
    path = os.path.join(preview_dir, "after_" + name + ".png")
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    im = bpy.data.images.load(path)
    a = np.empty(Wp * Hp * 4, dtype=np.float32)
    im.pixels.foreach_get(a)
    tiles.append(a.reshape(Hp, Wp, 4))
gap = 6
sheet = np.ones((Hp * 2 + gap, Wp * 2 + gap, 4), dtype=np.float32)
sheet[Hp + gap:, :Wp], sheet[Hp + gap:, Wp + gap:] = tiles[0], tiles[1]
sheet[:Hp, :Wp], sheet[:Hp, Wp + gap:] = tiles[2], tiles[3]
img = bpy.data.images.new("pv_sheet", Wp * 2 + gap, Hp * 2 + gap, alpha=True)
img.pixels.foreach_set(sheet.ravel())
img.filepath_raw = os.path.join(preview_dir, "truck_after.png")
img.file_format = "PNG"
img.save()
print("BUILD done")
