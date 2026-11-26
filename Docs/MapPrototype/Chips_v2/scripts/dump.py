
# 部屋の情報（扉の向きと、明かりを置く位置）を rooms.json に書き出す
import json

out = []
for index, (chip, key, title, kw, fn) in enumerate(ROOMS):
    r = Room(chip, seed=100 + index * 7, **kw)
    r.lights = []
    fn(r)
    pit = bool(r.pit_tiles) or r.open_center
    lights = [{"x": round(x, 3), "y": round(y, 3), "z": round(z - 0.45, 3), "kind": "red" if m == "RedCloth" else "warm"}
              for x, y, z, m in r.lights]
    # 提灯が遠いマスには、天井寄りに弱い明かりを足す
    for cx, cy in sorted(r.cells, key=lambda c: (abs(c[0]) + abs(c[1]), c)):
        x, y = cx * C, cy * C
        if all((x - l["x"]) ** 2 + (y - l["y"]) ** 2 > 7.5 ** 2 for l in lights):
            lights.append({"x": round(x, 3), "y": round(y, 3), "z": 4.5, "kind": "fill"})
    d = r.doors
    out.append({"name": f"{chip}_{key}" + ("_pit" if pit else ""), "title": title, "chip": chip,
                # 向き：Blender の -Y=上、+Y=下、+X=左、-X=右
                "top": (0, -1) in d, "bottom": (0, 1) in d, "left": (1, 0) in d, "right": (-1, 0) in d,
                "lights": lights})

with open(json_path, "w", encoding="utf-8") as f:
    json.dump({"rooms": out}, f, ensure_ascii=False, indent=1)
print("LIGHTS rooms", len(out), "total", sum(len(o["lights"]) for o in out), "max", max(len(o["lights"]) for o in out))
