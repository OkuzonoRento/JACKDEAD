
# ================================================================ 組み立て・検証・書き出し
def original_cells(chip):
    name = {"_": "__"}.get(chip, chip + "_") + "MapChips.fbx"
    root, _ver = parse_fbx.parse(os.path.join(fbx_dir, name))
    data = {}

    def walk(e):
        if e.id in (b'Vertices', b'PolygonVertexIndex') and e.id not in data:
            data[e.id] = e.props[0]
        for c in e.elems:
            walk(c)
    walk(root)
    v, idx = data[b'Vertices'], data[b'PolygonVertexIndex']
    polys, cur = [], []
    for i in idx:
        k = i if i >= 0 else -i - 1
        cur.append((v[k * 3], v[k * 3 + 1]))
        if i < 0:
            polys.append(cur); cur = []

    def inside(p, poly):
        hit = False
        for a, b in zip(poly, poly[1:] + poly[:1]):
            if (a[1] > p[1]) != (b[1] > p[1]) and p[0] < a[0] + (p[1] - a[1]) * (b[0] - a[0]) / (b[1] - a[1]):
                hit = not hit
        return hit
    return {(x, y) for x, y in ALL if any(inside((x * 2 / 3 + 0.011, y * 2 / 3 + 0.017), p) for p in polys)}


def check(r, solid_me):
    verts = [v.co / S for v in solid_me.vertices]
    polys = [list(p.vertices) for p in solid_me.polygons]
    tree = BVHTree.FromPolygons(verts, polys)
    problems = []

    def cast(origin, direction, dist):
        """ゲームの物理と同じく、裏向きの面は通り抜ける"""
        o, d = Vector(origin), Vector(direction)
        while dist > 0:
            loc, n, _f, gone = tree.ray_cast(o, d, dist)
            if loc is None:
                return None, None
            if n.dot(d) < 0:
                return loc, n
            o = loc + d * 1e-4
            dist -= gone + 1e-4
        return None, None

    lo = [min(v[i] for v in verts) for i in range(3)]
    hi = [max(v[i] for v in verts) for i in range(3)]
    if lo[0] < -10.001 or lo[1] < -10.001 or hi[0] > 10.001 or hi[1] > 10.001 or lo[2] < -0.001 or abs(hi[2] - H) > 0.001:
        problems.append(f"範囲外 {lo} {hi}")
    for p in solid_me.polygons:
        z = p.center.z / S
        if p.normal.z > 0.75 and 0.01 < z <= 1.0:
            problems.append(f"低い上向き面 z={z:.2f} at {[round(c / S, 1) for c in p.center]}")
            break
    samples = []
    for i in range(7):
        for j in range(7):
            x, y = (i / 3 - 1) * 8.4, (j / 3 - 1) * 8.4
            loc, n = cast((x, y, 12), (0, 0, -1), 24)
            if loc and n.z >= 0.75 and -1 <= loc.z <= 1:
                samples.append(round(loc.z, 3))
    if not samples or max(samples) > 0.01:
        problems.append(f"床の高さの判定 {sorted(set(samples))}")
    # 天井：床の上のどこでも、下から真上へ当たる
    for i, j in sorted(r.tiles | r.pit_tiles):
        cx, cy = r.tile_center(i, j)
        z, found = 2.9, False
        while not found:
            loc = tree.ray_cast(Vector((cx + 0.13, cy + 0.17, z)), Vector((0, 0, 1)), H)[0]
            if loc is None:
                break
            found = abs(loc.z - H) < 0.01
            z = loc.z + 0.001
        if not found:
            problems.append(f"天井がない {(round(cx, 1), round(cy, 1))}")
            break

    step = 0.2
    n = int(20 / step)

    def pos(c):
        return -10 + (c[0] + 0.5) * step, -10 + (c[1] + 0.5) * step

    def flood(heights, margin):
        cells = set()
        for i in range(n):
            for j in range(n):
                x, y = pos((i, j))
                if r.on_floor(x, y, margin) and all(tree.find_nearest(Vector((x, y, z)), 0.55)[0] is None for z in heights):
                    cells.add((i, j))
        goals = [(d[0] * 9.2, d[1] * 9.2) for d in r.doors]
        near = lambda g: min(cells, key=lambda c: (pos(c)[0] - g[0]) ** 2 + (pos(c)[1] - g[1]) ** 2)
        start = near(goals[0])
        seen, todo = {start}, [start]
        while todo:
            i, j = todo.pop()
            for c in ((i + 1, j), (i - 1, j), (i, j + 1), (i, j - 1)):
                if c in cells and c not in seen:
                    seen.add(c); todo.append(c)
        return cells, seen, goals, near

    cells, seen, goals, near = flood((0.65, 1.1, 1.5), 0.3)
    for g in goals:
        c = near(g)
        if c not in seen or (pos(c)[0] - g[0]) ** 2 + (pos(c)[1] - g[1]) ** 2 > 0.7:
            problems.append(f"出入口に行けない {g}")
    reach = len(seen) / max(1, len(cells))
    if reach < 0.97:
        problems.append(f"行けない場所がある {reach:.2f}")
    for d in r.doors:
        line = [(d[0] * 9.2 + (0 if d[0] else t), d[1] * 9.2 + (t if d[0] else 0)) for t in [-2 + 0.2 * k for k in range(21)]]
        width = sum((int((x + 10) / step), int((y + 10) / step)) in cells for x, y in line) * step
        if width < 2.2:
            problems.append(f"出入口が狭い {d} {width:.1f}m")
    cells2, seen2, _g, _n = flood((0.6,), 0.3)
    for x, y in r.hides:
        c = (int((x + 10) / step), int((y + 10) / step))
        if c not in seen2 or tree.ray_cast(Vector((x, y, 0.9)), Vector((0, 0, 1)), 0.7)[0] is None:
            problems.append(f"机の下に入れない {(x, y)}")
    holes = set(r.pit_tiles) | ({(i, j) for i in range(4, 8) for j in range(4, 8)} if r.open_center else set())
    for i, j in sorted(holes):
        cx, cy = r.tile_center(i, j)
        if any(cast((cx + a, cy + b, 3.0), (0, 0, -1), 30)[0] is not None for a in (-0.6, 0.01, 0.6) for b in (-0.6, 0.01, 0.6)):
            problems.append(f"穴の上に当たり判定 {(round(cx, 1), round(cy, 1))}")
            break
    return problems, reach


results = []
order = list(CHIPS)
for index, (chip, key, title, kw, fn) in enumerate(ROOMS):
    r = Room(chip, seed=100 + index * 7, **kw)
    fn(r)
    pit = bool(r.pit_tiles) or r.open_center
    name = f"{chip}_{key}" + ("_pit" if pit else "")
    coll = bpy.data.collections.new(f"{name} {title}")
    scene.collection.children.link(coll)
    root = bpy.data.objects.new(name, r.solid.make(name))
    child = bpy.data.objects.new(name + "_Deco", r.deco.make(name + "_Deco"))
    coll.objects.link(root)
    coll.objects.link(child)
    child.parent = root
    problems, reach = check(r, root.data)
    if chip != "O" and original_cells(chip) != r.cells:
        problems.append("元の FBX と形が違う")
    tris = [sum(len(p.vertices) - 2 for p in m.polygons) for m in (root.data, child.data)]
    mats = len({m.name for o in (root, child) for m in o.data.materials})
    print(f"ROOM {name} {title} tris={sum(tris)} (collider {tris[0]}) materials={mats} reach={reach:.3f} hides={len(r.hides)} "
          + ("OK" if not problems else "NG " + " / ".join(problems)))
    for o in scene.objects:
        o.select_set(False)
    root.select_set(True)
    child.select_set(True)
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.fbx(filepath=os.path.join(out_dir, "fbx", name + ".fbx"), use_selection=True, object_types={'MESH'},
                             axis_up='Z', axis_forward='-X', use_space_transform=False, apply_scale_options='FBX_SCALE_ALL',
                             path_mode='RELATIVE')
    results.append((chip, key, title, name, root, child, r))

for chip, key, title, name, root, child, r in results:
    k = order.index(chip)
    col = (k % 2) * 3 + "ABC".index(key)
    root.location = (col * 2.35 + (0.35 if col >= 3 else 0), -(k // 2) * 2.55, 0)
for o in originals:
    o.hide_set(True)
    o.hide_render = True
for im in bpy.data.images:
    if im.name.startswith("Ryokan_"):
        im.pack()
bpy.context.preferences.filepaths.save_version = 0
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out_dir, "MapChips_v2.blend"), copy=True)

# ---- 確認用の画像 ----
sh = scene.display.shading
scene.render.engine = 'BLENDER_WORKBENCH'
sh.light = 'STUDIO'
sh.color_type = 'TEXTURE'
sh.show_backface_culling = True
sh.show_cavity = False
sh.show_shadows = False
scene.view_settings.view_transform = 'Standard'
scene.world = scene.world or bpy.data.worlds.new("w")
scene.world.color = (0.012, 0.012, 0.016)
cam = bpy.data.cameras.new("cam")
cam_obj = bpy.data.objects.new("cam", cam)
scene.collection.objects.link(cam_obj)
scene.camera = cam_obj
cam.clip_start = 0.005


def shot(path, eye, target, lens, res, show):
    for item in results:
        item[4].hide_render = item[5].hide_render = item[3] not in show
    cam.type = 'PERSP'
    cam.lens = lens
    cam_obj.location = eye
    cam_obj.rotation_euler = (Vector(target) - Vector(eye)).to_track_quat('-Z', 'Y').to_euler()
    scene.render.resolution_x, scene.render.resolution_y = res
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


for chip, key, title, name, root, child, r in results:
    # 出入口を入ってすぐの目線
    d = r.doors[1] if chip in ("-", "+", "G", "O", "=") and len(r.doors) > 1 else r.doors[0]
    base = root.location
    eye = base + Vector((d[0] * 9.35, d[1] * 9.35, 2.3)) * S
    aim = base + Vector((d[0] * 1.0, d[1] * 1.0, 1.4)) * S
    if chip == "_":
        aim = base + Vector((0.2, 0.5, 1.2)) * S
    if chip == "O":
        eye = base + Vector((d[0] * 9.2 + (0 if d[0] else 1.6), d[1] * 9.2, 2.3)) * S
        aim = base + Vector((d[0] * 2.0 + (0 if d[0] else -4.0), d[1] * 2.0 + 0, 1.0)) * S
    shot(os.path.join(out_dir, "view", name + ".png"), eye, aim, 17, (480, 340), {name})

# 部屋の境目（ドア枠）の見本：廊下を 2 つ、つなげて並べる
a = next(i for i in results if i[3] == "-_A")
b = next(i for i in results if i[3] == "-_C")
keep = b[4].location.copy()
b[4].location = a[4].location + Vector((2.0, 0, 0))
bpy.context.view_layer.update()
shot(os.path.join(out_dir, "view_doorframe.png"), a[4].location + Vector((0.45, -0.12, 0.22)), a[4].location + Vector((1.2, 0.02, 0.2)),
     20, (1100, 720), {a[3], b[3]})
b[4].location = keep

# 一覧（目線の画像を 横 6 × 縦 4 にまとめる）
import numpy as np
W, Hh, cols, rows = 480, 340, 6, 4
canvas = np.zeros((Hh * rows, W * cols, 4), dtype=np.float32)
for chip, key, title, name, root, child, r in results:
    k = order.index(chip)
    cx, cy = (k % 2) * 3 + "ABC".index(key), rows - 1 - k // 2
    im = bpy.data.images.load(os.path.join(out_dir, "view", name + ".png"))
    px = np.empty(W * Hh * 4, dtype=np.float32)
    im.pixels.foreach_get(px)
    canvas[cy * Hh:(cy + 1) * Hh, cx * W:(cx + 1) * W] = px.reshape(Hh, W, 4)
for cy in range(1, rows):
    canvas[cy * Hh - 2:cy * Hh + 2, :] = (1, 1, 1, 1)
for cx in range(1, cols):
    canvas[:, cx * W - (4 if cx == 3 else 1):cx * W + (4 if cx == 3 else 1)] = (1, 1, 1, 1)
sheet = bpy.data.images.new("sheet", W * cols, Hh * rows, alpha=True)
sheet.pixels.foreach_set(canvas.ravel())
sheet.filepath_raw = os.path.join(out_dir, "sheet_inside.png")
sheet.file_format = 'PNG'
sheet.save()
print("DONE")
