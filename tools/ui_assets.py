"""Generate and cut MU-styled UI art for the client (ornate UI kit and feature icons).

A manifest (tools/ui_assets/*.json) lists *sources*: one generated image each, with the prompt used to
create it and the rules that cut it into WebP (default) or PNG assets bundled into the client (Client.Main/Assets).

    python tools/ui_assets.py generate tools/ui_assets/ornate_icons.json [--only icons_menu] [--jobs 4]
    python tools/ui_assets.py cut      tools/ui_assets/ornate_icons.json [--only icons_menu]
    python tools/ui_assets.py sheet    tools/ui_assets/ornate_icons.json
    python tools/ui_assets.py measure  Client.Main/Assets/Interface/Ornate/Controls/button.webp
    python tools/ui_assets.py measure  Client.Main/Assets/Interface/Ornate/Controls/card.webp --ring 0.13,0.5

generate  asks the Codex CLI (image_generation tool) for each source image; originals are kept in
          tools/ui_assets/.work/<manifest>/<source>.png (git-ignored) with a .log next to them.
cut       turns originals into image files under the manifest "output" folder.
sheet     writes .work/<manifest>/sheet.png: every cut asset on a dark background for a quick look.
measure   prints alpha-profile numbers used for PlateLayout / OrnateStyle constants in C#.

Manifest format:
{
  "output": "Client.Main/Assets/Interface/Ornate",   # relative to the repository root
  "format": "webp",           # webp (default: lossy q90 colour, lossless alpha, ~4x smaller) or png
  "style": "...appended to every prompt (transparency/opacity is added automatically)...",
  "sources": {
    "<source>": {
      "prompt": "...",
      "opaque": false,            # true: keep the full canvas (textures), no alpha cleanup/bbox
      "split": 4,                 # optional: number of items laid out left-to-right in the image
      "cut": [                    # one entry per output (in split order when "split" is set)
        {"out": "Icons/resume", "square": 96},
        {"out": "Controls/toggle_on", "band": [0.33, 0.76], "height": 48},
        {"out": "Controls/arrow_left", "crop": [0, 0, 0.32, 1], "height": 48},
        {"out": "Controls/button", "width": 512, "desaturate_green": true},
        [{"out": "Icons/class_elf", "square": 96}, {"out": "Emblems/class_elf", "disk": true, "square": 96}]
      ]
    }
  }
}
"quality" (WebP, default 90) can be lowered for big opaque illustrations (82 is plenty for full-screen art).
Cut steps, in order: crop (fractions x0,y0,x1,y1) -> band (vertical fractions) -> bbox -> desaturate_green ->
disk -> hollow -> resize (square | width+height | width | height).
"hollow": inset fraction; the dark inside of a frame beyond it becomes transparent (the game draws its own fill).
"tileable": true blends an opaque texture with a half-shifted copy so it repeats without seams (after hollow). A list of rules for one item writes several outputs.
"disk": cut out the inside of a medallion: the gold ring is detected (its centre is rarely the image centre because
the spikes around it are asymmetric) and the area inside it is kept as a round emblem with a soft edge, ready to be
placed into a socket of another plate (see OrnateWidgets.Card). "measure --ring" reports such sockets.
"""
import argparse
import concurrent.futures
import io
import json
import os
import shutil
import subprocess
import sys

import numpy as np
from PIL import Image

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ALPHA_NOISE = 12
WEBP_QUALITY = 90

COPY_INSTRUCTION = (
    "Use your image generation tool to create ONE image, then copy the PNG file the tool saved under "
    "{images} into the current directory as {name}.png using a single short copy command with the "
    "file path (never embed image data in a command). Do nothing else. Image: "
)
TRANSPARENT = "Isolated on a fully transparent background (real alpha channel)."
OPAQUE = "Fully opaque, fills the entire canvas edge to edge, no frame, no border."


def load_manifest(path):
    with open(path, encoding="utf-8") as f:
        manifest = json.load(f)
    work = os.path.join(REPO, "tools", "ui_assets", ".work", os.path.splitext(os.path.basename(path))[0])
    os.makedirs(work, exist_ok=True)
    return manifest, work


def selected(manifest, only):
    names = list(manifest["sources"])
    if only:
        wanted = set(only.split(","))
        unknown = wanted - set(names)
        if unknown:
            sys.exit(f"unknown sources: {', '.join(sorted(unknown))}")
        names = [n for n in names if n in wanted]
    return names


# --- generate -----------------------------------------------------------------------------------------------

def generate_one(name, source, style, work):
    codex = shutil.which("codex")
    if not codex:
        return name, "codex CLI not found on PATH"

    target = os.path.join(work, name + ".png")
    images = os.path.join(os.path.expanduser("~"), ".codex", "generated_images")
    background = OPAQUE if source.get("opaque") else TRANSPARENT
    prompt = COPY_INSTRUCTION.format(images=images, name=name) + " ".join((source["prompt"], style, background))
    for attempt in range(2):
        with open(os.path.join(work, name + ".log"), "w", encoding="utf-8") as log:
            subprocess.run([codex, "exec", "--skip-git-repo-check", "-s", "workspace-write", prompt],
                           cwd=work, stdin=subprocess.DEVNULL, stdout=log, stderr=subprocess.STDOUT, timeout=900)
        if os.path.exists(target) and os.path.getsize(target) > 0:
            return name, "ok" + (" (retry)" if attempt else "")
    return name, f"failed, see {name}.log"


def cmd_generate(args):
    manifest, work = load_manifest(args.manifest)
    names = selected(manifest, args.only)
    style = manifest.get("style", "")
    with concurrent.futures.ThreadPoolExecutor(max_workers=args.jobs) as pool:
        futures = [pool.submit(generate_one, n, manifest["sources"][n], style, work) for n in names]
        for future in concurrent.futures.as_completed(futures):
            name, status = future.result()
            print(f"{name}: {status}")


# --- cut ----------------------------------------------------------------------------------------------------

def load_source(work, name, opaque):
    path = os.path.join(work, name + ".png")
    if not os.path.exists(path):
        sys.exit(f"missing {path} - run generate first")
    image = Image.open(path).convert("RGBA")
    if opaque:
        return image
    pixels = np.array(image)
    pixels[pixels[..., 3] < ALPHA_NOISE] = 0  # drop near-invisible fringe noise
    return Image.fromarray(pixels)


def bbox(image):
    alpha = np.array(image)[..., 3]
    ys, xs = np.where(alpha > ALPHA_NOISE)
    if len(xs) == 0:
        return image
    return image.crop((xs.min(), ys.min(), xs.max() + 1, ys.max() + 1))


def split_columns(image, count, min_gap=6):
    """Split items laid out left-to-right, separated by fully transparent columns."""
    filled = (np.array(image)[..., 3] > ALPHA_NOISE).any(axis=0)
    parts, start, gap = [], None, 0
    for x, value in enumerate(filled):
        if value:
            start = x if start is None else start
            gap = 0
        elif start is not None:
            gap += 1
            if gap >= min_gap:
                parts.append((start, x - gap + 1))
                start, gap = None, 0
    if start is not None:
        parts.append((start, len(filled)))

    parts = sorted(sorted(parts, key=lambda p: p[1] - p[0], reverse=True)[:count])  # ignore stray specks
    if len(parts) != count:
        sys.exit(f"expected {count} items, found {len(parts)} - regenerate with clearer gaps")
    return [bbox(image.crop((x0, 0, x1, image.height))) for x0, x1 in parts]


def desaturate_green(image):
    """Generated metal sometimes has mossy green specks - turn them into neutral bronze."""
    pixels = np.array(image).astype(np.int16)
    r, g, b = pixels[..., 0], pixels[..., 1], pixels[..., 2]
    mask = (g > r + 8) & (g > b + 8)
    grey = (r + g + b) // 3
    pixels[mask, 0] = grey[mask] + 10
    pixels[mask, 1] = grey[mask]
    pixels[mask, 2] = grey[mask] - 10
    return Image.fromarray(np.clip(pixels, 0, 255).astype(np.uint8))


def hollow(image, inset, threshold=40):
    """Make the dark inside of a frame transparent: pixels further than `inset` (fraction of the size) from every edge
    and darker than `threshold` get alpha 0 (with a 1px soft edge), so the game can draw its own fill behind the frame."""
    pixels = np.array(image.convert("RGBA")).astype(np.int16)
    h, w = pixels.shape[:2]
    x0, y0, x1, y1 = int(w * inset), int(h * inset), int(w * (1 - inset)), int(h * (1 - inset))
    lum = pixels[..., :3].max(axis=2)
    inside = np.zeros((h, w), bool)
    inside[y0:y1, x0:x1] = True
    dark = inside & (lum < threshold)
    near = inside & (lum < threshold * 2) & ~dark
    pixels[dark, 3] = 0
    pixels[near, 3] = pixels[near, 3] // 2
    return Image.fromarray(np.clip(pixels, 0, 255).astype(np.uint8))


def tileable(image):
    """Hide the seams of an opaque texture: blend it with a copy shifted by half its size, weighted so the original
    wins in the middle and the shifted copy (whose middle is the original's edges) wins at the edges."""
    pixels = np.array(image.convert("RGBA")).astype(np.float32)
    h, w = pixels.shape[:2]
    shifted = np.roll(np.roll(pixels, h // 2, axis=0), w // 2, axis=1)
    wy = 1 - np.abs(np.linspace(-1, 1, h))[:, None]
    wx = 1 - np.abs(np.linspace(-1, 1, w))[None, :]
    weight = np.clip(np.minimum(wx, wy) * 2.2, 0, 1)[..., None]
    return Image.fromarray(np.clip(pixels * weight + shifted * (1 - weight), 0, 255).astype(np.uint8))


def gold_mask(pixels):
    r, g, b, a = (pixels[..., i].astype(np.int16) for i in range(4))
    return (a > 200) & (r > 110) & (r > b + 45) & (g > b + 20)


def _best_ring(mask, w, h, centers, radii):
    """Best (score, cx, cy, r) over candidate centres/radii, scoring the gold radial profile."""
    yy, xx = np.mgrid[0:h, 0:w]
    xx, yy = xx.ravel(), yy.ravel()
    rmin, rmax = radii
    best = (-1.0, 0, 0, rmin)
    for cx, cy in centers:
        d = np.hypot(xx - cx, yy - cy).astype(np.int32)
        counts = np.bincount(d, minlength=rmax + 8)
        golds = np.bincount(d, weights=mask, minlength=rmax + 8)
        for r in range(max(7, rmin), rmax):
            ring = golds[r:r + 3].sum() / max(1, counts[r:r + 3].sum())
            inside = golds[r - 6:r - 2].sum() / max(1, counts[r - 6:r - 2].sum())
            if ring - inside > best[0]:
                best = (ring - inside, cx, cy, r)
    return best


def find_ring(image, center=(0.5, 0.5), search=0.12, radii=(0.2, 0.48)):
    """Find a gold ring: returns (cx, cy, inner_radius) in pixels of `image`.

    Scores candidate centres by the radial profile of gold pixels: the ring is where the profile jumps from
    "not gold" (inside) to "gold" (the metal ring). Coarse search on a small copy, then refined at full size."""
    scale = min(1.0, 160 / max(image.size))
    small = image.resize((max(1, round(image.width * scale)), max(1, round(image.height * scale))), Image.LANCZOS)
    w, h = small.size
    side = min(w, h)
    span = max(2, int(side * search))
    cx0, cy0 = int(w * center[0]), int(h * center[1])
    centers = [(x, y) for y in range(cy0 - span, cy0 + span + 1) for x in range(cx0 - span, cx0 + span + 1)]
    _, cx, cy, r = _best_ring(gold_mask(np.array(small)).astype(np.float64).ravel(), w, h, centers,
                              (int(side * radii[0]), int(side * radii[1])))
    if scale == 1.0:
        return cx + 0.5, cy + 0.5, float(r)

    full = gold_mask(np.array(image)).astype(np.float64).ravel()
    fx, fy, fr, step = round(cx / scale), round(cy / scale), round(r / scale), round(1 / scale) + 1
    centers = [(x, y) for y in range(fy - step, fy + step + 1) for x in range(fx - step, fx + step + 1)]
    _, cx, cy, r = _best_ring(full, image.width, image.height, centers, (fr - step - 2, fr + step + 3))
    return cx + 0.5, cy + 0.5, float(r)


def disk(image, feather=1.5):
    """Keep only the inside of the medallion ring, as a square with a soft round alpha edge."""
    cx, cy, radius = find_ring(image)
    radius += 1  # tuck the edge just under the ring of the socket it will be placed in
    box = (round(cx - radius), round(cy - radius), round(cx + radius), round(cy + radius))
    crop = image.crop(box)
    size = crop.width
    yy, xx = np.mgrid[0:size, 0:size]
    dist = np.hypot(xx + 0.5 - size / 2, yy + 0.5 - size / 2)
    edge = np.clip((size / 2 - dist) / feather, 0, 1)
    pixels = np.array(crop).astype(np.float64)
    pixels[..., 3] *= edge
    return Image.fromarray(pixels.astype(np.uint8))


def resize(image, rule):
    if "square" in rule:
        side = max(image.size)
        canvas = Image.new("RGBA", (side, side), (0, 0, 0, 0))
        canvas.paste(image, ((side - image.width) // 2, (side - image.height) // 2))
        return canvas.resize((rule["square"], rule["square"]), Image.LANCZOS)
    w, h = rule.get("width"), rule.get("height")
    if w and h:
        return image.resize((w, h), Image.LANCZOS)
    if w:
        return image.resize((w, max(1, round(image.height * w / image.width))), Image.LANCZOS)
    if h:
        return image.resize((max(1, round(image.width * h / image.height)), h), Image.LANCZOS)
    return image


def apply_rule(image, rule, opaque):
    if "crop" in rule:
        x0, y0, x1, y1 = rule["crop"]
        image = image.crop((int(image.width * x0), int(image.height * y0), int(image.width * x1), int(image.height * y1)))
    if "band" in rule:
        top, bottom = rule["band"]
        image = image.crop((0, int(image.height * top), image.width, int(image.height * bottom)))
    if not opaque:
        image = bbox(image)
    if rule.get("desaturate_green"):
        image = desaturate_green(image)
    if rule.get("disk"):
        image = disk(image)
    if "hollow" in rule:
        image = hollow(image, rule["hollow"])
    if rule.get("tileable"):
        image = tileable(image)
    return resize(image, rule)


def write_image(image, output_root, out, fmt, quality=WEBP_QUALITY):
    target = os.path.join(output_root, out + "." + fmt)
    os.makedirs(os.path.dirname(target), exist_ok=True)
    if fmt == "webp":
        image.save(target, "WEBP", quality=quality, alpha_quality=100, method=6)
    else:
        image.save(target, "PNG", optimize=True)
    print(f"{out}.{fmt}  {image.width}x{image.height}  {os.path.getsize(target) // 1024} KB")


def cmd_cut(args):
    manifest, work = load_manifest(args.manifest)
    output_root = os.path.join(REPO, manifest["output"])
    fmt = manifest.get("format", "webp")
    for name in selected(manifest, args.only):
        source = manifest["sources"][name]
        opaque = source.get("opaque", False)
        image = load_source(work, name, opaque)
        rules = source["cut"]
        items = split_columns(image, source["split"]) if source.get("split") else [image] * len(rules)
        if len(items) != len(rules):
            sys.exit(f"{name}: {len(items)} items but {len(rules)} cut rules")
        for item, item_rules in zip(items, rules):
            for rule in item_rules if isinstance(item_rules, list) else [item_rules]:
                write_image(apply_rule(item, rule, opaque).convert("RGBA"), output_root, rule["out"], fmt,
                            rule.get("quality", WEBP_QUALITY))


# --- sheet / measure ----------------------------------------------------------------------------------------

def read_any(path):
    """Reads PNG/WebP, or a legacy OZP (PNG prefixed with 4 extra signature bytes)."""
    with open(path, "rb") as f:
        data = f.read()
    if data[:4] == b"\x89PNG" and data[4:8] == b"\x89PNG":
        data = data[4:]
    return Image.open(io.BytesIO(data)).convert("RGBA")


def cmd_sheet(args):
    manifest, work = load_manifest(args.manifest)
    output_root = os.path.join(REPO, manifest["output"])
    images = []
    for source in manifest["sources"].values():
        for rule in (r for rules in source["cut"] for r in (rules if isinstance(rules, list) else [rules])):
            path = os.path.join(output_root, rule["out"] + "." + manifest.get("format", "webp"))
            if os.path.exists(path):
                image = read_any(path)
                image.thumbnail((256, 128))
                images.append(image)
    if not images:
        sys.exit("nothing cut yet")

    pad, row_width = 12, 1100
    rows, row, x = [], [], pad
    for image in images:
        if x + image.width + pad > row_width and row:
            rows.append(row)
            row, x = [], pad
        row.append(image)
        x += image.width + pad
    rows.append(row)

    height = sum(max(i.height for i in r) + pad for r in rows) + pad
    sheet = Image.new("RGBA", (row_width, height), (28, 30, 38, 255))
    y = pad
    for r in rows:
        x = pad
        for image in r:
            sheet.alpha_composite(image, (x, y))
            x += image.width + pad
        y += max(i.height for i in r) + pad
    target = os.path.join(work, "sheet.png")
    sheet.save(target)
    print(target)


def cmd_measure(args):
    image = read_any(args.file)
    alpha = np.array(image)[..., 3] > 60
    h, w = alpha.shape
    print(f"size {w}x{h}")
    for fx in (0.1, 0.2, 0.3, 0.5):
        ys = np.where(alpha[:, int(w * fx)])[0]
        if len(ys):
            print(f"column x={fx:.1f}: opaque rows {ys.min()}..{ys.max()}  (band {ys.min() / h:.3f}..{(ys.max() + 1) / h:.3f})")
    for fy in (0.5, 0.8):
        xs = np.where(alpha[int(h * fy), :])[0]
        if len(xs):
            print(f"row y={fy:.1f}: opaque columns {xs.min()}..{xs.max()}")
    tops = [int(np.where(alpha[:, x])[0].min()) if alpha[:, x].any() else -1 for x in range(0, w, max(1, w // 32))]
    print("top edge every 1/32 of width (ornaments stick up where it drops):")
    print("  " + " ".join(str(t) for t in tops))
    if args.ring:
        fx, fy = (float(v) for v in args.ring.split(","))
        cx, cy, radius = find_ring(image, (fx, fy), search=0.06, radii=(0.1, 0.45))
        print(f"ring: centre {cx:.1f},{cy:.1f}px  inner radius {radius:.1f}px")
        print(f"  C#: new Vector3({cx / w:.3f}f, {cy / h:.3f}f, {2 * radius / w:.3f}f)  // centre X/Y, inner diameter / width")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)
    for name in ("generate", "cut", "sheet"):
        p = sub.add_parser(name)
        p.add_argument("manifest")
        if name != "sheet":
            p.add_argument("--only", help="comma separated source names")
        if name == "generate":
            p.add_argument("--jobs", type=int, default=4)
    p = sub.add_parser("measure")
    p.add_argument("file")
    p.add_argument("--ring", help="approximate centre 'x,y' (fractions) of a round socket/medallion to locate")
    args = parser.parse_args()
    {"generate": cmd_generate, "cut": cmd_cut, "sheet": cmd_sheet, "measure": cmd_measure}[args.command](args)


if __name__ == "__main__":
    main()
