"""旅館マップ用のテクスチャを作る（床4・壁3・天井1・家具用の1枚）。画素の行は下から上（UV と同じ向き）"""
import bpy, os
import numpy as np

# 色（Blender のリニア値）。画像には sRGB に直して入れる
PALETTE = {
    "Tatami": (0.45, 0.38, 0.19), "Plank": (0.23, 0.13, 0.065), "Plank2": (0.19, 0.105, 0.052),
    "Tile": (0.34, 0.40, 0.40), "Soil": (0.17, 0.14, 0.09), "Plaster": (0.30, 0.26, 0.19),
    "WoodWall": (0.14, 0.085, 0.048), "TileWall": (0.30, 0.36, 0.38), "DarkWood": (0.065, 0.036, 0.020),
    "RedCloth": (0.20, 0.018, 0.015), "BlackLacquer": (0.016, 0.012, 0.012), "Paper": (0.60, 0.55, 0.42),
    "Curtain": (0.085, 0.006, 0.012), "Stain": (0.075, 0.050, 0.025), "PaleCloth": (0.42, 0.45, 0.50),
    "BlueCloth": (0.045, 0.075, 0.17), "OldGold": (0.33, 0.22, 0.055), "Rubble": (0.13, 0.12, 0.105),
    "Stone": (0.23, 0.24, 0.22), "Metal": (0.20, 0.22, 0.23), "Rust": (0.22, 0.08, 0.03),
    "Water": (0.015, 0.045, 0.048), "Moss": (0.055, 0.12, 0.035), "Void": (0.002, 0.002, 0.003),
    "NeonRed": (0.9, 0.04, 0.06), "NeonCyan": (0.03, 0.75, 0.8), "NeonYellow": (0.95, 0.68, 0.05),
    "Cardboard": (0.28, 0.17, 0.075),
}
SW = list(PALETTE)
PANELS = ["shelf", "shoji", "byobu", "crate", "screen_cyan", "screen_red", "screen_yellow", "screen_dead",
          "mirror", "painting", "curtain", "table_red", "table_blue", "table_pale", "table_plain", "tray",
          "futon", "noren", "pit"]
ATLAS_W, ATLAS_H, CELL, PANEL = 512, 1024, 64, 128
PANEL_Y = 256                       # 色見本（8 列×4 段）の上から、絵のパネルが並ぶ


def region(name):
    """家具用テクスチャの中の範囲 (u0, v0, u1, v1)。にじみを防ぐため少し内側"""
    if name in PALETTE:
        i = SW.index(name)
        x, y, w = (i % 8) * CELL, (i // 8) * CELL, CELL
        pad = 8
    else:
        i = PANELS.index(name)
        x, y, w = (i % 4) * PANEL, PANEL_Y + (i // 4) * PANEL, PANEL
        pad = 2
    return ((x + pad) / ATLAS_W, (y + pad) / ATLAS_H, (x + w - pad) / ATLAS_W, (y + w - pad) / ATLAS_H)


def col(name):
    return np.array([c ** (1 / 2.2) for c in PALETTE[name]], dtype=np.float32)


rng = np.random.default_rng(11)


def noise(h, w, block, amount):
    """荒いドット風のむら（1.0 を中心とした明るさの倍率）"""
    small = rng.random((h // block + 1, w // block + 1)).astype(np.float32)
    big = np.kron(small, np.ones((block, block), dtype=np.float32))[:h, :w]
    return (1 + (big - 0.5) * 2 * amount)[..., None]


def fill(h, w, name, amount=0.08, block=4):
    return np.clip(col(name) * noise(h, w, block, amount) * noise(h, w, block * 6, amount * 0.8), 0, 1)


def rect(img, x, y, w, h, color, alpha=1.0):
    x0, y0 = max(0, int(x)), max(0, int(y))
    x1, y1 = min(img.shape[1], int(x + w)), min(img.shape[0], int(y + h))
    if x1 > x0 and y1 > y0:
        img[y0:y1, x0:x1] = img[y0:y1, x0:x1] * (1 - alpha) + np.asarray(color, dtype=np.float32) * alpha


def blob(img, cx, cy, rx, ry, color, alpha=0.6, soft=True):
    yy, xx = np.mgrid[0:img.shape[0], 0:img.shape[1]]
    d = ((xx - cx) / rx) ** 2 + ((yy - cy) / ry) ** 2 + (noise(img.shape[0], img.shape[1], 6, 0.35)[..., 0] - 1)
    a = np.clip(1 - d, 0, 1) if soft else (d < 1).astype(np.float32)
    a = (a * alpha)[..., None]
    img[:] = img * (1 - a) + np.asarray(color, dtype=np.float32) * a


def crack(img, x, y, length, color, step=4, width=2):
    ang = rng.uniform(0, 6.283)
    for _ in range(length):
        rect(img, x, y, width, width, color)
        ang += rng.uniform(-0.7, 0.7)
        x += np.cos(ang) * step
        y += np.sin(ang) * step


def litter(img, n, size=(5, 11)):
    for _ in range(n):
        rect(img, rng.integers(0, img.shape[1] - 12), rng.integers(0, img.shape[0] - 12),
             rng.integers(*size), rng.integers(3, 8), col("Paper") * rng.uniform(0.75, 1.0), 0.9)


def stains(img, n, color=None, r=(15, 55), alpha=0.5):
    color = col("Stain") if color is None else color
    for _ in range(n):
        blob(img, rng.integers(0, img.shape[1]), rng.integers(0, img.shape[0]), rng.integers(*r), rng.integers(*r), color, alpha)


def floor_tatami():
    img = fill(512, 512, "Tatami", 0.06, 2)
    for i in range(4):
        for j in range(2):
            x, y = i * 128, j * 256 + (128 if i % 2 else 0)
            for yy in (y % 512, (y + 256) % 512):
                shade = rng.uniform(0.86, 1.08)
                h = min(256, 512 - yy)
                img[yy:yy + h, x:x + 128] *= shade
                for k in range(0, h, 4):
                    rect(img, x, yy + k, 128, 1, col("Tatami") * 0.7, 0.35)
                rect(img, x, yy, 128, 3, col("DarkWood"), 0.7)
            rect(img, x, 0, 6, 512, col("Moss") * 0.75)
            rect(img, x + 122, 0, 6, 512, col("Moss") * 0.75)
    stains(img, 9)
    litter(img, 16)
    return img


def floor_plank():
    img = fill(512, 512, "Plank", 0.08, 2)
    for k in range(16):
        y = k * 32
        img[y:y + 32] *= rng.uniform(0.74, 1.15)
        for _ in range(7):
            rect(img, rng.integers(0, 500), y + rng.integers(4, 28), rng.integers(40, 140), 1, col("DarkWood"), 0.55)
        rect(img, 0, y, 512, 2, col("Void"), 0.8)
        for x in (rng.integers(0, 250), rng.integers(260, 510)):
            rect(img, x, y, 2, 32, col("Void"), 0.8)
            rect(img, x + 5, y + 8, 3, 3, col("Metal"))
            rect(img, x + 5, y + 22, 3, 3, col("Metal"))
    stains(img, 8)
    litter(img, 14)
    return img


def floor_tile():
    img = fill(512, 512, "Tile", 0.06, 3)
    for i in range(8):
        for j in range(8):
            img[j * 64:j * 64 + 64, i * 64:i * 64 + 64] *= (0.80 if (i + j) % 2 else 1.0) * rng.uniform(0.92, 1.05)
    for k in range(8):
        rect(img, k * 64, 0, 3, 512, col("Rubble"))
        rect(img, 0, k * 64, 512, 3, col("Rubble"))
    for _ in range(7):
        crack(img, rng.integers(20, 490), rng.integers(20, 490), rng.integers(10, 26), col("Rubble") * 0.7)
    stains(img, 6, col("Moss"), (10, 35), 0.55)
    stains(img, 4, None, (20, 50), 0.35)
    return img


def floor_soil():
    img = fill(512, 512, "Soil", 0.18, 6)
    stains(img, 14, col("Moss"), (15, 60), 0.6)
    stains(img, 8, col("Stain"), (15, 45), 0.5)
    for _ in range(260):
        x, y = rng.integers(0, 508), rng.integers(0, 500)
        if rng.random() < 0.65:
            for k in range(3):
                rect(img, x + k * 3, y, 1, rng.integers(5, 11), col("Moss") * rng.uniform(1.0, 1.5))
        else:
            rect(img, x, y, rng.integers(3, 8), rng.integers(3, 6), col("Stone") * rng.uniform(0.7, 1.0))
    return img


# 壁：横 6.67m、縦 6m（1m ≒ 横 77px、縦 85px）
def wall_common(img):
    for x in (0, 250, 500):
        rect(img, x, 0, 12, 512, col("DarkWood") * 1.1)
        rect(img, x + 11, 0, 2, 512, col("Void"), 0.6)
    rect(img, 0, 292, 512, 16, col("DarkWood"))        # 長押（高さ約 3.5m）
    rect(img, 0, 496, 512, 16, col("DarkWood"))        # 天井ぎわの回り縁
    for _ in range(14):                                # 上から垂れた雨漏りの跡
        x, ln = rng.integers(20, 490), rng.integers(50, 230)
        for k in range(ln):
            rect(img, x + rng.integers(-1, 2), 495 - k, rng.integers(3, 9), 1, col("Stain"), 0.45 * (1 - k / ln))
    img *= np.linspace(1.0, 0.78, 512, dtype=np.float32)[:, None, None]


def wall_plaster():
    img = fill(512, 512, "Plaster", 0.07, 3)
    stains(img, 7, None, (15, 60), 0.4)
    for _ in range(5):
        crack(img, rng.integers(20, 490), rng.integers(120, 470), rng.integers(10, 24), col("Stain"), 4, 2)
    blob(img, 130, 190, 34, 42, col("DarkWood"), 1.0, False)   # はがれて下地が見える所
    for y in range(160, 222, 9):
        rect(img, 106, y, 48, 3, col("Plank"), 0.9)
    for x in range(0, 512, 26):                        # 腰板（高さ約 1.1m）
        rect(img, x, 0, 26, 92, col("DarkWood") * rng.uniform(0.85, 1.35))
        rect(img, x, 0, 2, 92, col("Void"), 0.8)
    rect(img, 0, 90, 512, 9, col("DarkWood") * 1.5)
    wall_common(img)
    return img


def wall_wood():
    img = fill(512, 512, "WoodWall", 0.08, 3)
    for x in range(0, 512, 32):
        img[:, x:x + 32] *= rng.uniform(0.78, 1.15)
        rect(img, x, 0, 2, 512, col("Void"), 0.75)
        for _ in range(5):
            rect(img, x + rng.integers(5, 28), rng.integers(0, 430), 1, rng.integers(30, 110), col("DarkWood"), 0.6)
    rect(img, 0, 90, 512, 9, col("DarkWood") * 1.4)
    for x, y, w in [(60, 130, 22), (340, 370, 16)]:
        blob(img, x, y, w, w * 2, col("Void"), 1.0, False)
    stains(img, 6, col("Void"), (12, 40), 0.4)
    wall_common(img)
    return img


def wall_tile():
    img = fill(512, 512, "Plaster", 0.07, 3)
    tiles = fill(224, 512, "TileWall", 0.06, 3)
    for x in range(0, 512, 32):
        rect(tiles, x, 0, 2, 224, col("Rubble"))
    for y in range(0, 224, 32):
        rect(tiles, 0, y, 512, 2, col("Rubble"))
    for _ in range(9):
        rect(tiles, rng.integers(0, 15) * 32 + 2, rng.integers(0, 7) * 32 + 2, 30, 30, col("Rubble") * 0.8)
    img[:224] = tiles
    rect(img, 0, 222, 512, 8, col("DarkWood"))
    stains(img, 8, col("Moss"), (10, 40), 0.55)
    wall_common(img)
    return img


def ceiling():
    img = fill(512, 512, "WoodWall", 0.08, 3)
    for y in range(0, 512, 42):
        img[y:y + 42] *= rng.uniform(0.82, 1.12)
        rect(img, 0, y, 512, 2, col("Void"), 0.7)
    stains(img, 7, col("Void"), (20, 60), 0.5)
    blob(img, 370, 140, 34, 22, col("Void"), 1.0, False)
    for k in (0, 256):                                  # 梁
        rect(img, k, 0, 22, 512, col("DarkWood"))
        rect(img, 0, k, 512, 22, col("DarkWood"))
    return img


def panel(name):
    s = PANEL
    if name == "shelf":
        img = fill(s, s, "DarkWood", 0.1, 2)
        rect(img, 6, 4, s - 12, s - 10, col("Void"), 0.75)
        for y in (6, 46, 86):
            rect(img, 4, y, s - 8, 6, col("Plank"))
            x = 10
            while x < s - 22:
                w, h = int(rng.integers(10, 20)), int(rng.integers(12, 30))
                if rng.random() < 0.7:
                    c = col(["Stone", "Rust", "BlueCloth", "OldGold", "Cardboard", "Cardboard"][rng.integers(0, 6)])
                    if rng.random() < 0.5:
                        blob(img, x + w / 2, y + 6 + h / 2, w / 2, h / 2, c, 1.0, False)
                    else:
                        rect(img, x, y + 6, w, h, c)
                        rect(img, x, y + 6 + h - 3, w, 3, c * 1.3)
                x += w + int(rng.integers(2, 8))
        return img
    if name == "shoji":
        img = fill(s, s, "Paper", 0.05, 3)
        stains(img, 4, None, (6, 22), 0.5)
        for _ in range(3):
            blob(img, rng.integers(15, s - 15), rng.integers(15, s - 15), rng.integers(6, 15), rng.integers(6, 18), col("Void"), 1.0, False)
        for x in range(0, s, 25):
            rect(img, x, 0, 3, s, col("DarkWood"))
        for y in range(0, s, 31):
            rect(img, 0, y, s, 3, col("DarkWood"))
        rect(img, s - 4, 0, 4, s, col("DarkWood"))
        rect(img, 0, s - 4, s, 4, col("DarkWood"))
        return img
    if name == "byobu":
        img = fill(s, s, "OldGold", 0.09, 3)
        x, y = 8.0, 20.0
        for k in range(26):
            rect(img, x, y, 8 - k // 5, 6, col("DarkWood"))
            x += 4.2
            y += rng.uniform(0, 5)
            if k % 5 == 0:
                for _ in range(6):
                    blob(img, x + rng.integers(-18, 18), y + rng.integers(-8, 22), 4, 4, col("RedCloth") * 1.5, 1.0, False)
        stains(img, 3, None, (12, 30), 0.5)
        for a in ((0, 0, 5, s), (s - 5, 0, 5, s), (0, 0, s, 5), (0, s - 5, s, 5)):
            rect(img, *a, col("BlackLacquer"))
        return img
    if name == "crate":
        img = fill(s, s, "Cardboard", 0.1, 3)
        for k in range(s):
            rect(img, k, k * 0.9 + 4, 9, 9, col("Plank2"))
        for a in ((0, 0, 10, s), (s - 10, 0, 10, s), (0, 0, s, 10), (0, s - 10, s, 10)):
            rect(img, *a, col("DarkWood") * 1.3)
        return img
    if name.startswith("screen"):
        dead = name == "screen_dead"
        base = {"screen_cyan": "NeonCyan", "screen_red": "NeonRed", "screen_yellow": "NeonYellow"}.get(name, "Void")
        img = fill(s, s, base, 0.05, 2)
        if not dead:
            for y in range(0, s, 5):
                rect(img, 0, y, s, 2, col("Void"), 0.35)
            for x in (22, 54, 86):
                rect(img, x, 42, 22, 46, col("BlackLacquer"))
                rect(img, x + 4, 76, 15, 6, col("Paper"))
                for k in range(8):
                    rect(img, x + 13 - k, 70 - k * 3, 6, 4, col("Paper"))
        else:
            for _ in range(4):
                crack(img, 70, 74, 14, col("Metal"), 4, 1)
        for a in ((0, 0, 9, s), (s - 9, 0, 9, s), (0, 0, s, 9), (0, s - 9, s, 9)):
            rect(img, *a, col("BlackLacquer"))
        return img
    if name == "mirror":
        img = fill(s, s, "Metal", 0.05, 4)
        img *= np.linspace(0.7, 1.25, s, dtype=np.float32)[None, :, None]
        for _ in range(3):
            crack(img, 40, 80, 20, col("Void"), 5, 1)
        stains(img, 4, col("Void"), (8, 26), 0.5)
        for a in ((0, 0, 6, s), (s - 6, 0, 6, s), (0, 0, s, 6), (0, s - 6, s, 6)):
            rect(img, *a, col("DarkWood"))
        return img
    if name == "painting":
        img = fill(s, s, "Paper", 0.08, 3)
        for x in range(10, s - 10):
            rect(img, x, 10, 1, max(0, 60 - abs(x - 52) * 0.9), col("BlueCloth"))
            rect(img, x, 10, 1, max(0, 36 - abs(x - 94) * 0.8), col("Stain"))
        blob(img, 90, 92, 13, 13, col("RedCloth") * 1.4, 1.0, False)
        stains(img, 4, None, (10, 34), 0.6)
        for a in ((0, 0, 9, s), (s - 9, 0, 9, s), (0, 0, s, 9), (0, s - 9, s, 9)):
            rect(img, *a, col("OldGold") * 0.8)
        return img
    if name == "curtain":
        img = fill(s, s, "Curtain", 0.06, 2)
        fold = 1 + 0.45 * np.sin(np.arange(s, dtype=np.float32) / s * 6.283 * 4)
        img *= fold[None, :, None]
        rect(img, 0, 0, s, 4, col("OldGold"), 0.6)
        return np.clip(img, 0, 1)
    if name.startswith("table"):
        img = fill(s, s, "DarkWood", 0.12, 3)
        for y in range(0, s, 21):
            rect(img, 0, y, s, 1, col("Void"), 0.7)
        cloth = {"table_red": "RedCloth", "table_blue": "BlueCloth", "table_pale": "PaleCloth"}.get(name)
        if cloth:
            rect(img, 6, 38, s - 12, 52, col(cloth))
            rect(img, 6, 42, s - 12, 3, col("OldGold"), 0.8)
            rect(img, 6, 83, s - 12, 3, col("OldGold"), 0.8)
        stains(img, 3, col("Void"), (6, 20), 0.5)
        return img
    if name == "tray":
        img = fill(s, s, "BlackLacquer", 0.1, 2)
        for x, y, r in [(36, 40, 20), (92, 44, 18), (64, 92, 22)]:
            blob(img, x, y, r, r, col("Paper"), 1.0, False)
            blob(img, x, y, r * 0.6, r * 0.6, col("Stain"), 1.0, False)
        for a in ((0, 0, 7, s), (s - 7, 0, 7, s), (0, 0, s, 7), (0, s - 7, s, 7)):
            rect(img, *a, col("RedCloth") * 0.8)
        return img
    if name == "futon":
        img = fill(s, s, "PaleCloth", 0.05, 3)
        rect(img, 30, 100, 68, 22, col("Paper"))
        rect(img, 0, 0, s, 80, col("BlueCloth"))
        rect(img, 0, 76, s, 7, col("Paper"), 0.8)
        for k in range(10):
            blob(img, rng.integers(10, 118), rng.integers(8, 68), 6, 6, col("PaleCloth"), 0.7, False)
        stains(img, 3, None, (8, 26), 0.6)
        return img
    if name == "noren":
        img = fill(s, s, "BlueCloth", 0.1, 2)
        blob(img, 64, 70, 30, 30, col("Paper"), 0.85, False)
        blob(img, 64, 70, 18, 18, col("BlueCloth"), 1.0, False)
        for x in (31, 63, 95):
            rect(img, x, 0, 3, 100, col("Void"))
        stains(img, 3, None, (8, 24), 0.5)
        return img
    # 穴の縦の壁：上は土と根太、下は真っ暗
    img = fill(s, s, "Soil", 0.25, 4)
    for y in (100, 114):
        rect(img, 0, y, s, 7, col("DarkWood"))
    for x in range(10, s, 31):
        rect(img, x, 80, 6, 48, col("DarkWood"))
    img *= (np.clip((np.arange(s, dtype=np.float32) - 78) / 50, 0, 1) ** 1.7)[:, None, None]
    return img


def atlas():
    img = np.zeros((ATLAS_H, ATLAS_W, 3), dtype=np.float32)
    for i, name in enumerate(SW):
        x, y = (i % 8) * CELL, (i // 8) * CELL
        amount = 0.02 if name in ("Void", "NeonRed", "NeonCyan", "NeonYellow") else 0.1
        img[y:y + CELL, x:x + CELL] = fill(CELL, CELL, name, amount, 4)
    for i, name in enumerate(PANELS):
        x, y = (i % 4) * PANEL, PANEL_Y + (i // 4) * PANEL
        img[y:y + PANEL, x:x + PANEL] = np.clip(panel(name), 0, 1)
    return img


TILED = {"Tatami": floor_tatami, "Plank": floor_plank, "Tile": floor_tile, "Soil": floor_soil,
         "Plaster": wall_plaster, "WoodWall": wall_wood, "TileWall": wall_tile, "Ceiling": ceiling}


def build(folder):
    """テクスチャを書き出し、{名前: Blender のマテリアル} を返す"""
    os.makedirs(folder, exist_ok=True)
    mats = {}
    for key, fn in list(TILED.items()) + [("Props", atlas)]:
        arr = np.clip(fn(), 0, 1).astype(np.float32)
        h, w = arr.shape[:2]
        im = bpy.data.images.new("Ryokan_" + key, w, h, alpha=False)
        rgba = np.ones((h, w, 4), dtype=np.float32)
        rgba[..., :3] = arr
        im.pixels.foreach_set(rgba.ravel())
        im.filepath_raw = os.path.join(folder, "Ryokan_" + key + ".png")
        im.file_format = 'PNG'
        im.save()
        m = bpy.data.materials.new("Ryokan_" + key)
        m.use_nodes = True
        nodes = m.node_tree.nodes
        bsdf = next(n for n in nodes if n.type == 'BSDF_PRINCIPLED')
        bsdf.inputs['Roughness'].default_value = 0.9
        tex = nodes.new('ShaderNodeTexImage')
        tex.image = im
        m.node_tree.links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])
        nodes.active = tex
        mats[key] = m
    return mats
