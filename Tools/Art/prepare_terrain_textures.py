"""Prepara las siete capas PBR pendientes, sin modificar la muestra aprobada.

Fuentes CC0: https://polyhaven.com/license · Powered by Poly Haven.
Python 3 y Pillow. Las fuentes y los catálogos quedan en ArtSource/TechArt.
"""
import argparse
from PIL import Image, ImageEnhance
import prepare_review_textures as pipeline

DEST = pipeline.ROOT / "Assets/_Game/Art/Textures/Environment/Terrain"
SETS = {
    "SandDry": "sand_01",
    "SandWet": "sand_01",
    "LeafLitter": "forest_leaves_04",
    "Mud": "brown_mud_02",
    "Rock": "rock_face_03",
    "PathDirt": "forest_ground_04",
}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--offline", action="store_true")
    args = parser.parse_args()
    DEST.mkdir(parents=True, exist_ok=True)
    pipeline.DEST = DEST
    for name, asset in SETS.items():
        pipeline.prepare_set(name, asset, 1024, args.offline)
        albedo_path = DEST / f"T_{name}_Albedo.jpg"
        mask_path = DEST / f"T_{name}_Mask.png"
        with Image.open(albedo_path) as image:
            albedo = image.convert("RGB")
        with Image.open(mask_path) as image:
            channels = list(image.convert("RGBA").split())
        if name == "SandWet":
            albedo = ImageEnhance.Brightness(albedo).enhance(.72)
            channels[3] = channels[3].point(lambda value: int(72 + value * .42))
        elif name == "PathDirt":
            albedo = ImageEnhance.Brightness(albedo).enhance(1.06)
            channels[3] = channels[3].point(lambda value: int(value * .8))
        elif name == "Mud":
            channels[3] = channels[3].point(lambda value: min(140, int(35 + value * .65)))
        albedo.save(albedo_path, quality=94, subsampling=0)
        Image.merge("RGBA", channels).save(mask_path)
        if name == "SandWet":
            # Las dos arenas comparten la misma normal importada.
            (DEST / "T_SandWet_Normal.png").unlink()
    print(f"Siete capas preparadas en {DEST}")


if __name__ == "__main__":
    main()
