from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image, ImageDraw

from translate_images import fit_text_to_box, render_lines, resolve_font_path, sample_fill_color


BUTTON_TRANSLATIONS = {
    "GUIBtnBBG.png": "官网",
    "GUIBtnBBGIn.png": "官网",
    "GUIBtnContinue.png": "继续",
    "GUIBtnContinueIn.png": "继续",
    "GUIBtnCredits.png": "制作",
    "GUIBtnCreditsIn.png": "制作",
    "GUIBtnDiscord.png": "社群",
    "GUIBtnDiscordIn.png": "社群",
    "GUIBtnNew.png": "新建",
    "GUIBtnNewIn.png": "新建",
    "GUIBtnOptions.png": "设置",
    "GUIBtnOptionsIn.png": "设置",
    "GUIBtnSteam.png": "Steam",
    "GUIBtnSteamIn.png": "Steam",
    "GUIBtnWiki.png": "百科",
    "GUIBtnWikiIn.png": "百科",
}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Render translated GUIBtn PNG images.")
    parser.add_argument("--input-root", type=Path, required=True, help="Source images directory, usually StreamingAssets/images.")
    parser.add_argument("--output-root", type=Path, required=True, help="Output images directory, usually workspace/mod-images.")
    parser.add_argument("--font-path", default=None, help="Optional font path for CJK rendering.")
    return parser.parse_args()


def get_text_box(file_name: str, image_width: int, image_height: int) -> tuple[int, int, int, int]:
    if "Continue" in file_name or "New" in file_name or "Options" in file_name:
        return (12, 18, image_width - 12, image_height - 14)
    if "Credits" in file_name or "Wiki" in file_name:
        return (10, 9, image_width - 10, image_height - 8)
    return (10, 10, image_width - 10, image_height - 10)


def get_text_colors(file_name: str) -> tuple[tuple[int, int, int, int], tuple[int, int, int, int]]:
    if file_name.endswith("In.png"):
        return (255, 255, 255, 255), (77, 23, 13, 220)
    return (20, 20, 20, 255), (255, 255, 255, 180)


def render_button(source_path: Path, output_path: Path, translation: str, font_path: Path) -> None:
    image = Image.open(source_path).convert("RGBA")
    draw = ImageDraw.Draw(image)
    text_box = get_text_box(source_path.name, image.width, image.height)
    background_color = sample_fill_color(image, text_box)
    draw.rounded_rectangle(text_box, radius=6, fill=background_color)

    fill, stroke_fill = get_text_colors(source_path.name)
    font, lines = fit_text_to_box(
        draw=draw,
        text=translation,
        font_path=font_path,
        max_width=max(1, text_box[2] - text_box[0]),
        max_height=max(1, text_box[3] - text_box[1]),
        min_font_size=10,
        max_font_size=28,
        stroke_width=1,
    )
    render_lines(
        draw=draw,
        lines=lines,
        font=font,
        bbox=text_box,
        fill=fill,
        stroke_fill=stroke_fill,
        stroke_width=1,
    )

    output_path.parent.mkdir(parents=True, exist_ok=True)
    image.save(output_path)


def main() -> int:
    args = parse_args()
    font_path = resolve_font_path(args.font_path)
    for file_name, translation in BUTTON_TRANSLATIONS.items():
        source_path = args.input_root / file_name
        if not source_path.exists():
            continue

        output_path = args.output_root / file_name
        render_button(source_path, output_path, translation, font_path)
        print(f"Rendered {file_name} -> {output_path}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())