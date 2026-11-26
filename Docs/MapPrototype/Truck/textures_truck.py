"""トラックと玄関前の通路のテクスチャを作る。画素の行は下から上（UV と同じ向き）。
描く道具は、旅館マップ用の textures.py（../Chips_v2/scripts）を使う"""
import bpy, os
import numpy as np
import textures as T
from textures import col, fill, rect, blob, crack, stains, litter


def rgb(r, g, b):
    """Blender のリニア値の色を、画像に入れる値へ直す"""
    return np.array([r ** (1 / 2.2), g ** (1 / 2.2), b ** (1 / 2.2)], dtype=np.float32)


def truck_metal():
    """荷台の内側：くすんだ緑の塗装が、錆びて剥げた鉄板"""
    rng = T.rng
    img = np.clip(rgb(0.085, 0.115, 0.095) * T.noise(512, 512, 3, 0.10) * T.noise(512, 512, 24, 0.10), 0, 1)
    for _ in range(26):                                 # 下の方ほど多い錆
        y = int(abs(rng.normal(0, 170))) % 512
        blob(img, rng.integers(0, 512), y, rng.integers(10, 55), rng.integers(8, 36), col("Rust"), 0.85)
    for _ in range(18):                                 # 塗装が剥げて、地金が見える所
        blob(img, rng.integers(0, 512), rng.integers(0, 512), rng.integers(4, 16), rng.integers(3, 10), col("Metal"), 0.9)
    for _ in range(16):                                 # 垂れた錆の筋
        x, y, ln = rng.integers(8, 500), rng.integers(180, 500), rng.integers(40, 170)
        for k in range(ln):
            rect(img, x + rng.integers(-1, 2), y - k, rng.integers(2, 6), 1, col("Rust"), 0.55 * (1 - k / ln))
    for x in range(0, 512, 128):                        # 鉄板の継ぎ目と鋲
        rect(img, x, 0, 4, 512, col("Void"), 0.75)
        rect(img, x + 4, 0, 2, 512, col("Metal"), 0.7)
        for y in range(14, 512, 48):
            rect(img, x + 12, y, 7, 7, col("Void"), 0.6)
            rect(img, x + 13, y + 2, 5, 5, col("Metal"))
    for y in (0, 256):
        rect(img, 0, y, 512, 5, col("Void"), 0.7)
    for _ in range(40):                                 # こすれ傷
        rect(img, rng.integers(0, 470), rng.integers(0, 510), rng.integers(12, 50), 1, col("Metal"), 0.45)
    return img


def truck_floor():
    """荷台の床：泥と錆で汚れた板"""
    rng = T.rng
    img = fill(512, 512, "Plank2", 0.10, 2)
    for k in range(10):
        y = k * 51
        img[y:y + 51] *= rng.uniform(0.75, 1.2)
        for _ in range(9):
            rect(img, rng.integers(0, 480), y + rng.integers(5, 46), rng.integers(30, 150), 1, col("Void"), 0.5)
        rect(img, 0, y, 512, 4, col("Void"), 0.85)
        for x in (8, rng.integers(230, 290), 496):
            rect(img, x, y + 12, 6, 6, col("Rust"))
            rect(img, x, y + 34, 6, 6, col("Rust"))
    stains(img, 10, col("Soil"), (20, 70), 0.6)
    stains(img, 5, col("Rust"), (12, 40), 0.55)
    stains(img, 5, col("Void"), (10, 30), 0.5)
    litter(img, 12)
    return img


def truck_board():
    """金額を出す板：黄ばんだ紙。黒い文字が読めるよう、明るくしておく"""
    rng = T.rng
    img = np.clip(rgb(0.72, 0.66, 0.50) * T.noise(256, 256, 2, 0.05) * T.noise(256, 256, 20, 0.05), 0, 1)
    for _ in range(9):                                  # ふちの近くだけ汚す（中央の文字の邪魔をしない）
        side = rng.integers(0, 4)
        t = rng.integers(0, 256)
        x, y = [(t, 8), (t, 248), (8, t), (248, t)][side]
        blob(img, x, y, rng.integers(12, 34), rng.integers(10, 28), col("Stain"), 0.65)
    for k in range(5):                                  # ふち
        rect(img, k, k, 256 - 2 * k, 1, col("DarkWood"), 0.75)
        rect(img, k, 255 - k, 256 - 2 * k, 1, col("DarkWood"), 0.75)
        rect(img, k, k, 1, 256 - 2 * k, col("DarkWood"), 0.75)
        rect(img, 255 - k, k, 1, 256 - 2 * k, col("DarkWood"), 0.75)
    return img


def truck_red():
    """出発レバー：剥げた赤い塗装"""
    rng = T.rng
    img = np.clip(rgb(0.42, 0.025, 0.018) * T.noise(128, 128, 2, 0.12), 0, 1)
    for _ in range(14):
        blob(img, rng.integers(0, 128), rng.integers(0, 128), rng.integers(3, 12), rng.integers(2, 8), col("Rust"), 0.9)
    return img


def entrance_stone():
    """玄関前の石畳：目地に苔の生えた、ふぞろいな敷石（横にくり返せる）"""
    rng = T.rng
    img = np.clip(col("Soil") * 0.8 * T.noise(512, 512, 4, 0.18), 0, 1)
    y = 0
    heights = [104, 88, 120, 96, 104]
    for h in heights:
        x = rng.integers(0, 90)
        end = x + 512
        while x < end:
            w = int(min(rng.integers(90, 190), end - x))
            if end - x - w < 60:
                w = int(end - x)
            shade = rng.uniform(0.68, 1.05)
            stone = col("Stone") * shade
            for xx in (x % 512, x % 512 - 512):
                rect(img, xx + 5, y + 5, w - 9, h - 9, stone)
                rect(img, xx + 5, y + h - 12, w - 9, 6, stone * 1.18, 0.8)
                rect(img, xx + 5, y + 5, w - 9, 5, stone * 0.7, 0.8)
            x += w
        y += h
    img *= T.noise(512, 512, 3, 0.09)
    img = np.clip(img, 0, 1).astype(np.float32)
    for _ in range(9):
        crack(img, rng.integers(20, 490), rng.integers(20, 490), rng.integers(8, 22), col("Void"), 4, 2)
    stains(img, 16, col("Moss"), (10, 46), 0.65)
    stains(img, 6, col("Void"), (14, 40), 0.4)
    litter(img, 8)
    return img


def entrance_fence():
    """板塀：横 3.4m・縦 3.4m。朽ちた縦板と、押さえの横木。下は苔"""
    rng = T.rng
    img = fill(512, 512, "WoodWall", 0.10, 2)
    for x in range(0, 512, 64):
        img[:, x:x + 64] *= rng.uniform(0.72, 1.22)
        for _ in range(8):
            rect(img, x + rng.integers(7, 58), rng.integers(0, 430), 1, rng.integers(30, 120), col("Void"), 0.5)
        rect(img, x, 0, 4, 512, col("Void"), 0.9)
    for y in (70, 400):                                 # 横木と鋲
        rect(img, 0, y, 512, 26, col("DarkWood") * 1.25)
        rect(img, 0, y, 512, 3, col("Void"), 0.8)
        for x in range(30, 512, 64):
            rect(img, x, y + 9, 7, 7, col("Rust"))
    for x, y, w, h in [(150, 250, 9, 70), (410, 170, 7, 46)]:   # 節穴と割れ
        blob(img, x, y, w, h, col("Void"), 1.0, False)
    for _ in range(26):
        blob(img, rng.integers(0, 512), rng.integers(0, 90), rng.integers(10, 44), rng.integers(8, 34), col("Moss"), 0.7)
    stains(img, 6, col("Void"), (14, 50), 0.45)
    img *= np.linspace(0.82, 1.05, 512, dtype=np.float32)[:, None, None]
    return img


def entrance_lantern():
    """提灯の紙：骨の横線と、すすけ"""
    rng = T.rng
    img = np.clip(rgb(0.80, 0.46, 0.16) * T.noise(128, 128, 2, 0.08), 0, 1)
    for y in range(6, 128, 14):
        rect(img, 0, y, 128, 2, col("DarkWood"), 0.7)
    for _ in range(7):
        blob(img, rng.integers(0, 128), rng.integers(0, 128), rng.integers(6, 20), rng.integers(6, 20), col("Stain"), 0.7)
    return img


ALL = {
    "Truck_Metal": truck_metal, "Truck_Floor": truck_floor, "Truck_Board": truck_board, "Truck_Red": truck_red,
    "Entrance_Stone": entrance_stone, "Entrance_Fence": entrance_fence, "Entrance_Lantern": entrance_lantern,
}


def build(folder):
    os.makedirs(folder, exist_ok=True)
    for name, fn in ALL.items():
        arr = np.clip(fn(), 0, 1).astype(np.float32)
        h, w = arr.shape[:2]
        im = bpy.data.images.new(name, w, h, alpha=False)
        rgba = np.ones((h, w, 4), dtype=np.float32)
        rgba[..., :3] = arr
        im.pixels.foreach_set(rgba.ravel())
        im.filepath_raw = os.path.join(folder, name + ".png")
        im.file_format = "PNG"
        im.save()
