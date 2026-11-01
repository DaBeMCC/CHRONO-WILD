"""Prepara las texturas de la muestra. Requiere Python 3 y Pillow."""

import argparse
import hashlib
import json
import math
import random
import urllib.request
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageOps

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "ArtSource/TechArt"
DEST = ROOT / "Assets/_Game/Art/Textures/Environment/Review"
SETS = {
    "ForestSoil": ("forest_ground_04", 1024),
    "Rock": ("rock_face_03", 512),
    "Bark": ("bark_brown_02", 512),
}


def fetch(url, target):
    if not target.exists():
        request = urllib.request.Request(url, headers={"User-Agent": "CHRONO-WILD-art-preparation/1.0"})
        with urllib.request.urlopen(request, timeout=60) as response:
            target.write_bytes(response.read())


def prepare_set(name, asset, size, offline):
    folder = SOURCE / asset
    folder.mkdir(parents=True, exist_ok=True)
    manifest = folder / "files.json"
    if not offline:
        fetch(f"https://api.polyhaven.com/files/{asset}", manifest)
    files = json.loads(manifest.read_text())
    maps = {}
    for channel in ("Diffuse", "nor_gl", "Rough", "AO", "Displacement"):
        info = files[channel]["1k"]["jpg"]
        source = folder / Path(info["url"]).name
        if not offline:
            fetch(info["url"], source)
        if hashlib.md5(source.read_bytes()).hexdigest() != info["md5"]:
            raise ValueError(f"No coincide la suma de verificación: {source}")
        with Image.open(source) as image:
            maps[channel] = image.convert("RGB").resize((size, size), Image.Resampling.LANCZOS)
    maps["Diffuse"].save(DEST / f"T_{name}_Albedo.jpg", quality=94, subsampling=0)
    maps["nor_gl"].save(DEST / f"T_{name}_Normal.png")
    # URP Terrain: R metallic, G AO, B height, A smoothness. URP Lit reutiliza R/G/A.
    zero = Image.new("L", (size, size), 0)
    mask = Image.merge("RGBA", (
        zero, maps["AO"].convert("L"), maps["Displacement"].convert("L"),
        ImageOps.invert(maps["Rough"].convert("L")),
    ))
    mask.save(DEST / f"T_{name}_Mask.png")


def prepare_leaf():
    size = 512
    rng = random.Random(47)
    image = Image.new("RGB", (size, size))
    pixels = image.load()
    for y in range(size):
        for x in range(size):
            radius = math.hypot(x - size * .5, y - size * .05) / size
            wave = math.sin(x * .045 + y * .019) * 2 + rng.uniform(-3, 3)
            pixels[x, y] = (int(78 + radius * 15 + wave), int(115 + radius * 24 + wave), int(34 + radius * 10 + wave))
    draw = ImageDraw.Draw(image)
    for i in range(77):
        angle = math.radians(-72 + 144 * i / 76)
        end = (256 + math.sin(angle) * 700, 24 + math.cos(angle) * 700)
        draw.line(((256, 24), end), fill=(105, 145, 48), width=1)
    image.filter(ImageFilter.GaussianBlur(.35)).save(DEST / "T_GinkgoLeaf_Albedo.png")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--offline", action="store_true", help="Usar únicamente fuentes ya descargadas")
    args = parser.parse_args()
    DEST.mkdir(parents=True, exist_ok=True)
    for name, (asset, size) in SETS.items():
        prepare_set(name, asset, size, args.offline)
    prepare_leaf()
    print(f"Texturas preparadas en {DEST}")


if __name__ == "__main__":
    main()
