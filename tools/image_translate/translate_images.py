from __future__ import annotations

import argparse
import json
import math
import os
import shutil
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Sequence


IMAGE_EXTENSIONS = {".png", ".jpg", ".jpeg", ".webp", ".bmp"}
DEFAULT_MODEL = "deepseek-v4-flash"
DEFAULT_SOURCE_LANGUAGE = "Chinese"
DEFAULT_OCR_LANGUAGE = "ch"
DEFAULT_BATCH_SIZE = 20
DEFAULT_CONFIDENCE_THRESHOLD = 0.60
DEFAULT_PADDING = 4
DEFAULT_MIN_FONT_SIZE = 10
DEFAULT_MAX_FONT_SIZE = 72


@dataclass(frozen=True)
class OcrRegion:
    index: int
    polygon: tuple[tuple[float, float], ...]
    bbox: tuple[int, int, int, int]
    text: str
    confidence: float


class DeepSeekTranslator:
    def __init__(
        self,
        api_key: str,
        model: str,
        source_language: str,
        target_language: str,
        proxy: str | None,
        timeout_seconds: int,
        system_prompt: str | None = None,
    ) -> None:
        import requests

        self._api_key = api_key
        self._model = model
        self._source_language = source_language
        self._target_language = target_language
        self._timeout_seconds = timeout_seconds
        self._system_prompt = system_prompt
        self._session = requests.Session()
        if proxy:
            self._session.proxies.update({"http": proxy, "https": proxy})

    def translate_batch(self, texts: Sequence[str]) -> list[str]:
        if not texts:
            return []

        payload = {
            "model": self._model,
            "messages": [
                {
                    "role": "system",
                    "content": self._build_system_prompt(),
                },
                {
                    "role": "user",
                    "content": json.dumps(list(texts), ensure_ascii=False),
                },
            ],
            "response_format": {"type": "json_object"},
            "temperature": 0.2,
            "max_tokens": 4096,
            "thinking": {"type": "disabled"},
        }

        response = self._session.post(
            "https://api.deepseek.com/chat/completions",
            headers={
                "Authorization": f"Bearer {self._api_key}",
                "Accept": "application/json",
                "Content-Type": "application/json; charset=utf-8",
            },
            json=payload,
            timeout=self._timeout_seconds,
        )
        if not response.ok:
            raise RuntimeError(
                f"DeepSeek request failed with {response.status_code}: {response.text.strip()}"
            )

        response_payload = response.json()
        content = (
            response_payload.get("choices", [{}])[0]
            .get("message", {})
            .get("content")
        )
        if not content:
            raise RuntimeError("DeepSeek returned an empty message.content payload.")

        try:
            parsed = json.loads(content)
        except json.JSONDecodeError as exc:
            raise RuntimeError(f"DeepSeek returned non-JSON content: {content}") from exc

        translations = parsed.get("translations")
        if not isinstance(translations, list):
            raise RuntimeError(f"DeepSeek JSON payload is missing translations: {content}")
        if len(translations) != len(texts):
            raise RuntimeError(
                f"DeepSeek returned {len(translations)} translations for {len(texts)} inputs."
            )

        return ["" if item is None else str(item) for item in translations]

    def _build_system_prompt(self) -> str:
        if self._system_prompt:
            return self._system_prompt

        return (
            f"You translate OCR text snippets from {self._source_language} into "
            f"{self._target_language}. The user message is always a JSON array of text "
            "segments extracted from a single image. Return exactly one valid JSON object "
            'in the form {"translations":[...]}. The translations array must contain the '
            "same number of elements and the same order as the input. Output only the JSON "
            "object. Do not output markdown, code fences, comments, or extra text. Preserve "
            "numbers, placeholders, punctuation, and obvious IDs. Keep each entry as one "
            "translated snippet, not a rewritten paragraph."
        )


class ImageTranslationPipeline:
    def __init__(self, args: argparse.Namespace) -> None:
        from paddleocr import PaddleOCR

        self._args = args
        self._translator = DeepSeekTranslator(
            api_key=args.api_key,
            model=args.model,
            source_language=args.source_language,
            target_language=args.target_language,
            proxy=args.proxy,
            timeout_seconds=args.timeout_seconds,
        )
        self._ocr = PaddleOCR(use_angle_cls=False, lang=args.ocr_language)
        self._font_path = resolve_font_path(args.font_path)

    def run(self) -> int:
        input_path = self._args.input_path
        image_paths = collect_image_paths(input_path)
        if not image_paths:
            raise FileNotFoundError(f"No supported images were found under '{input_path}'.")

        output_root = self._args.output_path
        for image_path in image_paths:
            output_path = resolve_output_path(input_path, output_root, image_path)
            output_path.parent.mkdir(parents=True, exist_ok=True)
            self._process_image(image_path, output_path)

        return 0

    def _process_image(self, image_path: Path, output_path: Path) -> None:
        regions = self._extract_regions(image_path)
        if not regions:
            shutil.copy2(image_path, output_path)
            print(f"[copy] {image_path} -> {output_path} (no OCR text)")
            return

        source_texts = [region.text for region in regions]
        translations = []
        for start in range(0, len(source_texts), self._args.batch_size):
            batch = source_texts[start : start + self._args.batch_size]
            translations.extend(self._translator.translate_batch(batch))

        self._render_translated_image(image_path, output_path, regions, translations)
        if self._args.write_debug_json:
            self._write_debug_json(image_path, output_path, regions, translations)

        print(f"[done] {image_path} -> {output_path} ({len(regions)} region(s))")

    def _extract_regions(self, image_path: Path) -> list[OcrRegion]:
        raw_result = self._ocr.ocr(str(image_path), cls=False)
        if not raw_result:
            return []

        entries = raw_result[0] or []
        regions: list[OcrRegion] = []
        for index, entry in enumerate(entries):
            if not entry or len(entry) < 2:
                continue

            polygon_raw = entry[0]
            text_info = entry[1]
            if not polygon_raw or not text_info or len(text_info) < 2:
                continue

            text = str(text_info[0]).strip()
            confidence = float(text_info[1])
            if not text or confidence < self._args.confidence_threshold:
                continue

            polygon = tuple((float(point[0]), float(point[1])) for point in polygon_raw)
            bbox = polygon_to_bbox(polygon)
            regions.append(
                OcrRegion(
                    index=index,
                    polygon=polygon,
                    bbox=bbox,
                    text=text,
                    confidence=confidence,
                )
            )

        return regions

    def _render_translated_image(
        self,
        image_path: Path,
        output_path: Path,
        regions: Sequence[OcrRegion],
        translations: Sequence[str],
    ) -> None:
        from PIL import Image, ImageDraw

        image = Image.open(image_path).convert("RGBA")
        draw = ImageDraw.Draw(image)

        for region, translated_text in zip(regions, translations, strict=True):
            replacement_text = translated_text.strip() or region.text
            bbox = expand_bbox(region.bbox, self._args.padding, image.width, image.height)
            background_color = sample_fill_color(image, bbox)
            draw.rectangle(bbox, fill=background_color)

            text_fill, stroke_fill = get_contrasting_colors(background_color)
            font, lines = fit_text_to_box(
                draw=draw,
                text=replacement_text,
                font_path=self._font_path,
                max_width=max(1, bbox[2] - bbox[0]),
                max_height=max(1, bbox[3] - bbox[1]),
                min_font_size=self._args.min_font_size,
                max_font_size=self._args.max_font_size,
                stroke_width=1,
            )

            render_lines(
                draw=draw,
                lines=lines,
                font=font,
                bbox=bbox,
                fill=text_fill,
                stroke_fill=stroke_fill,
                stroke_width=1,
            )

        image.save(output_path)

    def _write_debug_json(
        self,
        image_path: Path,
        output_path: Path,
        regions: Sequence[OcrRegion],
        translations: Sequence[str],
    ) -> None:
        debug_path = output_path.with_suffix(output_path.suffix + ".json")
        payload = {
            "source_image": str(image_path),
            "output_image": str(output_path),
            "source_language": self._args.source_language,
            "target_language": self._args.target_language,
            "regions": [
                {
                    "index": region.index,
                    "text": region.text,
                    "translation": translation,
                    "confidence": region.confidence,
                    "bbox": list(region.bbox),
                    "polygon": [list(point) for point in region.polygon],
                }
                for region, translation in zip(regions, translations, strict=True)
            ],
        }
        debug_path.write_text(
            json.dumps(payload, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8",
        )


def parse_args(argv: Sequence[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="OCR Chinese text from UI images, translate it with DeepSeek, and repaint translated PNGs.",
        epilog=(
            "Example: D:/Python314/python.exe translate_images.py "
            "--input G:/SteamLibrary/steamapps/common/Ostranauts/Ostranauts_Data/StreamingAssets/images "
            "--output D:/tmp/translated-images --target-language English "
            "--api-key <key> --proxy http://127.0.0.1:20800 --write-debug-json"
        ),
    )
    parser.add_argument("--input", dest="input_path", type=Path, required=True, help="Input image file or directory.")
    parser.add_argument("--output", dest="output_path", type=Path, required=True, help="Output image file or directory.")
    parser.add_argument(
        "--target-language",
        required=True,
        help="Target language name passed to DeepSeek, for example English or Simplified Chinese.",
    )
    parser.add_argument(
        "--source-language",
        default=DEFAULT_SOURCE_LANGUAGE,
        help=f"Source language name for the OCR text. Default: {DEFAULT_SOURCE_LANGUAGE}.",
    )
    parser.add_argument(
        "--ocr-language",
        default=DEFAULT_OCR_LANGUAGE,
        help=f"PaddleOCR language code. Default: {DEFAULT_OCR_LANGUAGE}.",
    )
    parser.add_argument(
        "--api-key",
        default=os.environ.get("DEEPSEEK_API_KEY", ""),
        help="DeepSeek API key. Defaults to DEEPSEEK_API_KEY.",
    )
    parser.add_argument("--model", default=DEFAULT_MODEL, help=f"DeepSeek model. Default: {DEFAULT_MODEL}.")
    parser.add_argument(
        "--proxy",
        default=os.environ.get("HTTPS_PROXY") or os.environ.get("HTTP_PROXY") or None,
        help="Optional HTTP or SOCKS5 proxy, for example http://127.0.0.1:20800 or socks5://127.0.0.1:20800.",
    )
    parser.add_argument("--font-path", default=None, help="Optional .ttf/.ttc font path used for repainting.")
    parser.add_argument("--batch-size", type=int, default=DEFAULT_BATCH_SIZE, help=f"DeepSeek batch size. Default: {DEFAULT_BATCH_SIZE}.")
    parser.add_argument(
        "--confidence-threshold",
        type=float,
        default=DEFAULT_CONFIDENCE_THRESHOLD,
        help=f"Minimum OCR confidence to keep a text region. Default: {DEFAULT_CONFIDENCE_THRESHOLD}.",
    )
    parser.add_argument("--padding", type=int, default=DEFAULT_PADDING, help=f"Bounding-box padding in pixels. Default: {DEFAULT_PADDING}.")
    parser.add_argument(
        "--min-font-size",
        type=int,
        default=DEFAULT_MIN_FONT_SIZE,
        help=f"Minimum repaint font size. Default: {DEFAULT_MIN_FONT_SIZE}.",
    )
    parser.add_argument(
        "--max-font-size",
        type=int,
        default=DEFAULT_MAX_FONT_SIZE,
        help=f"Maximum repaint font size. Default: {DEFAULT_MAX_FONT_SIZE}.",
    )
    parser.add_argument(
        "--timeout-seconds",
        type=int,
        default=120,
        help="HTTP timeout for each DeepSeek request.",
    )
    parser.add_argument(
        "--write-debug-json",
        action="store_true",
        help="Write a sidecar JSON file containing OCR boxes and translated text.",
    )
    args = parser.parse_args(argv)

    if not args.api_key:
        parser.error("--api-key is required unless DEEPSEEK_API_KEY is already set.")
    if args.batch_size <= 0:
        parser.error("--batch-size must be greater than 0.")
    if args.min_font_size <= 0 or args.max_font_size < args.min_font_size:
        parser.error("--min-font-size and --max-font-size must be positive and ordered.")
    if args.padding < 0:
        parser.error("--padding cannot be negative.")

    return args


def collect_image_paths(input_path: Path) -> list[Path]:
    if input_path.is_file():
        if input_path.suffix.lower() not in IMAGE_EXTENSIONS:
            raise ValueError(f"Unsupported image extension: {input_path.suffix}")
        return [input_path]
    if not input_path.is_dir():
        raise FileNotFoundError(f"Input path was not found: {input_path}")

    return sorted(
        path for path in input_path.rglob("*") if path.is_file() and path.suffix.lower() in IMAGE_EXTENSIONS
    )


def resolve_output_path(input_root: Path, output_root: Path, image_path: Path) -> Path:
    if input_root.is_file():
        if output_root.suffix:
            return output_root
        return output_root / image_path.name

    relative_path = image_path.relative_to(input_root)
    return output_root / relative_path


def polygon_to_bbox(polygon: Sequence[tuple[float, float]]) -> tuple[int, int, int, int]:
    xs = [point[0] for point in polygon]
    ys = [point[1] for point in polygon]
    return (
        max(0, math.floor(min(xs))),
        max(0, math.floor(min(ys))),
        max(1, math.ceil(max(xs))),
        max(1, math.ceil(max(ys))),
    )


def expand_bbox(
    bbox: tuple[int, int, int, int],
    padding: int,
    image_width: int,
    image_height: int,
) -> tuple[int, int, int, int]:
    left, top, right, bottom = bbox
    return (
        max(0, left - padding),
        max(0, top - padding),
        min(image_width, right + padding),
        min(image_height, bottom + padding),
    )


def sample_fill_color(image: Image.Image, bbox: tuple[int, int, int, int]) -> tuple[int, int, int, int]:
    import numpy as np

    left, top, right, bottom = bbox
    crop = image.crop((left, top, right, bottom)).convert("RGBA")
    pixels = np.asarray(crop)
    if pixels.size == 0:
        return (255, 255, 255, 255)

    flattened = pixels.reshape(-1, 4)
    median = np.median(flattened, axis=0)
    return tuple(int(channel) for channel in median)


def get_contrasting_colors(background: tuple[int, int, int, int]) -> tuple[tuple[int, int, int, int], tuple[int, int, int, int]]:
    red, green, blue, _ = background
    luminance = 0.299 * red + 0.587 * green + 0.114 * blue
    if luminance >= 140:
        return (0, 0, 0, 255), (255, 255, 255, 180)
    return (255, 255, 255, 255), (0, 0, 0, 180)


def fit_text_to_box(
    draw: Any,
    text: str,
    font_path: Path,
    max_width: int,
    max_height: int,
    min_font_size: int,
    max_font_size: int,
    stroke_width: int,
) -> tuple[Any, list[str]]:
    from PIL import ImageFont

    best_font: Any | None = None
    best_lines: list[str] = [text]

    for font_size in range(max_font_size, min_font_size - 1, -1):
        font = ImageFont.truetype(str(font_path), font_size)
        lines = wrap_text(draw, text, font, max_width, stroke_width)
        width, height = measure_block(draw, lines, font, stroke_width)
        best_font = font
        best_lines = lines
        if width <= max_width and height <= max_height:
            return font, lines

    if best_font is None:
        raise RuntimeError("No usable font could be created.")
    return best_font, best_lines


def get_effective_line_height(font: Any, fallback_height: int, stroke_width: int) -> int:
    if hasattr(font, "getmetrics"):
        ascent, descent = font.getmetrics()
        return max(fallback_height, int(ascent + descent + stroke_width))

    return fallback_height


def wrap_text(
    draw: Any,
    text: str,
    font: Any,
    max_width: int,
    stroke_width: int,
) -> list[str]:
    if not text:
        return [""]

    if "\n" in text:
        wrapped_lines: list[str] = []
        for paragraph in text.splitlines():
            if not paragraph:
                wrapped_lines.append("")
                continue

            wrapped_lines.extend(wrap_text(draw, paragraph, font, max_width, stroke_width))

        return wrapped_lines or [""]

    if any(character.isspace() for character in text.strip()):
        word_lines = wrap_words(draw, text, font, max_width, stroke_width)
        if max(measure_line(draw, line, font, stroke_width) for line in word_lines) <= max_width:
            return word_lines

    return wrap_characters(draw, text, font, max_width, stroke_width)


def wrap_words(
    draw: Any,
    text: str,
    font: Any,
    max_width: int,
    stroke_width: int,
) -> list[str]:
    words = text.split()
    if not words:
        return [text]

    lines: list[str] = []
    current = words[0]
    for word in words[1:]:
        candidate = f"{current} {word}"
        if measure_line(draw, candidate, font, stroke_width) <= max_width:
            current = candidate
            continue

        if measure_line(draw, word, font, stroke_width) > max_width:
            return wrap_characters(draw, text, font, max_width, stroke_width)

        lines.append(current)
        current = word

    lines.append(current)
    return lines


def wrap_characters(
    draw: Any,
    text: str,
    font: Any,
    max_width: int,
    stroke_width: int,
) -> list[str]:
    lines: list[str] = []
    current = ""
    for character in text:
        candidate = f"{current}{character}"
        if current and measure_line(draw, candidate, font, stroke_width) > max_width:
            lines.append(current)
            current = character
            continue
        current = candidate

    if current:
        lines.append(current)

    return lines or [text]


def measure_line(
    draw: Any,
    text: str,
    font: Any,
    stroke_width: int,
) -> int:
    left, _top, right, _bottom = draw.textbbox((0, 0), text, font=font, stroke_width=stroke_width)
    return right - left


def measure_block(
    draw: Any,
    lines: Sequence[str],
    font: Any,
    stroke_width: int,
) -> tuple[int, int]:
    widths = [measure_line(draw, line, font, stroke_width) for line in lines]
    heights = []
    use_relaxed_multiline_height = len(lines) > 1
    for line in lines:
        line_bbox = draw.textbbox((0, 0), line or "Ay", font=font, stroke_width=stroke_width)
        glyph_height = line_bbox[3] - line_bbox[1]
        if use_relaxed_multiline_height:
            heights.append(get_effective_line_height(font, glyph_height, stroke_width))
        else:
            heights.append(glyph_height)

    line_gap = max(2, int(font.size * (0.22 if use_relaxed_multiline_height else 0.15)))
    total_height = sum(heights) + line_gap * max(0, len(lines) - 1)
    return max(widths, default=0), total_height


def render_lines(
    draw: Any,
    lines: Sequence[str],
    font: Any,
    bbox: tuple[int, int, int, int],
    fill: tuple[int, int, int, int],
    stroke_fill: tuple[int, int, int, int],
    stroke_width: int,
) -> None:
    left, top, right, bottom = bbox
    box_width = max(1, right - left)
    box_height = max(1, bottom - top)
    use_relaxed_multiline_height = len(lines) > 1
    line_gap = max(2, int(font.size * (0.22 if use_relaxed_multiline_height else 0.15)))

    metrics = []
    for line in lines:
        line_bbox = draw.textbbox((0, 0), line or "Ay", font=font, stroke_width=stroke_width)
        glyph_height = line_bbox[3] - line_bbox[1]
        line_height = get_effective_line_height(font, glyph_height, stroke_width) if use_relaxed_multiline_height else glyph_height
        metrics.append((line_bbox[2] - line_bbox[0], line_height))

    total_height = sum(height for _width, height in metrics) + line_gap * max(0, len(metrics) - 1)
    cursor_y = top + max(0, (box_height - total_height) / 2)

    for line, (line_width, line_height) in zip(lines, metrics, strict=True):
        cursor_x = left + max(0, (box_width - line_width) / 2)
        draw.text(
            (cursor_x, cursor_y),
            line,
            font=font,
            fill=fill,
            stroke_width=stroke_width,
            stroke_fill=stroke_fill,
        )
        cursor_y += line_height + line_gap


def resolve_font_path(font_path: str | None) -> Path:
    candidates = [Path(font_path)] if font_path else []
    windir = Path(os.environ.get("WINDIR", r"C:\Windows"))
    candidates.extend(
        [
            windir / "Fonts" / "msyh.ttc",
            windir / "Fonts" / "msyh.ttf",
            windir / "Fonts" / "simhei.ttf",
            windir / "Fonts" / "simsun.ttc",
            windir / "Fonts" / "arial.ttf",
        ]
    )

    for candidate in candidates:
        if candidate.exists():
            return candidate

    raise FileNotFoundError(
        "No usable font file was found. Pass --font-path explicitly, for example a .ttf or .ttc file."
    )


def main(argv: Sequence[str] | None = None) -> int:
    args = parse_args(argv or sys.argv[1:])
    pipeline = ImageTranslationPipeline(args)
    return pipeline.run()


if __name__ == "__main__":
    raise SystemExit(main())