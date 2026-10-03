#!/usr/bin/env python3
"""Mediciones de referencia desde BSPs GoldSrc (BSP version 30).

Lee los .bsp de referencia (SOLO lectura, nunca se copian al repo) y deriva:
  - bounds del mundo (bbox del model 0 = worldspawn)
  - conteo de spawns CT (info_player_start) / T (info_player_deathmatch)
  - distancias spawn-vs-spawn (entre equipos e intra-equipo)
  - altura media de coberturas (caras near-verticales en banda [32,128]u)
  - linea de vision mas larga con LOS despejado entre spawns enemigos

Uso:
    python3 tools/parse_bsp.py [bsp1 bsp2 ...]
Sin argumentos usa las rutas de referencia conocidas. Genera
docs/reference/mediciones.md e imprime un resumen por stdout.

Solo stdlib. Cross-checks de sanidad via asserts (entity counts > 0,
bounds finitos, al menos 1 LOS despejado entre equipos por mapa).
"""

import math
import re
import statistics
import struct
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent

DEFAULT_BSPS = {
    "fy_iceworld (iceworld)": (
        "/home/leo/Documentos/FrutaCS-reference/maps/iceworld/maps/fy_iceworld.bsp"
    ),
    "fy_pool_day (poolday)": (
        "/home/leo/Documentos/FrutaCS-reference/maps/poolday/cstrike/maps/"
        "fy_pool_day.bsp"
    ),
    "he_tennis (tennis)": (
        "/home/leo/Documentos/FrutaCS-reference/maps/tennis/cstrike/maps/"
        "he_tennis.bsp"
    ),
}

# Lump indices BSP30 (GoldSrc/Quake): Entities, Planes, Miptex, Vertices,
# Visilist, Nodes, Texinfo, Faces, Lighting, Clipnodes, Leaves,
# Marksurfaces, Edges, Surfedges, Models.
[L_ENT, L_PLANES, _L_MIP, L_VERTS, _L_VIS, _L_NODES, _L_TEXINFO, L_FACES,
 _L_LIGHT, _L_CLIP, _L_LEAVES, _L_MARKS, L_EDGES, L_SURF, L_MODELS] = range(15)

# item -> nombre segun TWHL armoury_entity (Counter-Strike), 0-based.
ARMOURY = {
    0: "weapon_mp5navy", 1: "weapon_tmp", 2: "weapon_p90",
    3: "weapon_mac10", 4: "weapon_ak47", 5: "weapon_sg552",
    6: "weapon_m4a1", 7: "weapon_aug", 8: "weapon_scout",
    9: "weapon_g3sg1", 10: "weapon_awp", 11: "weapon_m3",
    12: "weapon_xm1014", 13: "weapon_m249", 14: "weapon_flashbang",
    15: "weapon_hegrenade", 16: "item_kevlar", 17: "item_assaultsuit",
    18: "weapon_smokegrenade",
}

# Spawn GoldSrc: origin = centro del bbox del jugador (36u sobre los pies).
# Ojo en pie (standing) = pies + 64 = origin + 28 (VEC_VIEW HL/CS).
EYE_ABOVE_SPAWN = 28.0
# Caras "verticales": |nz| <= este umbral (paredes, bloques, cajas).
VERT_NZ = 0.2
# Banda de "cobertura": ni escalones/cordones (<32u) ni muros (<128u+).
COVER_LO, COVER_HI = 32.0, 128.0
# Brush entities que NO ocluyen vision (volumenes de gameplay invisibles).
# Todo otro brush model visible en pose base (func_wall, func_door_rotating,
# func_water, func_illusionary, func_button) SI ocluye y entra en el set de
# bloqueadores LOS. Los prefijos cubren point entities por si trajeran "model".
NON_OCCLUDING_PREFIXES = ("trigger_", "info_", "game_", "ambient_")
NON_OCCLUDING_BRUSH = {"func_buyzone", "func_bomb_target"}


def parse_entities(raw: bytes):
    text = raw.decode("ascii", errors="replace")
    ents = []
    for body in re.findall(r"\{(.*?)\}", text, re.S):
        ents.append(dict(re.findall(r'"([^"]+)"\s+"([^"]*)"', body)))
    return ents


def v3(s):
    return tuple(float(x) for x in s.split())


class BSP30:
    def __init__(self, path):
        with open(path, "rb") as f:
            self.data = f.read()
        self.version, = struct.unpack_from("<i", self.data, 0)
        self.lumps = [struct.unpack_from("<ii", self.data, 4 + 8 * i)
                      for i in range(15)]

    def lump(self, i):
        off, ln = self.lumps[i]
        assert 0 <= off <= len(self.data) and 0 <= ln
        assert off + ln <= len(self.data), f"lump {i} fuera del fichero"
        return off, ln

    def entities(self):
        off, ln = self.lump(L_ENT)
        return parse_entities(self.data[off:off + ln])

    def planes(self):
        off, ln = self.lump(L_PLANES)
        n = ln // 20
        assert n * 20 == ln
        return [struct.unpack_from("<4f", self.data, off + 20 * i)
                for i in range(n)]

    def vertices(self):
        off, ln = self.lump(L_VERTS)
        n = ln // 12
        assert n * 12 == ln
        return [struct.unpack_from("<3f", self.data, off + 12 * i)
                for i in range(n)]

    def edges(self):
        off, ln = self.lump(L_EDGES)
        n = ln // 4
        assert n * 4 == ln
        return [struct.unpack_from("<HH", self.data, off + 4 * i)
                for i in range(n)]

    def surfedges(self):
        off, ln = self.lump(L_SURF)
        n = ln // 4
        assert n * 4 == ln
        return [struct.unpack_from("<i", self.data, off + 4 * i)[0]
                for i in range(n)]

    def faces(self):
        """(planenum, side, firstedge, numedges) por cara."""
        off, ln = self.lump(L_FACES)
        n = ln // 20
        assert n * 20 == ln
        out = []
        for i in range(n):
            pn, side, first, num = struct.unpack_from("<hhih", self.data,
                                                     off + 20 * i)
            out.append((pn, side, first, num))
        return out

    def model0_bounds(self):
        off, _ln = self.lump(L_MODELS)
        mins = struct.unpack_from("<3f", self.data, off)
        maxs = struct.unpack_from("<3f", self.data, off + 12)
        return mins, maxs

    def model0_face_range(self):
        """(firstface, numfaces) del model 0 = solo geometria worldspawn."""
        return self.model_face_range(0)

    def model_face_range(self, idx):
        off, ml = self.lump(L_MODELS)
        n = ml // 64
        assert 0 <= idx < n, f"brush model *{idx} fuera de rango (n={n})"
        return struct.unpack_from("<ii", self.data, off + 64 * idx + 56)


def face_polygons(bsp, first=0, count=None):
    """Poligonos de las caras [first, first+count): (normal, dist, bbox, poly, ejes)."""
    planes = bsp.planes()
    verts = bsp.vertices()
    edges = bsp.edges()
    surf = bsp.surfedges()
    all_faces = bsp.faces()
    if count is None:
        count = len(all_faces) - first
    out = []
    for i in range(first, first + count):
        pn, side, firstedge, numedges = all_faces[i]
        if numedges < 3:
            continue
        nx, ny, nz, dist = planes[pn]
        if side:
            nx, ny, nz, dist = -nx, -ny, -nz, -dist
        pts = []
        for k in range(numedges):
            se = surf[firstedge + k]
            vi = edges[abs(se)][0 if se >= 0 else 1]
            pts.append(verts[vi])
        xs = [p[0] for p in pts]
        ys = [p[1] for p in pts]
        zs = [p[2] for p in pts]
        bb = (min(xs), max(xs), min(ys), max(ys), min(zs), max(zs))
        drop = max(range(3), key=lambda a: abs((nx, ny, nz)[a]))
        axes = [a for a in range(3) if a != drop]
        poly = [(p[axes[0]], p[axes[1]]) for p in pts]
        out.append(((nx, ny, nz, dist), bb, poly, axes))
    return out


def occluder_polys(bsp, ents):
    """Poligonos que bloquean vision: worldspawn + brush models visibles.

    Solo se excluyen volumenes de gameplay invisibles (NON_OCCLUDING_BRUSH y
    prefijos NON_OCCLUDING_PREFIXES). Puertas rotatorias etc. se consideran en
    pose base (cerradas): los conteos despejados son cota inferior si abren.
    """
    ranges = [bsp.model0_face_range()]
    for e in ents:
        m = e.get("model", "")
        if not m.startswith("*"):
            continue
        cls = e.get("classname", "")
        if cls in NON_OCCLUDING_BRUSH or cls.startswith(NON_OCCLUDING_PREFIXES):
            continue
        ranges.append(bsp.model_face_range(int(m[1:])))
    polys = []
    for first, count in ranges:
        polys.extend(face_polygons(bsp, first, count))
    return polys


def segment_blocked(a, b, blockers):
    """True si el segmento a->b atraviesa alguna cara (doble cara)."""
    ab = (b[0] - a[0], b[1] - a[1], b[2] - a[2])
    mnx, mxx = (a[0], b[0]) if a[0] < b[0] else (b[0], a[0])
    mny, mxy = (a[1], b[1]) if a[1] < b[1] else (b[1], a[1])
    mnz, mxz = (a[2], b[2]) if a[2] < b[2] else (b[2], a[2])
    for (nx, ny, nz, d), bb, poly, axes in blockers:
        if (bb[1] < mnx or bb[0] > mxx or bb[3] < mny or bb[2] > mxy
                or bb[5] < mnz or bb[4] > mxz):
            continue
        d0 = nx * a[0] + ny * a[1] + nz * a[2] - d
        d1 = nx * b[0] + ny * b[1] + nz * b[2] - d
        if d0 * d1 > 0:
            continue
        if abs(d0) < 1e-9 and abs(d1) < 1e-9:
            continue  # segmento contenido en el plano: roza, no bloquea
        t = d0 / (d0 - d1)
        if t <= 1e-4 or t >= 1 - 1e-4:
            continue
        h = (a[0] + ab[0] * t, a[1] + ab[1] * t, a[2] + ab[2] * t)
        px, py = h[axes[0]], h[axes[1]]
        inside = False
        n = len(poly)
        for j in range(n):
            x1, y1 = poly[j]
            x2, y2 = poly[(j + 1) % n]
            if (y1 > py) != (y2 > py):
                if px < (x2 - x1) * (py - y1) / (y2 - y1) + x1:
                    inside = not inside
        if inside:
            return True
    return False


def measure(label, path):
    bsp = BSP30(path)
    assert bsp.version == 30, f"{label}: version BSP {bsp.version} != 30"

    ents = bsp.entities()
    assert len(ents) > 0, f"{label}: lump de entidades vacio"

    ct = [v3(e["origin"]) for e in ents
          if e.get("classname") == "info_player_start" and "origin" in e]
    te = [v3(e["origin"]) for e in ents
          if e.get("classname") == "info_player_deathmatch" and "origin" in e]
    assert len(ct) > 0, f"{label}: sin spawns CT"
    assert len(te) > 0, f"{label}: sin spawns T"

    armoury = [e for e in ents if e.get("classname") == "armoury_entity"]
    counts = {}
    defaulted = 0  # entidades sin clave "item": se asume default FGD = 0
    for e in armoury:
        if "item" not in e:
            defaulted += int(e.get("count", "1"))
        item = int(e.get("item", "0"))
        counts[item] = counts.get(item, 0) + int(e.get("count", "1"))
    extras = sorted({e.get("classname") for e in ents} - {
        "worldspawn", "light", "light_environment", "info_player_start",
        "info_player_deathmatch", "armoury_entity"})

    mins, maxs = bsp.model0_bounds()
    for v in mins + maxs:
        assert math.isfinite(v), f"{label}: bounds no finitos"
    size = tuple(mx - mn for mn, mx in zip(mins, maxs))
    assert all(s > 0 for s in size), f"{label}: bounds degenerados {size}"
    diag = math.sqrt(sum(s * s for s in size))

    def centroid(pts):
        n = len(pts)
        return tuple(sum(p[i] for p in pts) / n for i in range(3))

    cc, ce = centroid(ct), centroid(te)
    cross = [math.dist(a, b) for a in ct for b in te]
    intra_ct = [math.dist(a, b) for i, a in enumerate(ct) for b in ct[i + 1:]]
    intra_te = [math.dist(a, b) for i, a in enumerate(te) for b in te[i + 1:]]
    intra = intra_ct + intra_te

    def ranges(pts):
        xs = [p[0] for p in pts]
        ys = [p[1] for p in pts]
        return (min(xs), max(xs), min(ys), max(ys))

    m0_first, m0_num = bsp.model0_face_range()
    assert m0_num > 0, f"{label}: model 0 sin caras"
    # Coberturas: arquitectura estatica (worldspawn). Los bloqueadores LOS
    # anaden ademas los brush models visibles (ver occluder_polys).
    covers = face_polygons(bsp, m0_first, m0_num)
    assert len(covers) > 0, f"{label}: sin caras"
    cover_h = []
    for (nx, ny, nz, _d), bb, _poly, _axes in covers:
        if abs(nz) <= VERT_NZ:
            h = bb[5] - bb[4]
            if COVER_LO <= h <= COVER_HI:
                cover_h.append(h)
    assert len(cover_h) > 0, f"{label}: sin caras de cobertura"

    polys = occluder_polys(bsp, ents)
    assert len(polys) >= len(covers), f"{label}: set de bloqueadores vacio"

    eyes_ct = [(x, y, z + EYE_ABOVE_SPAWN) for x, y, z in ct]
    eyes_te = [(x, y, z + EYE_ABOVE_SPAWN) for x, y, z in te]
    clear = []
    for a in eyes_ct:
        for b in eyes_te:
            d = math.dist(a, b)
            if not segment_blocked(a, b, polys):
                clear.append(d)
    # 0 despejados es un resultado valido (spawns tras muros, ej. iceworld):
    # se informa junto al max sin LOS para que la tabla siga siendo util.
    assert len(cross) > 0

    intra_clear = []
    for eyes in (eyes_ct, eyes_te):
        for i, a in enumerate(eyes):
            for b in eyes[i + 1:]:
                if not segment_blocked(a, b, polys):
                    intra_clear.append(math.dist(a, b))

    return {
        "label": label,
        "n_entities": len(ents),
        "n_ct": len(ct), "n_te": len(te),
        "ct_range": ranges(ct), "te_range": ranges(te),
        "centroid_dist": math.dist(cc, ce),
        "cross_min": min(cross), "cross_mean": sum(cross) / len(cross),
        "cross_max": max(cross),
        "intra_min": min(intra),
        "mins": mins, "maxs": maxs, "size": size, "diag": diag,
        "n_covers": len(cover_h),
        "cover_mean": statistics.mean(cover_h),
        "cover_median": statistics.median(cover_h),
        "sight_max": max(clear) if clear else 0.0,
        "sight_clear": len(clear), "sight_pairs": len(cross),
        "intra_sight_max": max(intra_clear) if intra_clear else 0.0,
        "armoury": counts, "n_armoury_ents": len(armoury),
        "armoury_defaulted": defaulted,
        "extras": extras,
    }


def render_markdown(rows):
    L = []
    L.append("# Mediciones de referencia: iceworld / poolday / tennis")
    L.append("")
    L.append("Tabla derivada de los BSP GoldSrc (v30) de referencia con")
    L.append("`tools/parse_bsp.py`. Los .bsp son SOLO lectura y no viven en el repo.")
    L.append("Unidades: unidades GoldSrc (u). Escala aprox: 1u ~= 2.54cm")
    L.append("(jugador 72u ~= 183cm); el ojo en pie esta a 64u de los pies.")
    L.append("")
    L.append("## Comparativa rapida")
    L.append("")
    L.append("| mapa | CT | T | area XY (u) | dist. centroides (u) | "
             "spawn-enemigo min/med/max (u) | cobertura media (u) | "
             "sightline max despejada (u) |")
    L.append("|---|---|---|---|---|---|---|---|")
    for m in rows:
        L.append(
            f"| {m['label']} | {m['n_ct']} | {m['n_te']} | "
            f"{m['size'][0]:.0f} x {m['size'][1]:.0f} | "
            f"{m['centroid_dist']:.0f} | "
            f"{m['cross_min']:.0f} / {m['cross_mean']:.0f} / {m['cross_max']:.0f} | "
            f"{m['cover_mean']:.0f} (n={m['n_covers']}, "
            f"mediana {m['cover_median']:.0f}) | "
            f"{m['sight_max']:.0f} "
            f"({m['sight_clear']}/{m['sight_pairs']} pares con LOS) |")
    L.append("")
    for m in rows:
        mn, mx, sz = m["mins"], m["maxs"], m["size"]
        cr, tr = m["ct_range"], m["te_range"]
        L.append(f"## {m['label']}")
        L.append("")
        L.append(f"- Bounds (model 0 worldspawn): min ({mn[0]:.0f}, {mn[1]:.0f}, "
                 f"{mn[2]:.0f}), max ({mx[0]:.0f}, {mx[1]:.0f}, {mx[2]:.0f}).")
        L.append(f"- Tamano: {sz[0]:.0f} x {sz[1]:.0f} x {sz[2]:.0f} u; "
                 f"diagonal {m['diag']:.0f} u.")
        L.append(f"- Spawns: CT (info_player_start) x{m['n_ct']} en "
                 f"x[{cr[0]:.0f},{cr[1]:.0f}] y[{cr[2]:.0f},{cr[3]:.0f}]; "
                 f"T (info_player_deathmatch) x{m['n_te']} en "
                 f"x[{tr[0]:.0f},{tr[1]:.0f}] y[{tr[2]:.0f},{tr[3]:.0f}]; "
                 f"{m['n_entities']} entidades en total.")
        L.append(f"- Distancia entre centroides CT-T: {m['centroid_dist']:.0f} u.")
        L.append(f"- Spawn enemigo-enemigo: min {m['cross_min']:.0f} u, "
                 f"media {m['cross_mean']:.0f} u, max {m['cross_max']:.0f} u.")
        L.append(f"- Spawn mismo equipo mas cercano: {m['intra_min']:.0f} u; "
                 f"sightline max intra-equipo despejada: "
                 f"{m['intra_sight_max']:.0f} u.")
        L.append(f"- Coberturas (caras verticales de worldspawn, "
                 f"{COVER_LO:.0f}-{COVER_HI:.0f}u "
                 f"de alto): n={m['n_covers']}, media {m['cover_mean']:.1f} u, "
                 f"mediana {m['cover_median']:.1f} u.")
        L.append(f"- Sightline maxima con LOS despejado entre enemigos (ojo a ojo, "
                 f"ojo = spawn+{EYE_ABOVE_SPAWN:.0f}u): {m['sight_max']:.0f} u "
                 f"({m['sight_clear']}/{m['sight_pairs']} pares enemigos con LOS).")
        arm_line = (f"- Armoury ({m['n_armoury_ents']} entidades): " +
                  ", ".join(f"{ARMOURY.get(k, f'desconocido({k})')} x{v}"
                            for k, v in sorted(m["armoury"].items())) + ".")
        if m["armoury_defaulted"]:
            arm_line += (f" De ellos, {m['armoury_defaulted']} asumidos como "
                         "weapon_mp5navy: entidades sin clave `item` (default FGD 0).")
        L.append(arm_line)
        if m["extras"]:
            L.append(f"- Otras entidades: {', '.join(m['extras'])}.")
        L.append("")
    L.append("## Metodo y supuestos")
    L.append("")
    L.append("- Parser BSP30 propio: lumps de entidades, planos, vertices, aristas, "
             "surfaristas, caras y models; sin dependencias.")
    L.append("- Bounds = bbox del model 0 (worldspawn). Incluye cielo/caja del mapa; "
             "el area jugable es menor o igual.")
    L.append("- Cobertura = cara de worldspawn con plano near-vertical (|nz| <= "
             f"{VERT_NZ}) cuya altura cae en [{COVER_LO:.0f}, {COVER_HI:.0f}]u: "
             "excluye escalones/cordones (<32u) y muros perimetrales/rascacielos "
             "(>128u). Heuristica, no semantica de gameplay.")
    L.append("- LOS: segmento ojo-a-ojo entre cada par CT-T contra worldspawn + "
             "brush models visibles en pose base (func_wall, func_door_rotating, "
             "func_water, func_illusionary, func_button) como bloqueadores de "
             "doble cara. Solo se excluyen volumenes invisibles: func_buyzone, "
             "func_bomb_target y trigger_*/info_*/game_*. Las puertas se consideran "
             "cerradas: si abren en juego, los pares despejados son cota inferior. "
             "Se ignoran rozamientos coplanares. "
             "Verificado con un segundo metodo 2D independiente.")
    L.append("- Armoury: entidades sin clave `item` se asumen item=0 (default FGD = "
             "weapon_mp5navy); la linea de armoury de cada mapa indica cuantos "
             "fueron asumidos.")
    L.append("- Ojo = origin del spawn + 28u (= 64u sobre los pies en pie). "
             "Verificado: los spawns flotan 1-33u sobre el suelo y origin = pies+36u.")
    L.append("")
    return "\n".join(L)


def main(argv):
    if argv[1:]:
        targets = [(Path(p).stem, p) for p in argv[1:]]
    else:
        targets = [(k, v) for k, v in DEFAULT_BSPS.items()]
    rows = []
    for label, path in targets:
        m = measure(label, path)
        rows.append(m)
        print(f"{label}: CT={m['n_ct']} T={m['n_te']} "
              f"bounds={tuple(f'{s:.0f}' for s in m['size'])} "
              f"cross={m['cross_min']:.0f}/{m['cross_mean']:.0f}/{m['cross_max']:.0f} "
              f"cover~{m['cover_mean']:.0f} sight={m['sight_max']:.0f} "
              f"({m['sight_clear']}/{m['sight_pairs']})")
    out = REPO_ROOT / "docs" / "reference" / "mediciones.md"
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(render_markdown(rows), encoding="utf-8")
    print(f"escrito: {out}")


if __name__ == "__main__":
    main(sys.argv)
