"""テクスチャを貼ったスクラップ（build_scrap.py の出力）を、Unity へ取り込む形で書き出す。.blend は保存しない。

実行:
  blender --factory-startup --disable-autoexec -b Docs/ScrapModels/JackDead-Scrap_textured.blend
          --python Docs/ScrapModels/export_unity.py -- <プロジェクトの場所>

出力: Assets/Models/Scrap の scrap.json（形と数値）、textures/、icons/。Unity 側は Assets/Editor/ScrapImporter が読み込む。
下の表の数値は、アイテムデータを「最初に作るとき」だけ使う（作った後は、Unity のインスペクターで調整する）。
"""
import bpy, sys, os, json, math, shutil
import numpy as np
from mathutils import Vector, Matrix

sys.dont_write_bytecode = True
project = sys.argv[sys.argv.index("--") + 1]
here = os.path.dirname(os.path.abspath(__file__))
out = os.path.join(project, "Assets", "Models", "Scrap")
for sub in ("textures", "icons"):
    os.makedirs(os.path.join(out, sub), exist_ok=True)

# 名前, 表示名, 金額(レベル1基準), 重さ(kg), 一番長い辺(m), 壊れやすさ, 置くときの回転, 特別な効果
ITEMS = [
    ("Phone", "スマホ", 360, 0.2, 0.30, 1.6, (), {}),
    ("GameConsole", "ゲーム機", 400, 0.4, 0.40, 1.4, (), {}),
    ("StunGun", "スタンガン", 300, 0.5, 0.35, 1.0, (("X", 90), ("Y", 90)), {"playerStun": 3.5, "enemyStun": 6.0}),
    ("Battery", "バッテリー", 260, 6.0, 0.45, 0.7, (), {}),
    ("BigGear", "大きな歯車", 280, 7.0, 0.80, 0.3, (), {}),
    ("Propeller", "プロペラ", 220, 3.5, 1.00, 0.8, (), {}),
    ("Flashlight", "懐中電灯", 160, 0.6, 0.40, 1.0, (("X", 90),),
     {"lightIntensity": 2.5, "lightRange": 1.8, "lightAngle": 1.2}),
    ("Gear", "歯車", 150, 3.0, 0.50, 0.3, (), {}),
    ("Chain", "チェーン", 120, 4.0, 1.20, 0.2, (), {}),
    ("IronPlate", "鉄板", 110, 8.0, 0.80, 0.3, (), {}),
    ("Screw", "ねじ", 70, 0.8, 0.35, 0.2, (), {}),
]
# アイコンを描くときの、見る向き（Blender の座標）
VIEW = {"Phone": (0.3, -0.5, 1.0), "IronPlate": (0.5, -0.8, 1.0), "Propeller": (0.3, -0.5, 1.0), "Chain": (0.3, -0.8, 0.9),
        "GameConsole": (0.45, -1.0, 0.85), "Screw": (0.9, -0.8, 0.7), "Flashlight": (1.0, -0.45, 0.7), "StunGun": (0.7, -0.45, 1.0),
        "Battery": (0.55, -1.0, 0.7)}


def to_unity(v):  # Blender(x, y, z) → Unity。左右が反対になるので、三角形の頂点の順も逆にする
    return (-v.x, v.z, -v.y)


scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
sh = scene.display.shading
sh.light = "STUDIO"
sh.color_type = "TEXTURE"
sh.show_specular_highlight = False
sh.show_object_outline = False
scene.view_settings.view_transform = "Standard"
scene.render.film_transparent = True
S = 128
scene.render.resolution_x = scene.render.resolution_y = S
scene.render.image_settings.file_format = "PNG"
scene.render.image_settings.color_mode = "RGBA"
cam = bpy.data.cameras.new("icon_cam")
cam.type = "ORTHO"
cob = bpy.data.objects.new("icon_cam", cam)
scene.collection.objects.link(cob)
scene.camera = cob

items = []
posed = []
for name, display, price, weight, size, fragility, turns, extra in ITEMS:
    source = bpy.data.objects[name]
    me = source.data.copy()
    for axis, angle in turns:
        me.transform(Matrix.Rotation(math.radians(angle), 4, axis))
    lo = Vector([min(v.co[i] for v in me.vertices) for i in range(3)])
    hi = Vector([max(v.co[i] for v in me.vertices) for i in range(3)])
    scale = size / max(hi - lo)
    me.transform(Matrix.Scale(scale, 4) @ Matrix.Translation(-(lo + hi) / 2))  # 中心を原点にして、決めた大きさにする
    me.update()
    ob = bpy.data.objects.new("export_" + name, me)
    scene.collection.objects.link(ob)
    posed.append((name, ob))

    me.calc_loop_triangles()
    uv = me.uv_layers.active.data
    vertices, normals, uvs = [], [], []
    for tri in me.loop_triangles:
        for k in (2, 1, 0):
            vertices += [round(c, 5) for c in to_unity(me.vertices[tri.vertices[k]].co)]
            normals += [round(c, 4) for c in to_unity(Vector(tri.split_normals[k]))]
            uvs += [round(c, 5) for c in uv[tri.loops[k]].uv]
    ext = (hi - lo) * scale
    entry = {"id": name, "displayName": display, "price": price, "weight": weight, "fragility": fragility,
             "playerStun": 0, "enemyStun": 0, "lightIntensity": 1, "lightRange": 1, "lightAngle": 1,
             "vertices": vertices, "normals": normals, "uvs": uvs}
    entry.update(extra)
    items.append(entry)
    shutil.copyfile(os.path.join(here, "textures", name + ".png"), os.path.join(out, "textures", "Scrap_" + name + ".png"))
    print(f"EXPORT {name:12s} tris={len(me.loop_triangles)} size(m) x={ext.x:.3f} y(高さ)={ext.z:.3f} z={ext.y:.3f}")

with open(os.path.join(out, "scrap.json"), "w", encoding="utf-8") as f:
    json.dump({"items": items}, f, ensure_ascii=False)

# ───────── 持ち物欄のアイコン ─────────
for o in list(scene.collection.all_objects):
    if o.type == "MESH" and not o.name.startswith("export_"):
        o.hide_render = True
for name, ob in posed:
    for _, other in posed:
        other.hide_render = other is not ob
    bpy.context.view_layer.update()
    pts = [v.co for v in ob.data.vertices]
    r = max(p.length for p in pts)
    d = Vector(VIEW.get(name, (0.7, -1.0, 0.75))).normalized()
    cob.location = d * r * 4
    cob.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    cam.ortho_scale = r * 2.05
    cam.clip_start = r * 0.1
    cam.clip_end = r * 20
    path = os.path.join(out, "icons", "Icon_" + name + ".png")
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    im = bpy.data.images.load(path)
    a = np.empty(S * S * 4, dtype=np.float32)
    im.pixels.foreach_get(a)
    a = a.reshape(S, S, 4)
    a[..., :3] = np.clip(a[..., :3] * 1.45 + 0.03, 0, 1)   # 暗い画面でも見えるよう、明るくする
    im.pixels.foreach_set(a.ravel())
    im.filepath_raw = path
    im.file_format = "PNG"
    im.save()
    bpy.data.images.remove(im)
print("EXPORT done", len(items))
