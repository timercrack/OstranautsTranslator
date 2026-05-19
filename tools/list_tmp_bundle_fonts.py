import argparse
import datetime as dt
import pathlib
import unicodedata

import UnityPy


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Statically list TMP font assets stored in a UnityFS bundle.")
    parser.add_argument("bundle_path", help="Path to the UnityFS asset bundle, for example notosans.")
    parser.add_argument(
        "--output-dir",
        help="Optional directory used to write <bundle>.report.txt and <bundle>.merged.txt.")
    return parser.parse_args()


def build_object_name_map(env) -> dict[int, str]:
    object_names: dict[int, str] = {}
    for obj in env.objects:
        try:
            data = obj.read()
        except Exception:
            continue

        name = getattr(data, "name", "") or ""
        if name:
            object_names[obj.path_id] = name
            continue

        try:
            tree = obj.read_typetree()
        except Exception:
            continue

        tree_name = tree.get("m_Name") or tree.get("name")
        if isinstance(tree_name, str) and tree_name:
            object_names[obj.path_id] = tree_name

    return object_names


def get_font_assets(env, object_names: dict[int, str]) -> list[dict]:
    fonts: list[dict] = []
    for obj in env.objects:
        if getattr(obj.type, "name", str(obj.type)) != "MonoBehaviour":
            continue

        try:
            tree = obj.read_typetree()
        except Exception:
            continue

        character_table = tree.get("m_CharacterTable")
        if not isinstance(character_table, list):
            continue

        name = tree.get("m_Name") or tree.get("name") or f"MonoBehaviour#{obj.path_id}"
        atlas_names = resolve_pointer_names(tree.get("m_AtlasTextures"), object_names)
        source_font_file = resolve_source_font_file(tree)
        characters = extract_characters(character_table)
        fonts.append(
            {
                "name": str(name),
                "source_font_file": source_font_file,
                "atlas_textures": atlas_names,
                "characters": characters,
                "char_count": count_text_elements(characters),
                "path_id": obj.path_id,
            }
        )

    fonts.sort(key=lambda item: item["name"].lower())
    return fonts


def resolve_pointer_names(value, object_names: dict[int, str]) -> list[str]:
    if not isinstance(value, list):
        return []

    names: list[str] = []
    for entry in value:
        if not isinstance(entry, dict):
            continue

        path_id = entry.get("m_PathID")
        if isinstance(path_id, int) and path_id in object_names:
            names.append(object_names[path_id])

    return dedupe_preserve_order(names)


def resolve_source_font_file(tree: dict) -> str:
    creation_settings = tree.get("m_CreationSettings")
    if not isinstance(creation_settings, dict):
        return ""

    value = creation_settings.get("sourceFontFileName") or creation_settings.get("m_SourceFontFileName")
    return value if isinstance(value, str) else ""


def extract_characters(character_table: list) -> str:
    characters: list[str] = []
    seen: set[int] = set()
    for entry in character_table:
        unicode_value = extract_unicode(entry)
        if unicode_value is None or unicode_value in seen:
            continue
        if unicode_value <= 0 or unicode_value > 0x10FFFF:
            continue

        text = chr(unicode_value)
        if not should_keep_text_element(text):
            continue

        seen.add(unicode_value)
        characters.append(text)

    return "".join(characters)


def extract_unicode(entry) -> int | None:
    if isinstance(entry, dict):
        for key in ("unicode", "m_Unicode"):
            value = entry.get(key)
            if isinstance(value, int):
                return value

    return None


def should_keep_text_element(text: str) -> bool:
    if not text:
        return False

    category = unicodedata.category(text)
    if category in {"Cc", "Cf", "Cs", "Co", "Cn", "Zl", "Zp"}:
        return False
    if category == "Zs":
        return ord(text) in {0x20, 0x3000}
    return True


def count_text_elements(text: str) -> int:
    return len(text)


def dedupe_preserve_order(values: list[str]) -> list[str]:
    seen: set[str] = set()
    result: list[str] = []
    for value in values:
        if value in seen:
            continue
        seen.add(value)
        result.append(value)
    return result


def format_report(bundle_path: pathlib.Path, fonts: list[dict], merged_characters: str) -> str:
    lines = [
        f"GeneratedAtUtc: {dt.datetime.now(dt.UTC).isoformat().replace('+00:00', 'Z')}",
        f"Bundle: {bundle_path.name}",
        f"FontCount: {len(fonts)}",
        f"MergedCharCount: {count_text_elements(merged_characters)}",
        "",
    ]

    for font in fonts:
        lines.append(font["name"])
        lines.append(f"  charCount: {font['char_count']}")
        lines.append(f"  pathId: {font['path_id']}")
        if font["source_font_file"]:
            lines.append(f"  sourceFontFile: {font['source_font_file']}")
        if font["atlas_textures"]:
            lines.append(f"  atlasTextures: {', '.join(font['atlas_textures'])}")
        lines.append("")

    return "\n".join(lines)


def main() -> int:
    args = parse_args()
    bundle_path = pathlib.Path(args.bundle_path).expanduser().resolve()
    env = UnityPy.load(str(bundle_path))
    object_names = build_object_name_map(env)
    fonts = get_font_assets(env, object_names)
    merged_characters = "".join(font["characters"] for font in fonts)

    report_text = format_report(bundle_path, fonts, merged_characters)
    print(report_text)

    if args.output_dir:
        output_dir = pathlib.Path(args.output_dir).expanduser().resolve()
        output_dir.mkdir(parents=True, exist_ok=True)
        stem = bundle_path.name
        report_path = output_dir / f"{stem}.report.txt"
        merged_path = output_dir / f"{stem}.merged.txt"
        report_path.write_text(report_text, encoding="utf-8")
        merged_path.write_text(merged_characters, encoding="utf-8")
        print(f"Wrote: {report_path}")
        print(f"Wrote: {merged_path}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())