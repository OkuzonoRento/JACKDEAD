# スロットの当たり演出の置き方を、台のモデルに重ねて確認する画像を作る（Unity の画面そのものではない）。
# Assets/Models/SlotMachine.blend を開いて実行する（保存しない）。
# 引数: PreviewProgram.cs が書き出した絵のフォルダ、出力先フォルダ、目の画像（SlotLogo.png）
# 大きさと位置は SlotWinEffects と同じ式。台は Unity では 0.7 倍なので、m の値は 0.7 で割る。
import bpy, mathutils, sys, os, math, random
import numpy as np

img_dir, out_dir, sheet_path = sys.argv[sys.argv.index('--') + 1:]
S = 0.7
CX, FACE = -2.7455, 0.4          # リールの窓の中心（Blender は -X が上、+Z が手前、+Y が右）
WIN_W, WIN_H, TOP = 1.8, 1.041, -3.326
scene = bpy.context.scene
for o in list(bpy.data.objects):
    if o.type in ('CAMERA', 'LIGHT'):
        bpy.data.objects.remove(o)


def pixels_of(path):
    im = bpy.data.images.load(path, check_existing=True)
    w, h = im.size
    a = np.array(im.pixels[:], dtype=np.float32).reshape(h, w, 4)
    return a  # 下の行が先


def image_from(name, array):
    h, w = array.shape[:2]
    im = bpy.data.images.new(name, w, h, alpha=True)
    im.pixels = array.astype(np.float32).ravel()
    im.pack()
    return im


def emissive(name, image, tint=(1, 1, 1), strength=1.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new('ShaderNodeOutputMaterial')
    tex = nt.nodes.new('ShaderNodeTexImage')
    tex.image = image
    tex.interpolation = 'Closest'
    tex.extension = 'CLIP'
    mul = nt.nodes.new('ShaderNodeVectorMath')
    mul.operation = 'MULTIPLY'
    lin = [pow(c / 255.0, 2.2) for c in (tint[0] * 255, tint[1] * 255, tint[2] * 255)]
    mul.inputs[1].default_value = lin
    em = nt.nodes.new('ShaderNodeEmission')
    em.inputs['Strength'].default_value = strength
    tr = nt.nodes.new('ShaderNodeBsdfTransparent')
    mix = nt.nodes.new('ShaderNodeMixShader')
    nt.links.new(tex.outputs['Color'], mul.inputs[0])
    nt.links.new(mul.outputs[0], em.inputs['Color'])
    nt.links.new(tex.outputs['Alpha'], mix.inputs[0])
    nt.links.new(tr.outputs[0], mix.inputs[1])
    nt.links.new(em.outputs[0], mix.inputs[2])
    nt.links.new(mix.outputs[0], out.inputs['Surface'])
    m.surface_render_method = 'DITHERED'
    return m


made = []


def quad(material, up, right, z, width, height, angle=0.0):
    """中心から up（上）・right（右）にずらした位置の板。z は Blender の手前方向の座標"""
    mesh = bpy.data.meshes.new('q')
    c = mathutils.Vector((CX - up, right, z))
    ca, sa = math.cos(angle), math.sin(angle)
    ax = mathutils.Vector((-sa, ca, 0))   # 板の右向き
    ay = mathutils.Vector((-ca, -sa, 0))  # 板の上向き
    v = [c + ax * (sx * width / 2) + ay * (sy * height / 2) for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
    mesh.from_pydata(v, [], [(0, 1, 2, 3)])
    uv = mesh.uv_layers.new()
    for i, t in enumerate(((0, 0), (1, 0), (1, 1), (0, 1))):
        uv.data[i].uv = t
    mesh.materials.append(material)
    o = bpy.data.objects.new('q', mesh)
    scene.collection.objects.link(o)
    made.append(o)
    return o


def picture(name, tint=(1, 1, 1)):
    im = bpy.data.images.load(os.path.join(img_dir, name + '.png'), check_existing=True)
    return emissive(name + str(tint), im, tint), im.size[1] / im.size[0]


sheet = pixels_of(sheet_path)


def reel_strip(stop, seed):
    """Unity の GenerateReelStrip と同じ帯（256×2560。下の段が止まる目。目は 6 割の大きさ）"""
    a = np.zeros((2560, 256, 4), np.float32)
    a[:] = (31 / 255, 31 / 255, 38 / 255, 1)
    rng = random.Random(seed)
    size = 154
    pick = (np.arange(size) + 0.5) * 32 / size
    for cell in range(10):
        k = stop if cell == 0 else rng.randrange(6)
        sp = sheet[0:32, k * 32:(k + 1) * 32][pick.astype(int)][:, pick.astype(int)]
        y0, x0 = cell * 256 + (256 - size) // 2, (256 - size) // 2
        part = a[y0:y0 + size, x0:x0 + size]
        a[y0:y0 + size, x0:x0 + size] = np.where(sp[..., 3:4] > 0.05, sp, part)
    return image_from('reel%d_%d' % (stop, seed), a)


def set_reels(symbols):
    """本物のリールのメッシュと UV に、Unity と同じ拡大率・ずれで貼る"""
    for r, s in enumerate(symbols):
        m = emissive('r%d_%d' % (r, s), reel_strip(s, r + 3))
        nt = m.node_tree
        tex = next(n for n in nt.nodes if n.type == 'TEX_IMAGE')
        tex.extension = 'REPEAT'
        coord = nt.nodes.new('ShaderNodeTexCoord')
        move = nt.nodes.new('ShaderNodeMapping')
        move.inputs['Scale'].default_value = (1, 0.2, 1)
        move.inputs['Location'].default_value = (0, 0.96, 0)
        nt.links.new(coord.outputs['UV'], move.inputs['Vector'])
        nt.links.new(move.outputs[0], tex.inputs['Vector'])
        o = bpy.data.objects['Reel-%d' % r]
        o.data.materials.clear()
        o.data.materials.append(m)


# 台の材質（確認用の暗い鉄の色）
metal = bpy.data.materials.new('metal')
metal.use_nodes = True
bsdf = metal.node_tree.nodes['Principled BSDF']
bsdf.inputs['Base Color'].default_value = (0.16, 0.035, 0.03, 1)
bsdf.inputs['Roughness'].default_value = 0.55
for o in scene.objects:
    if o.type == 'MESH':
        o.data.materials.clear()
        o.data.materials.append(metal)

world = scene.world or bpy.data.worlds.new('w')
scene.world = world
world.use_nodes = True
world.node_tree.nodes['Background'].inputs['Color'].default_value = (0.006, 0.005, 0.006, 1)


def light(up, right, z, color, power, name='l'):
    l = bpy.data.lights.new(name, 'POINT')
    l.color = color
    l.energy = power
    o = bpy.data.objects.new(name, l)
    o.location = (CX - up, right, z)
    scene.collection.objects.link(o)
    return o


room = light(0.8, -1.2, 2.4, (0.75, 0.8, 0.9), 28, 'room').data

# プレイヤーの目の位置（台から 1.25m、少し下から見上げる）
cam = bpy.data.cameras.new('c')
cam.sensor_fit = 'VERTICAL'
cam.angle = math.radians(60)
co = bpy.data.objects.new('c', cam)
scene.collection.objects.link(co)
eye = mathutils.Vector((CX + 0.32 / S, 0, FACE + 1.25 / S))
target = mathutils.Vector((CX + 0.05 / S, 0, FACE))
fwd = (target - eye).normalized()
right = fwd.cross(mathutils.Vector((-1, 0, 0))).normalized()
up = right.cross(fwd)
m = mathutils.Matrix((right, up, -fwd)).transposed().to_4x4()
m.translation = eye
co.matrix_world = m
scene.camera = co

scene.render.engine = 'BLENDER_EEVEE'
scene.render.resolution_x, scene.render.resolution_y = 960, 640
scene.view_settings.view_transform = 'Standard'

PINK, YELLOW, ORANGE = (1.0, 0.18, 0.72), (1.0, 0.86, 0.15), (1.0, 0.5, 0.08)
white_img = image_from('white', np.ones((4, 4, 4), np.float32))
gold = emissive('gold', white_img, (1.0, 0.74, 0.18), 0.9)


def frames(count):
    mat, _ = picture('frame', PINK)
    for i in range(count):
        quad(mat, -0.05 * WIN_H, (i - 1) * 0.6, FACE + 0.015 / S, 0.6 * 0.97, WIN_H * 0.44)


def board(name):
    mat, ratio = picture(name)
    w = WIN_W * 0.96
    quad(mat, (CX - TOP) + 0.1 / S, 0, FACE + 0.03 / S, w, w * ratio)


def title(name, rate, up_off, angle=0.0):
    mat, ratio = picture(name)
    w = WIN_W * rate
    quad(mat, up_off, 0, FACE + 0.11 / S, w, w * ratio, angle)


def amount(name, height_rate, up_off):
    mat, ratio = picture(name)
    h = WIN_H * height_rate
    w = min(h / ratio, WIN_W * 0.98)
    quad(mat, up_off, 0, FACE + 0.13 / S, w, w * ratio)


def glow():
    mat, _ = picture('glow')
    quad(mat, 0, 0, FACE + 0.082 / S, WIN_W + 0.09 / S, WIN_H + 0.09 / S)


def scatter(stars, coins, seed):
    rng = random.Random(seed)
    mats = [picture('star', PINK)[0], picture('star', YELLOW)[0]]
    for i in range(stars):
        a = rng.uniform(0, 2 * math.pi)
        r = rng.uniform(0.75, 1.45)
        size = rng.uniform(0.045, 0.11) / S
        quad(mats[0 if rng.random() < 0.55 else 1], math.sin(a) * WIN_H * 0.55 * r, math.cos(a) * WIN_W * 0.55 * r,
             FACE + rng.uniform(0.09, 0.19) / S, size, size, rng.uniform(-0.6, 0.6))
    for i in range(coins):
        bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.045 / S, depth=0.015 / S)
        o = bpy.context.object
        o.location = (CX + rng.uniform(0.5, 1.5), rng.uniform(-1.1, 1.1), FACE + rng.uniform(0.35, 1.0))
        o.rotation_euler = (rng.uniform(0, 3), rng.uniform(0, 3), rng.uniform(0, 3))
        o.data.materials.append(gold)
        made.append(o)


extra = []


def render(name, symbols):
    set_reels(symbols)
    scene.render.filepath = os.path.join(out_dir, name + '.png')
    bpy.ops.render.render(write_still=True)
    for o in made + extra:
        bpy.data.objects.remove(o)
    made.clear()
    extra.clear()


# 1. 数えている途中
frames(3)
board('led_count')
amount('amount_count', 0.21, -0.05 * WIN_H)
extra.append(light(0.5, 0, FACE + 0.8, PINK, 9))
render('1_count', [0, 0, 0])

# 2. JACKPOT
frames(3)
glow()
board('led_jackpot')
title('title_jackpot', 1.02, 0, 0.05)
scatter(34, 40, 8)
extra.append(light(0.4, -0.6, FACE + 0.9, (1.0, 0.1, 0.05), 35))
extra.append(light(0.4, 0.8, FACE + 0.9, YELLOW, 18))
render('2_jackpot', [0, 0, 0])

# 3. BIG WIN
frames(3)
glow()
board('led_big')
title('title_bigwin', 0.9, WIN_H * 0.08)
amount('amount_big', 0.21, -WIN_H * 0.33)
scatter(18, 16, 6)
extra.append(light(0.4, 0, FACE + 0.9, ORANGE, 24))
render('3_bigwin', [1, 1, 1])

# 4. WIN
frames(3)
glow()
board('led_win')
amount('amount_win', 0.27, -0.05 * WIN_H)
scatter(5, 8, 4)
extra.append(light(0.4, 0, FACE + 0.9, YELLOW, 14))
render('4_win', [3, 3, 3])
print('done')
