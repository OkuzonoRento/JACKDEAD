# ================================================================ 各部屋
ROOMS = []


def room(chip, key, title, **kw):
    def register(fn):
        ROOMS.append((chip, key, title, kw, fn))
        return fn
    return register


# ---- + 十字路 ----
@room("+", "A", "荷物の十字廊下")
def _(r):
    r.shelf(6.6, 2.75, 0); r.crates(-6.8, -2.35); r.barrel(-2.55, 6.4); r.barrel(-2.45, 7.55)
    r.long_table(2.15, -6.4, 90, cloth=None); r.byobu(-6.2, 2.3, 0, 3)
    for p in [(0, 0), (6.5, -1.5), (-1.5, -6.5)]:
        r.lantern(*p)
    r.noren(0, 9.4); r.frame(-2.9, -7.2, -90); r.bags(2.4, 7.6); r.decay(4)


@room("+", "B", "床が抜けた十字路", pits=[(-1.67, -1.67, 1.67, 1.67)])
def _(r):
    r.crates(6.7, -2.3, 12); r.shelf(-6.4, 2.75, 0, w=2.0); r.barrel(2.5, 6.8)
    r.long_table(-2.15, -6.6, 90); r.lantern(2.4, 2.4, mat="RedCloth"); r.lantern(-6.8, -1.2)
    r.noren(-9.4, 0, 90); r.drag(-8.6, -0.7, 6); r.decay(5, 34, 5)


@room("+", "C", "祭りの名残", floor="Tatami")
def _(r):
    r.mikoshi(0, 0)
    r.counter(6.6, 2.55, 0, 3.0, top="RedCloth"); r.counter(-6.6, -2.55, 0, 3.0, top="BlueCloth")
    r.counter(-2.55, 6.6, 90, 3.0, top="OldGold"); r.stone_lantern(2.55, -5.2); r.stone_lantern(2.55, -8.2)
    for x in (-8, -5.5, 5.5, 8):
        r.lantern(x, 0, mat="RedCloth" if x < 0 else "Paper")
    for y in (-7, 7):
        r.lantern(0, y, mat="RedCloth")
    r.noren(6.6, 2.0, 0, "Curtain"); r.pot(-2.5, -7.6); r.pot(2.4, 7.8, True); r.decay(3, 40)


# ---- - 直線 ----
@room("-", "A", "荷物の廊下")
def _(r):
    r.crates(-6.2, -2.25); r.long_table(-1.6, 2.15); r.crates(2.4, -2.25, -8, False); r.shelf(6.4, 2.75)
    r.barrel(8.3, -2.4)
    for x in (-6, 0, 6):
        r.lantern(x, 0)
    r.frame(-5.5, 3.0); r.bags(4.6, -2.3); r.decay(3, 22, 3)


@room("-", "B", "抜けた廊下", pits=[(-5.0, -3.34, 0, 0), (1.67, 0, 6.67, 3.34)])
def _(r):
    r.shelf(-7.6, 2.75, 0, w=1.8); r.crates(8.0, -2.3)
    r.lantern(0.8, 0, mat="RedCloth"); r.lantern(-8, -1)
    r.drag(-9.0, 1.4, -3, 6); r.decay(3, 20, 2)


@room("-", "C", "浴衣の廊下", floor="Tatami", wall="WoodWall")
def _(r):
    r.byobu(-5.6, -1.7, 4, 4); r.byobu(0.4, 1.7, -3, 4); r.toppled_table(5.7, -1.5, 8)
    for x, y in [(-8.0, 2.5), (-3.0, 2.55), (3.6, 2.45), (7.6, -2.5)]:
        r.yukata(x, y)
    r.noren(-9.4, 0, 90, "Curtain"); r.noren(9.4, 0, 90, "Curtain"); r.drag(-2.5, -0.4, 5)
    r.cushion(8.4, 0.4, 20); r.decay(2, 24, 2)


# ---- -O 行き止まりの大部屋 ----
@room("-O", "A", "宴会場", floor="Tatami")
def _(r):
    r.long_table(6.0, -8.45); r.long_table(-6.9, 2.05); r.long_table(-2.1, -6.4, 90)
    r.toppled_table(1.9, 7.2, 18); r.pillar(2.4, 2.2); r.pillar(2.4, -1.9); r.byobu(6.6, 3.2)
    for i in range(5):
        x = 3.3 + i * 1.35
        if i != 3:
            r.cushion(x, -2.9, r.rng.uniform(-6, 6)); r.tray(x, -3.75)
        r.cushion(x, -6.35, r.rng.uniform(-6, 6)); r.tray(x, -5.5)
    r.cushion(7.9, -1.9, 37)
    r.stage(6.75, 7.0, 5.9, 5.4); r.curtain(6.4, 9.55, 0, 12); r.curtain(9.55, 6.9, 90, 11); r.cushion(6.6, 6.9, 33)
    for i in range(4):
        r.cushion(-2.2, 2.8 + i)
    r.yukata(-1.9, 8.6); r.drag(-8.8, -1.2, -4, 9); r.decay(4, 44)


@room("-O", "B", "客室", floor="Tatami")
def _(r):
    r.partition(5.2, -3.4, 0, 3.4); r.partition(5.2, 3.4, 0, 3.4); r.partition(1.6, 6.9, 90, 3.6)
    for x, y, a, m in [(8.2, 0.2, 0, False), (5.9, 0.1, 4, False), (3.6, -0.3, -9, True)]:
        r.futon(x, y, a, m)
    r.futon(5.4, 7.0, 90, True); r.futon(5.6, -7.2, 87)
    r.shelf(8.0, 9.45, 0, w=2.6); r.shelf(-2.75, -7.0, 90, w=2.6); r.long_table(8.5, -7.0, 90)
    r.long_table(-7.0, -2.1, 0, cloth="BlueCloth")
    r.counter(-0.2, 9.3, 0, 2.0, 0.8); r.deco.box((-0.2, 9.3, 1.55), (1.0, 0.6, 0.65), "BlackLacquer")
    r.deco.box((-0.2, 8.98, 1.55), (0.75, 0.02, 0.48), "NeonCyan")
    r.byobu(0.6, -5.0, 40, 3); r.cushion(-0.2, 7.2, 12, "BlueCloth"); r.tray(5.0, 5.3, 20)
    r.pot(9.0, -1.8); r.yukata(-6.4, 2.6); r.decay(4, 36)


@room("-O", "C", "抜けた蔵", wall="WoodWall", pits=[(1.67, -3.34, 6.67, 3.34)])
def _(r):
    for x in (0.2, 3.6, 7.0):
        r.shelf(x, 6.2, 0); r.shelf(x, -6.2, 180)
    r.shelf(9.45, 0, -90, w=3.0); r.crates(-2.2, 8.3); r.crates(-2.2, -8.3, 30); r.barrel(8.8, 8.8); r.barrel(7.6, 9.0); r.barrel(8.9, -8.8)
    r.long_table(-6.6, -2.1, 0, cloth=None); r.crates(-8.4, 2.2, 8, False)
    r.lantern(0, 0, mat="RedCloth"); r.pot(8.2, 3.2, True); r.bags(-1.0, 4.2, 4); r.drag(-9.0, 0.8, -2, 9); r.decay(5, 40, 5)


# ---- = 三方向の広間 ----
@room("=", "A", "ロビー")
def _(r):
    r.counter(0, -2.25, 0, 5.4, 1.0); r.shelf(-5.8, -2.75, 180, w=2.4); r.shelf(5.8, -2.75, 180, w=2.4)
    r.pillar(-3.8, 4.2, 0.6); r.pillar(3.8, 4.2, 0.6)
    for x in (-6.9, 6.9):
        r.counter(x, 6.6, 0, 2.8, 0.35, body="RedCloth")
        r.deco.box((x, 5.95, 0.3), (2.8, 0.9, 0.6), "RedCloth")
    r.long_table(-7.4, 8.6, 0); r.stone_lantern(8.6, 8.6)
    r.stool(-1.2, -0.9, True); r.stool(1.6, -1.0); r.pot(8.8, 4.0); r.pot(-8.9, 4.4, True)
    r.frame(-5.6, 9.7); r.frame(6.2, 9.7, 0, -14); r.noren(0, 9.4); r.lantern(-3.8, 1.0); r.lantern(3.8, 1.0)
    r.deco.box((0, 3.5, 0.012), (3.2, 7.0, 0.008), "Curtain"); r.decay(4, 40)


@room("=", "B", "ゲームコーナー", wall="WoodWall")
def _(r):
    for i in range(4):
        x = 4.2 + i * 1.45
        r.cabinet(x, 9.2, 0, "NeonCyan", i == 2); r.cabinet(x, 5.0, 180, "NeonRed", i == 0)
        r.stool(x, 7.9, i == 1)
    for i in range(4):
        r.cabinet(-8.8 + i * 1.45, -2.65, 180, "NeonYellow" if i % 2 else "NeonCyan", i == 3)
        r.stool(-8.8 + i * 1.45, -1.4, i == 2)
    r.counter(-8.7, 6.4, 90, 3.6); r.crates(-5.0, 8.7); r.long_table(5.4, -2.1, 0, cloth="BlueCloth")
    r.cabinet(-0.4, 3.6, 25, "NeonRed"); r.cabinet(0.8, 3.0, 205, "NeonCyan", True)
    for x, m in [(-6.5, "NeonRed"), (6.4, "NeonYellow")]:
        r.deco.box((x, 9.72, 3.0), (2.6, 0.05, 0.45), m)
    r.lantern(-3.5, 5.5, mat="RedCloth"); r.bags(-3.6, -2.4, 3); r.decay(3, 46)


@room("=", "C", "抜けた厨房", floor="Tile", wall="TileWall", pits=[(-8.34, 5.0, -5.0, 8.34), (5.0, 1.67, 6.67, 6.67)])
def _(r):
    r.counter(-6.6, -2.5, 0, 4.8, 1.0, top="Metal", body="Metal"); r.counter(6.6, -2.5, 0, 4.8, 1.0, top="Metal", body="Metal")
    r.counter(0, 4.4, 0, 4.2, 1.4, top="Metal"); r.counter(9.2, 7.0, 90, 3.8, 0.9, top="Metal", body="Rust")
    r.shelf(-5.4, 9.45, 0, w=2.4); r.shelf(6.2, 9.45, 0, w=2.4); r.long_table(-1.6, -1.9, 0, cloth=None)
    r.barrel(-9.0, 9.0); r.barrel(8.9, 9.0)
    for x in (-1.4, -0.5, 0.5, 1.4):
        r.deco.prism((x, 4.4, 1.4), 0.28, 0.34, r.rng.choice(["Metal", "Rust"]), n=8, r_top=0.34)
        r.deco.box((x, 4.4, 2.9), (0.05, 0.05, 0.5), "BlackLacquer"); r.deco.prism((x, 4.4, 2.55), 0.22, 0.1, "Rust", n=8)
    r.water_patch(2.6, 1.0, 1.8, 1.1, 24); r.pot(-3.8, 8.2, True); r.pot(3.2, 7.6, True); r.decay(4, 34)


# ---- _ 曲がり角 ----
@room("_", "A", "曲がり廊下")
def _(r):
    r.crates(-2.3, 2.3); r.shelf(6.6, 2.75, 0, w=2.2); r.long_table(-2.15, -6.6, 90); r.barrel(2.5, -4.4)
    r.lantern(0, 0); r.lantern(6.5, -1); r.lantern(-1, -6.5); r.frame(-3.0, -2.4, -90); r.frame(3.4, 3.0)
    r.pot(8.8, -2.5); r.decay(3, 18, 2)


@room("_", "B", "納戸", wall="WoodWall")
def _(r):
    r.long_table(-2.15, 1.35, 90, cloth=None); r.shelf(2.2, 2.75, 0, w=2.6); r.shelf(6.6, 2.75, 0, w=2.6)
    r.shelf(-2.75, -6.2, 90, w=2.6); r.crates(2.4, -7.2, 20); r.crates(8.2, -2.35, -6, False); r.barrel(2.6, -4.6)
    r.bags(-1.8, -8.2, 4); r.bags(5.0, -2.4, 3); r.lantern(0.5, -0.5, mat="RedCloth"); r.decay(4, 30, 4)


@room("_", "C", "角の落とし穴", pits=[(-3.34, 0, 0, 3.34)])
def _(r):
    r.shelf(6.4, 2.75, 0, w=2.2); r.crates(-2.3, -7.4); r.barrel(8.6, -2.4)
    r.lantern(-1.4, 1.4, mat="RedCloth"); r.noren(0, -9.4); r.drag(0.4, -6.5, 100, 6); r.decay(3, 20, 3)


# ---- f 三叉路 ----
@room("f", "A", "三叉路", floor="Tatami")
def _(r):
    r.shelf(-2.75, 0, 90, w=3.0); r.byobu(0.9, 5.2, 90, 3); r.crates(-2.3, -6.6); r.long_table(6.4, 2.15)
    r.stone_lantern(-2.4, 7.8); r.lantern(0, 0); r.lantern(6.5, -1.2); r.noren(9.4, 0, 90)
    r.pot(-2.6, 3.4); r.cushion(0.8, -2.0, 24); r.decay(4, 24, 3)


@room("f", "B", "洗面所", floor="Tile", wall="TileWall")
def _(r):
    for y in (-6.6, -2.2, 2.2, 6.6):
        r.counter(-2.65, y, 90, 3.2, 0.8, top="Tile", body="Stone")
        for dy in (-0.8, 0.8):
            r.deco.prism((-2.6, y + dy, 1.23), 0.3, 0.05, "Water", n=8)
            r.deco.box((-3.08, y + dy, 2.0), (0.05, 0.9, 1.0), "Metal" if r.rng.random() < 0.6 else "Void")
    r.partition(2.0, 6.4, 90, 2.4, False); r.long_table(6.4, 2.15, 0, cloth="PaleCloth"); r.barrel(8.6, -2.4)
    for p in [(-0.6, -5.4), (0.9, 1.4), (4.8, -1.0)]:
        r.water_patch(p[0], p[1], r.rng.uniform(1.2, 2.2), r.rng.uniform(0.7, 1.3), r.rng.uniform(0, 180))
    for p in [(1.4, -7.6), (-0.9, 0.2), (2.4, 3.0)]:
        r.stool(*p, True)
    r.noren(0, -9.4, 0, "Curtain"); r.decay(3, 14, 2)


@room("f", "C", "落ちた床板", pits=[(-3.34, -1.67, 0, 5.0)])
def _(r):
    r.shelf(-2.75, -5.4, 90, w=2.2); r.shelf(-2.75, 7.8, 90, w=2.0); r.crates(6.8, -2.3); r.barrel(2.5, 7.8)
    r.long_table(6.4, 2.15, 0, cloth=None); r.lantern(1.6, 1.6, mat="RedCloth"); r.drag(1.8, -7.8, 92, 8); r.decay(4, 26, 4)


# ---- G 全面の大部屋 ----
@room("G", "A", "大浴場", floor="Tile", wall="TileWall")
def _(r):
    r.bath(0, 0, 8.4, 6.4)
    for sx in (-1, 1):
        for sy in (-1, 1):
            r.counter(sx * 7.0, sy * 9.3, 0, 4.4, 0.7, top="Tile", body="Stone")
            for k in (-1.4, 0, 1.4):
                r.stool(sx * 7.0 + k, sy * 8.3, r.rng.random() < 0.4)
                r.deco.box((sx * 7.0 + k, sy * 9.74, 1.9), (0.8, 0.04, 0.9), "Metal" if r.rng.random() < 0.6 else "Void")
            r.partition(sx * 7.0, sy * 4.2, 0, 2.8, False)
            r.pillar(sx * 5.4, sy * 5.4, 0.55)
    r.long_table(-8.4, -6.4, 90, cloth="PaleCloth"); r.stone_lantern(-5.6, 1.8); r.noren(0, 9.4, 0, "Curtain"); r.noren(0, -9.4)
    for p in [(5.6, 1.2), (-1.8, -5.6), (3.4, 6.4), (-6.8, 6.2)]:
        r.water_patch(p[0], p[1], r.rng.uniform(1.4, 2.6), r.rng.uniform(0.8, 1.6), r.rng.uniform(0, 180))
    r.drag(4.6, -2.6, -35, 5); r.decay(4, 18, 3)


@room("G", "B", "荒れた中庭", floor="Soil", wall="WoodWall")
def _(r):
    r.water_patch(-2.6, 2.0, 4.6, 3.2, 14); r.water_patch(-1.0, 1.0, 2.8, 2.6, -30)
    for p in [(-5.0, 1.0), (-4.2, 3.8), (-0.4, 4.0), (0.6, 0.0), (-2.6, -0.2)]:
        r.deco.lump((p[0], p[1], 0.2), r.rng.uniform(0.5, 0.9), r.rng.uniform(0.4, 0.8), r.rng.uniform(0.3, 0.6), "Rubble")
    r.tree(3.4, -2.8); r.tree(-6.4, -6.2); r.stone_lantern(2.6, 3.6); r.stone_lantern(-4.4, -2.4); r.stone_lantern(6.4, 6.4)
    for sx in (-1, 1):
        for sy in (-1, 1):
            r.pillar(sx * 3.9, sy * 8.0, 0.4); r.pillar(sx * 8.0, sy * 3.9, 0.4)
            r.deco.box((sx * 6.7, sy * 8.9, 0.03), (6.0, 1.7, 0.05), "Plank")
            r.deco.box((sx * 8.9, sy * 5.8, 0.03), (1.7, 4.4, 0.05), "Plank2")
    r.byobu(6.8, -6.8, -40, 3); r.long_table(-7.0, 7.2, 0, cloth=None); r.crates(8.4, -8.4)
    for _i in range(22):
        x, y = r.rng.uniform(-8, 8), r.rng.uniform(-8, 8)
        r.tuft(x, y)
    r.decay(4, 12, 3)


@room("G", "C", "崩れた大広間", floor="Tatami", pits=[(-5.0, -5.0, 5.0, 0), (-5.0, 1.67, 5.0, 5.0)])
def _(r):
    for sx in (-1, 1):
        for sy in (-1, 1):
            r.pillar(sx * 5.7, sy * 5.7, 0.6)
    r.long_table(-7.0, -8.4); r.long_table(7.0, 8.4); r.toppled_table(8.0, -6.6, 52); r.byobu(-7.6, 6.6, 48)
    for i in range(5):
        r.cushion(-8.6, -5.0 + i * 0.1 - 0.9 * i * 0.3, r.rng.uniform(-30, 30)); r.tray(6.0 + i * 0.7, -8.7, r.rng.uniform(-30, 30))
    r.stage(0, 8.4, 6.4, 2.8); r.lantern(-3, 0.8, mat="RedCloth"); r.lantern(3, 0.8, mat="RedCloth")
    r.lantern(0, 6.5); r.shelf(-9.45, 6.0, 90); r.drag(-8.6, 0.8, 0, 4); r.decay(5, 52, 6)


# ---- O 回廊 ----
@room("O", "A", "回廊", inner="wall")
def _(r):
    r.crates(-8.4, 8.4); r.barrel(8.8, 8.8); r.barrel(8.9, 7.6); r.long_table(7.0, -8.4); r.shelf(-9.45, -6.6, 90)
    r.shelf(6.6, 9.45); r.byobu(-6.6, -4.4, 0, 3); r.crates(4.4, 4.4, 15, False)
    for sx in (-1, 1):
        for sy in (-1, 1):
            r.lantern(sx * 6.6, sy * 6.6, mat="RedCloth" if sx * sy > 0 else "Paper")
    for a, (x, y) in enumerate([(0, 3.55), (3.55, 0), (0, -3.55), (-3.55, 0)]):
        r.frame(x, y, [180, 90, 0, -90][a], r.rng.uniform(-12, 12))
    r.yukata(-8.6, -8.4, 45); r.drag(-6.5, -7.0, 8); r.decay(5, 34, 4)


@room("O", "B", "吹き抜けの回廊", inner="rail")
def _(r):
    r.shelf(-9.45, 6.6, 90); r.long_table(-7.0, -8.4); r.crates(8.4, 8.4); r.barrel(8.8, -8.8)
    for sx in (-1, 1):
        for sy in (-1, 1):
            r.pillar(sx * 3.75, sy * 3.75, 0.45); r.lantern(sx * 6.6, sy * 6.6)
    r.byobu(6.6, -4.6, 0, 3); r.pot(-8.8, -3.6); r.pot(3.8, 8.8, True); r.noren(0, 9.4); r.decay(4, 30, 4)


@room("O", "C", "崩れた回廊", inner="open", pits=[(6.67, 6.67, 10, 10), (-6.67, -6.67, -3.34, -3.34), (3.34, -10, 5.0, -6.67)])
def _(r):
    r.crates(-8.4, 8.4); r.shelf(-9.45, -6.4, 90); r.long_table(-5.4, 8.4, 0, cloth=None); r.barrel(8.8, -8.8)
    r.toppled_table(6.2, -4.6, -24)
    r.lantern(-6.6, 0, mat="RedCloth"); r.lantern(6.6, 0, mat="RedCloth"); r.drag(-7.6, 4.8, -78, 6); r.decay(5, 30, 6)


