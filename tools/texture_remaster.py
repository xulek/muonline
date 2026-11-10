"""Remaster MU Online world textures with Codex image generation, keeping their exact layout.

The textures of the client are small (32-128 px) and many are atlases: their layout must match the UVs of the
models pixel for pixel, which image generation does not guarantee. So a generated image is never used as is: the
generated remaster is used when it kept the layout (checked by correlation with the original), its broad colour
pulled towards the original; otherwise only its fine detail is laid over the upscaled original. Output is WebP
(lossy colour, exact lossless alpha for cut-outs) in Client.Main/Assets/Remaster/<folder>/<name>.webp - embedded and
loaded instead of the Data file of the same name, with straight alpha like the originals. Edit the WebP files
directly for hand fixes.

Manifest (tools/remaster/<set>.json):
  { "data": "C:/Games/.../Data", "folder": "Object1", "scale": 4, "style": "...",
    "textures": { "tile_wood01": { "prompt": "...", "tileable": true }, ... } }

Commands:
  extract  <manifest>                     originals -> tools/remaster/.work/<set>/orig/<name>.png
  generate <manifest> [--only a,b] [--jobs 4]   Codex remaster, original attached -> .work/<set>/gen/<name>.png
  blend    <manifest> [--only a,b] [--detail 0.9] [--depth 0.35]   -> Client.Main/Assets/Remaster/<folder>/<name>.webp
  sheet    <manifest>                     original | generated | result -> .work/<set>/sheet.png
"""

import argparse
import concurrent.futures
import io
import json
import os
import shutil
import struct
import subprocess
import sys

import numpy as np
from PIL import Image, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT_ROOT = os.path.join(ROOT, "Client.Main", "Assets", "Remaster")

COPY_INSTRUCTION = (
    "Use your image generation tool to create ONE image based on the attached image, then copy the PNG file the "
    "tool saved under {images} into the current directory as {name}.png using a single short copy command with "
    "the file path (never embed image data in a command). Do nothing else. Image: "
)
LAYOUT = (
    "It is a remaster of the attached game texture: keep exactly the same composition and layout, every element "
    "in the same place, with the same size and the same colours; square image, fills the whole canvas edge to "
    "edge, no frame, no border, no text or letters, no new objects."
)
TILEABLE = "The texture repeats seamlessly: its left edge continues its right edge and its top edge its bottom edge."


def load_manifest(path):
    with open(path, encoding="utf-8") as f:
        manifest = json.load(f)
    name = os.path.splitext(os.path.basename(path))[0]
    work = os.path.join(os.path.dirname(os.path.abspath(path)), ".work", name)
    for sub in ("orig", "gen", "ref"):
        os.makedirs(os.path.join(work, sub), exist_ok=True)
    return manifest, work


def selected(manifest, only):
    names = list(manifest["textures"])
    if only:
        wanted = set(only.split(","))
        names = [n for n in names if n in wanted]
    return names


def source_file(manifest, name):
    folder = os.path.join(manifest["data"], manifest["folder"])
    for ext in (".OZJ", ".OZT", ".ozj", ".ozt"):
        path = os.path.join(folder, name + ext)
        if os.path.exists(path):
            return path
    return None


def decode(path):
    """Original MU texture -> (PIL image, raw header bytes, kind 'ozj'|'ozt')."""
    data = open(path, "rb").read()
    if path.lower().endswith(".ozj"):
        return Image.open(io.BytesIO(data[24:])).convert("RGB"), data[:24], "ozj"
    width, height = struct.unpack_from("<hh", data, 16)
    pixels = np.frombuffer(data, dtype=np.uint8, count=width * height * 4, offset=22).reshape(height, width, 4)
    rgba = pixels[::-1, :, [2, 1, 0, 3]]  # bottom-up BGRA -> top-down RGBA
    return Image.fromarray(np.ascontiguousarray(rgba), "RGBA"), data[:16], "ozt"


KEY = (255, 0, 255)
KEY_PROMPT = ("The background is flat pure magenta (#FF00FF): keep it exactly flat pure magenta, only the object on it "
              "is remastered.")


def on_key(image):
    """RGB image; a cut-out is composited on flat magenta."""
    if image.mode != "RGBA":
        return image.convert("RGB")
    background = Image.new("RGBA", image.size, KEY + (255,))
    background.alpha_composite(image)
    return background.convert("RGB")


# --- extract ------------------------------------------------------------------------------------------------

def cmd_extract(args):
    manifest, work = load_manifest(args.manifest)
    for name in selected(manifest, args.only):
        path = source_file(manifest, name)
        if path is None:
            print(f"{name}: not found")
            continue
        image, _, kind = decode(path)
        image.save(os.path.join(work, "orig", name + ".png"))
        # Reference for Codex: smooth 512 px so it does not copy the pixel blocks; cut-outs on flat magenta, so the
        # transparent part is unmistakably background (the alpha is taken from the original anyway).
        on_key(image).resize((512, 512), Image.BICUBIC).save(os.path.join(work, "ref", name + ".png"))
        print(f"{name}: {kind} {image.size}")


# --- generate -----------------------------------------------------------------------------------------------

def generate_one(name, texture, style, work):
    codex = shutil.which("codex")
    if not codex:
        return name, "codex CLI not found on PATH"
    gen = os.path.join(work, "gen")
    target = os.path.join(gen, name + ".png")
    reference = os.path.join(work, "ref", name + ".png")
    images = os.path.join(os.path.expanduser("~"), ".codex", "generated_images")
    keyed = os.path.exists(os.path.join(work, "orig", name + ".png")) and Image.open(os.path.join(work, "orig", name + ".png")).mode == "RGBA"
    parts = [texture["prompt"], LAYOUT, KEY_PROMPT if keyed else "", TILEABLE if texture.get("tileable") else "", style]
    prompt = COPY_INSTRUCTION.format(images=images, name=name) + " ".join(p for p in parts if p)
    for attempt in range(2):
        with open(os.path.join(gen, name + ".log"), "w", encoding="utf-8") as log:
            subprocess.run([codex, "exec", "--skip-git-repo-check", "-s", "workspace-write", prompt, "-i", reference],
                           cwd=gen, stdin=subprocess.DEVNULL, stdout=log, stderr=subprocess.STDOUT, timeout=900)
        if os.path.exists(target) and os.path.getsize(target) > 0:
            return name, "ok" + (" (retry)" if attempt else "")
    return name, f"failed, see gen/{name}.log"


def cmd_generate(args):
    manifest, work = load_manifest(args.manifest)
    style = manifest.get("style", "")
    with concurrent.futures.ThreadPoolExecutor(max_workers=args.jobs) as pool:
        futures = [pool.submit(generate_one, n, manifest["textures"][n], style, work) for n in selected(manifest, args.only)]
        for future in concurrent.futures.as_completed(futures):
            name, status = future.result()
            print(f"{name}: {status}")


# --- blend --------------------------------------------------------------------------------------------------

def box_blur(array, half, axis):
    """Moving average over 2*half+1 samples along one axis (edges extended)."""
    pad = [(0, 0)] * array.ndim
    pad[axis] = (half + 1, half)
    padded = np.pad(array, pad, mode="edge")
    summed = np.cumsum(padded, axis=axis)
    upper = np.take(summed, range(2 * half + 1, summed.shape[axis]), axis=axis)
    lower = np.take(summed, range(0, summed.shape[axis] - 2 * half - 1), axis=axis)
    return (upper - lower) / (2 * half + 1)


def blur(array, radius, tileable):
    """Approximate gaussian blur (three box passes) of a float HxWxC array; wraps around for tileable textures."""
    h, w = array.shape[:2]
    if tileable:
        return blur(np.tile(array, (3, 3, 1)), radius, False)[h:2 * h, w:2 * w]
    half = max(1, int(round(radius * 0.8)))
    out = array.astype(np.float32)
    for _ in range(3):
        out = box_blur(box_blur(out, half, 0), half, 1)
    return out


def seamless(detail, band=0.06):
    """Makes a layer repeat without seams: only a band along the edges is cross-faded with the layer shifted by
    half (whose middle lies there), the centre stays untouched."""
    h, w = detail.shape[:2]

    def ramp(n):
        distance = np.minimum(np.arange(n), n - 1 - np.arange(n)) / max(1, n * band)
        t = np.clip(distance, 0, 1)
        return t * t * (3 - 2 * t)

    weight = np.minimum(ramp(h)[:, None], ramp(w)[None, :])[..., None]
    shifted = np.roll(np.roll(detail, h // 2, axis=0), w // 2, axis=1)
    return detail * weight + shifted * (1 - weight)


def blend_one(manifest, work, name, detail_amount, depth_amount):
    texture = manifest["textures"][name]
    path = source_file(manifest, name)
    gen_path = os.path.join(work, "gen", name + ".png")
    if path is None or not os.path.exists(gen_path):
        return f"{name}: missing original or generated image"

    original, header, kind = decode(path)
    scale = texture.get("scale", manifest.get("scale", 4))
    size = (min(1024, original.width * scale), min(1024, original.height * scale))
    tileable = texture.get("tileable", False)
    radius = max(1.5, size[0] / 96)

    base = np.asarray(original.convert("RGB").resize(size, Image.LANCZOS), dtype=np.float32)
    generated = np.asarray(Image.open(gen_path).convert("RGB").resize(size, Image.LANCZOS), dtype=np.float32)

    # How well the remaster kept the layout: correlation of both pictures at low resolution.
    small = (32, 32)
    a = np.asarray(on_key(original).convert("L").resize(small, Image.BICUBIC), dtype=np.float32).ravel()
    b = np.asarray(Image.open(gen_path).convert("L").resize(small, Image.BICUBIC), dtype=np.float32).ravel()
    alignment = float(np.corrcoef(a, b)[0, 1]) if a.std() > 1 and b.std() > 1 else 0.0
    use_generated = alignment >= texture.get("min_alignment", 0.6) and not texture.get("detail_only", False)

    if use_generated:
        # The remaster as it is, its broad colour pulled halfway to the original so it fits the rest of the game.
        keep = texture.get("keep_colour", 0.5)
        if kind == "ozt":
            # Cut-out: colours averaged over the visible part only (the transparent part holds junk colour in the
            # original and magenta in the remaster), and magenta spill on the remaster's edges replaced.
            weight = np.asarray(original.split()[3].resize(size, Image.BICUBIC), dtype=np.float32)[..., None] / 255.0
            def low(image):
                return blur(image * weight, radius * 6, tileable) / np.maximum(blur(weight, radius * 6, tileable), 1e-3)
            low_orig = low(base)
            r, g, b = generated[..., 0], generated[..., 1], generated[..., 2]
            spill = np.clip((np.minimum(r, b) - g - 40) / 80, 0, 1)[..., None]
            generated = generated * (1 - spill) + low_orig * spill
            low_gen = low(generated)
        else:
            low_gen = blur(generated, radius * 6, tileable)
            low_orig = blur(base, radius * 6, tileable)
        result = generated - low_gen + low_gen * (1 - keep) + low_orig * keep
        if tileable:
            # The generated detail does not repeat at the edges: cross-fade it so the tile has no seam.
            result = seamless(result)
    else:
        # Detail of the remaster as a luminance-like layer (its own colour shifts are ignored).
        detail = generated - blur(generated, radius, tileable)
        detail = detail.mean(axis=2, keepdims=True)
        if tileable:
            detail = seamless(detail)
        # Depth: local contrast of the original's own shapes (deeper grooves, brighter edges).
        depth = base - blur(base, radius * 4, tileable)
        result = base + detail * detail_amount * texture.get("detail", 1.0) + depth * depth_amount * texture.get("depth", 1.0)
    if texture.get("flatten"):
        # Large blotches repeat visibly on tiled ground: keep the detail, even out the broad shading (the game varies
        # the brightness over large areas itself).
        low = blur(result, size[0] / 10, tileable)
        result = result - low + low.mean(axis=(0, 1), keepdims=True) * texture["flatten"] + low * (1 - texture["flatten"])
    result = np.clip(result, 0, 255).astype(np.uint8)
    mode = f"remaster (alignment {alignment:.2f})" if use_generated else f"detail transfer (alignment {alignment:.2f})"

    os.makedirs(os.path.join(OUT_ROOT, manifest["folder"]), exist_ok=True)
    preview = os.path.join(work, "result", name + ".png")
    os.makedirs(os.path.dirname(preview), exist_ok=True)

    out = os.path.join(OUT_ROOT, manifest["folder"], name + ".webp")
    if kind == "ozj":
        image = Image.fromarray(result, "RGB")
    else:
        # Alpha from the original, upscaled smoothly and kept crisp (cut-out shapes unchanged).
        alpha = np.asarray(original.split()[3].resize(size, Image.BICUBIC), dtype=np.float32)
        alpha = np.clip((alpha - 128) * 1.6 + 128, 0, 255).astype(np.uint8)
        image = Image.fromarray(np.dstack([result, alpha]), "RGBA")
    image.save(preview)
    image.save(out, "WEBP", quality=92, method=6, alpha_quality=100, exact=True)
    return f"{name}: {size[0]}x{size[1]} {mode} -> {os.path.relpath(out, ROOT)}"


def record_size(folder, name, size):
    """Original size of a remastered texture, for code that lays textures out by their size (terrain tiling)."""
    path = os.path.join(OUT_ROOT, "sizes.json")
    sizes = json.load(open(path, encoding="utf-8")) if os.path.exists(path) else {}
    sizes[f"{folder}/{name}"] = list(size)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(dict(sorted(sizes.items())), f, indent=1)


def cmd_blend(args):
    manifest, work = load_manifest(args.manifest)
    for name in selected(manifest, args.only):
        print(blend_one(manifest, work, name, args.detail, args.depth))
        path = source_file(manifest, name)
        if path:
            record_size(manifest["folder"], name, decode(path)[0].size)


def cmd_sheet(args):
    manifest, work = load_manifest(args.manifest)
    names = [n for n in selected(manifest, None) if os.path.exists(os.path.join(work, "result", n + ".png"))]
    cell = 220
    sheet = Image.new("RGB", (cell * 3, cell * max(1, len(names))), (32, 32, 32))
    for row, name in enumerate(names):
        for col, sub in enumerate(("orig", "gen", "result")):
            path = os.path.join(work, sub, name + ".png")
            if os.path.exists(path):
                image = Image.open(path)
                if image.mode == "RGBA":
                    backdrop = Image.new("RGBA", image.size, (40, 60, 90, 255))
                    backdrop.alpha_composite(image)
                    image = backdrop
                image = image.convert("RGB").resize((cell - 8, cell - 8), Image.LANCZOS)
                sheet.paste(image, (col * cell + 4, row * cell + 4))
    out = os.path.join(work, "sheet.png")
    sheet.save(out)
    print(out)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)
    for command in ("extract", "generate", "blend", "sheet"):
        p = sub.add_parser(command)
        p.add_argument("manifest")
        p.add_argument("--only")
        if command == "generate":
            p.add_argument("--jobs", type=int, default=4)
        if command == "blend":
            p.add_argument("--detail", type=float, default=0.9)
            p.add_argument("--depth", type=float, default=0.35)
    args = parser.parse_args()
    {"extract": cmd_extract, "generate": cmd_generate, "blend": cmd_blend, "sheet": cmd_sheet}[args.command](args)


if __name__ == "__main__":
    main()
