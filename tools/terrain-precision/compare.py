"""Compare terrain sampling over identical world-space fixtures. Requires Python 3 and NumPy.

The extractor reads the project's C# regular-cell tables. It creates float32 geometry
without Unity normals, shading, welding, MeshCollider cooking or world streaming.
The counts describe this offline fixed-volume extraction, not Player frame times.
"""

import argparse
import json
import re
from pathlib import Path

import numpy as np


ROOT = Path(__file__).resolve().parents[2]
CORNER = np.array([(i & 1, i >> 1 & 1, i >> 2) for i in range(8)])
SPHERE_RADIUS = 0.65
INSET = 0.20


def read_tables():
    source = (ROOT / "Assets/Reol2/Voxels/RegularCellTables.cs").read_text(encoding="utf-8-sig")

    def body(name):
        return source.split(name + " =", 1)[1].split("};", 1)[0]

    def rows(name):
        return [[int(v.strip(), 0) for v in row.split(",") if v.strip()]
                for row in re.findall(r"new byte\[\]\s*\{([^}]*)\}", body(name))]

    classes = [int(v, 16) for v in re.findall(r"0x[0-9a-fA-F]+", body("Classes"))]
    edges, triangles = rows("Edges"), rows("Triangles")
    assert len(classes) == len(edges) == 256 and len(triangles) == 16
    return [(np.array(edges[i], dtype=int), triangles[c]) for i, c in enumerate(classes)]


TABLES = read_tables()


class Grid:
    def __init__(self, origin, size, spacing):
        self.origin = np.array(origin, dtype=np.float64)
        self.size = np.array(size)
        self.spacing = spacing
        self.cells = np.rint(self.size / spacing).astype(int)
        assert np.allclose(self.cells * spacing, self.size)
        # Include one density sample beyond both corners as in the runtime normal halo.
        axes = [np.arange(-1, n + 2, dtype=np.float32) * spacing + np.float32(start)
                for n, start in zip(self.cells, self.origin)]
        self.x, self.y, self.z = np.meshgrid(*axes, indexing="ij", sparse=True)

    def mesh(self, field):
        density = np.broadcast_to(field, tuple(self.cells + 3)).astype(np.float32)
        density[density == 0] = 0.0001  # ContinentField.NonzeroDensity
        corner_values = []
        case = np.zeros(self.cells, dtype=np.uint8)
        for bit, offset in enumerate(CORNER):
            start = offset + 1
            slices = tuple(slice(a, a + n) for a, n in zip(start, self.cells))
            values = density[slices]
            corner_values.append(values)
            case |= (values < 0).astype(np.uint8) << bit

        triangle_lists, owner_lists = [], []
        for mask in np.unique(case):
            edges, pattern = TABLES[mask]
            if not pattern:
                continue
            cells = np.argwhere(case == mask)
            indices = tuple(cells.T)
            vertices = []
            for edge in edges:
                a, b = edge >> 4, edge & 15
                va, vb = corner_values[a][indices], corner_values[b][indices]
                t = va / (va - vb)
                points = (cells + CORNER[a]).astype(np.float32)
                points[:, (a ^ b) >> 1] += t
                vertices.append(points * self.spacing + self.origin)
            tri = np.stack(vertices, axis=1)[:, pattern].reshape(-1, 3, 3)
            triangle_lists.append(tri)
            owner_lists.append(np.repeat(cells[:, 0] * self.cells[2] + cells[:, 2], len(pattern) // 3))

        tris = np.concatenate(triangle_lists) if triangle_lists else np.empty((0, 3, 3))
        owners = np.concatenate(owner_lists) if owner_lists else np.empty(0, dtype=int)
        counts = dict(halo_samples=int(density.size), classified_cells=int(case.size),
                      mixed_cells=int(np.count_nonzero((case != 255) & (case != 0))), triangles=len(tris))
        return Mesh(self, tris, owners, counts)


class Mesh:
    def __init__(self, grid, triangles, owners, counts):
        self.grid = grid
        order = np.argsort(owners)
        self.owners = owners[order]
        self.triangles = triangles[order]
        self.counts = counts

    def vertical_hits(self, xz):
        """Lowest/highest intersections of a vertical probe with the actual triangle faces."""
        g = self.grid
        cells = np.floor((xz - g.origin[[0, 2]]) / g.spacing).astype(int)
        assert np.all((cells >= 0) & (cells < g.cells[[0, 2]]))
        columns = cells[:, 0] * g.cells[2] + cells[:, 1]
        start = np.searchsorted(self.owners, columns)
        lengths = np.searchsorted(self.owners, columns, side="right") - start
        probe = np.repeat(np.arange(len(columns)), lengths)
        first = np.cumsum(lengths) - lengths
        tri = self.triangles[np.repeat(start - first, lengths) + np.arange(len(probe))]
        a, b, c = (tri[:, i] for i in range(3))
        u, v = b - a, c - a
        dx, dz = (xz[probe] - a[:, [0, 2]]).T
        det = u[:, 0] * v[:, 2] - u[:, 2] * v[:, 0]
        with np.errstate(divide="ignore", invalid="ignore"):
            s = (dx * v[:, 2] - dz * v[:, 0]) / det
            t = (dz * u[:, 0] - dx * u[:, 2]) / det
        valid = (np.abs(det) > 1e-12) & (s >= -1e-8) & (t >= -1e-8) & (s + t <= 1 + 1e-8)
        height = a[valid, 1] + s[valid] * u[valid, 1] + t[valid] * v[valid, 1]
        lo, hi = np.full(len(columns), np.inf), np.full(len(columns), -np.inf)
        np.minimum.at(lo, probe[valid], height)
        np.maximum.at(hi, probe[valid], height)
        return lo, hi


def height_error(mesh, xz, reference):
    _, height = mesh.vertical_hits(xz)
    assert np.all(np.isfinite(height)), "Missing ground under a probe"
    errors = np.abs(height - reference)
    return {"probes": len(xz), "p95_mm": round(float(np.percentile(errors, 95)) * 1000, 4),
            "max_mm": round(float(errors.max()) * 1000, 4)}


def grade_comparison(spacing):
    grid = Grid((-2, -2, -3), (24, 6, 6), spacing)
    # Current one-metre fill applied to the right half of a four-metre-wide strip.
    # Each rounded-box edit only visits the samples within its 1m logical cell.
    density = np.broadcast_to(grid.y, tuple(grid.cells + 3)).copy()
    density[density == 0] = 0.0001
    edge = round(1 / spacing) + 1
    axis = np.arange(edge, dtype=np.float32) * spacing - 0.5
    offset = np.stack(np.meshgrid(axis, axis, axis, indexing="ij"), axis=-1)
    delta = np.abs(offset) - 0.55 + 0.08
    stamp = np.linalg.norm(np.maximum(delta, 0), axis=-1) + np.minimum(delta.max(axis=-1), 0) - 0.08
    for x in range(10, 22):
        for z in range(-2, 2):
            index = np.rint((np.array([x, 0, z]) - grid.origin) / spacing).astype(int) + 1
            box = tuple(slice(i, i + edge) for i in index)
            density[box] = np.minimum(density[box], stamp)

    # Points straddle the join of the two plateaus; sample every 5 cm and over 0.5m intervals.
    x = np.linspace(0, 20, 401)
    xz = np.column_stack([x, np.full_like(x, 0.137)])
    results = {}
    for name, field in (("integer_boxes", density), ("continuous_plane", grid.y - grid.x / 20)):
        mesh = grid.mesh(field)
        result = height_error(mesh, xz, x / 20)
        _, heights = mesh.vertical_hits(xz[::10])
        result["max_rise_per_half_m_cm"] = round(float(np.max(np.diff(heights))) * 100, 4)
        results[name] = result
    return results


def hill_height(x, z, phases):
    return (2.8 + 1.4 * np.exp(-((x - 3.9) ** 2 + (z - 4.1) ** 2) / 5.0)
            + 0.35 * np.sin(1.2 * x + phases[0]) * np.cos(0.95 * z + phases[1]))


def sphere_edit(grid, x, z):
    h = 3.0
    # The current hit plane is flat. A subtraction is inset 20 cm below its top.
    center_y = h - INSET
    distance = np.sqrt((grid.x - x) ** 2 + (grid.y - center_y) ** 2 + (grid.z - z) ** 2)
    return np.maximum(grid.y - h, SPHERE_RADIUS - distance)


def surface_fixture(seed):
    rng = np.random.default_rng(seed)
    return (rng.uniform(0.25, 7.75, size=(1024, 2)), rng.uniform(0, 2 * np.pi, size=2),
            rng.uniform(3.5, 4.0, (8, 2)))


def surface_comparison(spacing, seed):
    grid = Grid((0, 0, 0), (8, 8, 8), spacing)
    probes, phases, centers = surface_fixture(seed)
    hill = grid.mesh(grid.y - hill_height(grid.x, grid.z, phases))
    results = {"smooth_hill": height_error(hill, probes, hill_height(*probes.T, phases))}
    results["smooth_hill"]["work"] = hill.counts

    # Test the interior of each playable crater, excluding its near-vertical lip.
    # A fixed polar probe set includes the center and three rings, across 8 grid phases.
    angles = np.linspace(0, 2 * np.pi, 32, endpoint=False) + 0.017
    directions = np.column_stack([np.cos(angles), np.sin(angles)])
    offsets = np.vstack([np.zeros((1, 2))] + [directions * r for r in (0.15, 0.32, 0.50)])
    bottom = 3.0 - INSET - np.sqrt(SPHERE_RADIUS ** 2 - (offsets ** 2).sum(axis=1))
    errors, center_depth, triangle_counts = [], [], []
    for center in centers:
        sphere = grid.mesh(sphere_edit(grid, *center))
        _, heights = sphere.vertical_hits(center + offsets)
        assert np.isfinite(heights).all()
        errors.extend(np.abs(heights - bottom))
        center_depth.append(3.0 - heights[0])
        triangle_counts.append(sphere.counts["triangles"])
    results["sphere_cut"] = dict(probes=len(errors), p95_mm=round(float(np.percentile(errors, 95)) * 1000, 3),
                                 max_mm=round(float(max(errors)) * 1000, 3),
                                 center_depth_min_m=round(float(min(center_depth)), 4),
                                 center_depth_max_m=round(float(max(center_depth)), 4),
                                 min_page_triangles=min(triangle_counts), max_page_triangles=max(triangle_counts))
    return results


def height_axis_lookup(axis, step):
    coord = axis.ravel()
    floor = np.floor(coord / step).astype(int)
    nodes = np.arange(floor.min(), floor.max() + 2) * step
    return nodes, floor - floor.min(), (coord - floor * step) / step


def height_node_comparison(seed):
    # Same continuous hill for every case, stretched to a 32m patch spanning 4x4 world pages.
    # Preserve the 0.5m extraction lattice while varying only the generator's X/Z lattice.
    rng = np.random.default_rng(seed)
    probes = rng.uniform(0.25, 31.75, size=(4096, 2))
    phases = rng.uniform(0, 2 * np.pi, size=2)

    def reference(x, z):
        return 1.6 + 3.5 * (hill_height(x / 4, z / 4, phases) - 2.8)

    grid = Grid((0, 0, 0), (32, 8, 32), 0.5)
    expected = reference(*probes.T)
    results = {}
    for step in (8, 4, 2):
        xs, ix, u = height_axis_lookup(grid.x, step)
        zs, iz, v = height_axis_lookup(grid.z, step)
        # Evaluate each distinct lattice node just once and use WorldSurface.Interpolate's diagonal.
        nodes = reference(xs[:, None], zs[None, :]).astype(np.float32)
        ix, iz = ix[:, None], iz[None, :]
        u, v = u[:, None], v[None, :]
        a, b = nodes[ix, iz], nodes[ix + 1, iz]
        c, d = nodes[ix, iz + 1], nodes[ix + 1, iz + 1]
        height = np.where(u + v <= 1, a + (b - a) * u + (c - a) * v,
                          d + (c - d) * (1 - u) + (b - d) * (1 - v))
        mesh = grid.mesh(grid.y - height[:, None, :])
        result = height_error(mesh, probes, expected)
        result["evaluated_height_nodes_including_halo"] = int(nodes.size)
        result["triangles"] = mesh.counts["triangles"]
        results[str(step)] = result
    direct = grid.mesh(grid.y - reference(grid.x, grid.z))
    results["analytic_at_each_half_m"] = height_error(direct, probes, expected)
    return results


def thin_layer_comparison(spacing):
    grid = Grid((0, 0, 0), (4, 4, 4), spacing)
    axis = np.linspace(0.53, 3.47, 12)
    xz = np.stack(np.meshgrid(axis, axis, indexing="ij"), axis=-1).reshape(-1, 2)
    result = {}
    for thickness in (0.08, 0.30):
        coverage = []
        for pitch, yaw in ((0, 0), (20, 0), (20, 35), (35, 75)):
            slope = np.tan(np.deg2rad(pitch))
            ux, uz = np.cos(np.deg2rad(yaw)), np.sin(np.deg2rad(yaw))
            horizontal = ((grid.x - 2) * ux + (grid.z - 2) * uz) * slope
            for phase in (np.arange(8) + 0.5) / 16:
                distance = (grid.y - (1.75 + phase) - horizontal) / np.hypot(1, slope)
                mesh = grid.mesh(np.abs(distance) - thickness / 2)
                low, high = mesh.vertical_hits(xz)
                coverage.append(int(np.count_nonzero(high > low + 1e-6)))
        count = len(coverage) * len(xz)
        result[f"{thickness:.2f}m"] = dict(hit_both_faces=sum(coverage), probes=count,
                                            paired_hit_percent=round(100 * sum(coverage) / count, 2),
                                            fully_missing_cases=coverage.count(0), cases=len(coverage))
    return result


def render_crater(path, seed, font_path):
    """Draw top-down probe depths on a centerline, keeping both axes at the same scale."""
    from PIL import Image, ImageDraw, ImageFont

    image = Image.new("RGB", (860, 680), "#fbfaf7")
    draw = ImageDraw.Draw(image)
    fonts = [ImageFont.truetype(font_path, size) for size in (34, 20, 17, 15)]
    ink, muted = "#232b2d", "#425154"
    center = surface_fixture(seed)[2][0]
    offsets = np.linspace(-1.0, 1.0, 1001)
    ray_xz = center + np.column_stack([offsets, np.zeros_like(offsets)])
    lateral_radius = np.sqrt(SPHERE_RADIUS ** 2 - INSET ** 2)
    target = np.zeros_like(offsets)
    interior = np.abs(offsets) < lateral_radius
    target[interior] = -INSET - np.sqrt(SPHERE_RADIUS ** 2 - offsets[interior] ** 2)
    profiles = [("理想球坑", "#273436", target)]
    for spacing, color in ((0.5, "#c45b1b"), (0.25, "#007b78")):
        grid = Grid((0, 0, 0), (8, 8, 8), spacing)
        mesh = grid.mesh(sphere_edit(grid, *center))
        _, actual = mesh.vertical_hits(ray_xz)
        assert np.all(np.isfinite(actual))
        profiles.append((f"{spacing:g} m 采样", color, actual - 3))

    scale, left, top = 350, 96, 172
    def point(x, y):
        return (left + (x + 1) * scale, top - y * scale)

    draw.text((44, 28), "同一球形挖掘的地表截面", font=fonts[0], fill=ink)
    draw.text((46, 88), "原地面为 0 m   ·   工具半径 0.65 m   ·   中心低于地面 0.20 m",
              font=fonts[1], fill=muted)
    for height in np.arange(0, -1.01, -0.25):
        draw.line([point(-1, height), point(1, height)], fill="#ced4d0", width=1)
        draw.text((81, point(0, height)[1]), f"{height:g}", font=fonts[2], fill=muted, anchor="rm")
    for x in np.arange(-1, 1.01, 0.25):
        draw.line([point(x, 0), point(x, -1)], fill="#e0e4df", width=1)
        draw.text((point(x, 0)[0], point(0, -1)[1] + 12), f"{x:g}", font=fonts[2], fill=muted, anchor="mt")
    draw.text((44, 136), "相对高度 (m)", font=fonts[2], fill=muted)
    draw.text((445, 575), "距工具中心的水平距离 (m)", font=fonts[2], fill=muted, anchor="mt")
    for i, (label, color, height) in enumerate(profiles):
        draw.line([point(x, y) for x, y in zip(offsets, height)], fill=color, width=2 if i == 0 else 4)
        x = left + i * 227
        draw.line([(x, 617), (x + 38, 617)], fill=color, width=4)
        draw.text((x + 47, 617), label, font=fonts[2], fill=ink, anchor="lm")
    draw.text((46, 650), f"离线三角网格垂直射线 · 轴向等比例 · 种子 {seed} · 8 组格内位置中的第 1 组",
              font=fonts[3], fill=muted)
    path.parent.mkdir(parents=True, exist_ok=True)
    image.save(path)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--seed", type=int, default=20261121)
    parser.add_argument("--json", type=Path, help="Also write the report to this file")
    parser.add_argument("--plot", type=Path, help="Draw the crater centerline to a PNG (requires Pillow)")
    parser.add_argument("--font", default="C:/Windows/Fonts/msyh.ttc", help="Chinese font used by --plot")
    args = parser.parse_args()
    report = {"seed": args.seed, "numpy": np.__version__, "sphere_radius_m": SPHERE_RADIUS,
              "sphere_inset_m": INSET, "spacing": {}}
    report["height_nodes_m_at_half_m_spacing"] = height_node_comparison(args.seed)
    for spacing in (0.5, 0.25):
        report["spacing"][str(spacing)] = {
            "grade_20m": grade_comparison(spacing),
            "surfaces": surface_comparison(spacing, args.seed),
            "slabs": thin_layer_comparison(spacing),
        }
    value = json.dumps(report, indent=2, allow_nan=False)
    if args.json:
        args.json.parent.mkdir(parents=True, exist_ok=True)
        args.json.write_text(value + "\n", encoding="utf-8")
    if args.plot:
        render_crater(args.plot, args.seed, args.font)
    print(value)


if __name__ == "__main__":
    main()
