from __future__ import annotations

"""生成 standalone 语言字符集 txt 文件。

当前脚本会输出这些语言/文件：
Simplified Chinese		简体中文
Traditional Chinese		繁體中文
Japanese				日本語
Korean					한국어
Russian					Русский
Ukrainian				Українська
French					Français
German					Deutsch
Italian					Italiano
Spanish (Spain)			Español (España)
Spanish (Latin America)	Español (Latinoamérica)
Portuguese				Português
Portuguese (Brazil)		Português (Brasil)
Polish					Polski
Turkish					Türkçe
Indonesian				Bahasa Indonesia
Vietnamese				Tiếng Việt
Thai					ไทย
Arabic					العربية	
Nordic / Latin Extended	北欧/拉丁扩展

| 简体中文 | English | 本地语言名 | 文件名 |
| --- | --- | --- | --- |
| 简体中文 | Simplified Chinese | 简体中文 | `zh_CN.txt` |
| 繁体中文 | Traditional Chinese | 繁體中文 | `zh_TW.txt` |
| 日语 | Japanese | 日本語 | `jp.txt` |
| 韩语 | Korean | 한국어 | `ko.txt` |
| 俄语 | Russian | Русский | `ru.txt` |
| 乌克兰语 | Ukrainian | Українська | `uk.txt` |
| 法语 | French | Français | `fr.txt` |
| 德语 | German | Deutsch | `de.txt` |
| 意大利语 | Italian | Italiano | `it.txt` |
| 西班牙语（西班牙） | Spanish (Spain) | Español (España) | `es.txt` |
| 西班牙语（拉丁美洲） | Spanish (Latin America) | Español (Latinoamérica) | `es_419.txt` |
| 葡萄牙语 | Portuguese | Português | `pt.txt` |
| 葡萄牙语（巴西） | Portuguese (Brazil) | Português (Brasil) | `pt_BR.txt` |
| 波兰语 | Polish | Polski | `pl.txt` |
| 土耳其语 | Turkish | Türkçe | `tr.txt` |
| 印度尼西亚语 | Indonesian | Bahasa Indonesia | `id.txt` |
| 越南语 | Vietnamese | Tiếng Việt | `vi.txt` |
| 泰语 | Thai | ไทย | `th.txt` |
| 阿拉伯语 | Arabic | العربية | `ar.txt` |
| 北欧/拉丁扩展 | Nordic / Latin Extended | - | `nordic.txt` |
| 数字 | Digits | - | `digits.txt` |
| 标点/符号/空白 | Punctuation / Symbols / Spaces | - | `punctuation.txt` |
| 全量合并去重 | All merged unique chars | - | `all.txt` |

除 digits/punctuation/all 外，其余语言文件都会输出可单独使用的完整 standalone 字符集。
"""

import argparse
import sys
import unicodedata
from pathlib import Path
from typing import Iterable


REPO_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_SOURCE_PATH = REPO_ROOT / "tmp" /"7000+symbols.txt"
DEFAULT_OUTPUT_DIR = REPO_ROOT / "tmp" / "generated-charsets"
DEFAULT_SPLIT_MARKER = "萬與豐價優體傭兒黨準幾兇劃廠臺"
HAN_RANGES = (
    (0x3400, 0x4DBF),
    (0x4E00, 0x9FFF),
    (0xF900, 0xFAFF),
    (0x20000, 0x2A6DF),
    (0x2A700, 0x2B73F),
    (0x2B740, 0x2B81F),
    (0x2B820, 0x2CEAF),
    (0x2CEB0, 0x2EBEF),
    (0x30000, 0x3134F),
)
EXTRA_PUNCTUATION_SYMBOLS = frozenset("丨丶丿乛々〆〇ー")
EXTRA_SHARED_SYMBOLS = "•™"
RUNTIME_FONT_DUMP_FILE_NAME = "loaded-tmp-fonts.merged.txt"
USER_MANAGED_CHARSET_FILE_NAMES = ("zh_TW.txt", "ko.txt")

# 与 src/OstranautsTranslator.Tool/SupportedLanguageCatalog.cs 保持同步。
SUPPORTED_TARGET_LANGUAGES = (
    ("zh", "Simplified Chinese", "简体中文", "zh_CN.txt"),
    ("zh-TW", "Traditional Chinese", "繁體中文", "zh_TW.txt"),
    ("jp", "Japanese", "日本語", "jp.txt"),
    ("ko", "Korean", "한국어", "ko.txt"),
    ("ru", "Russian", "Русский", "ru.txt"),
    ("uk", "Ukrainian", "Українська", "uk.txt"),
    ("fr", "French", "Français", "fr.txt"),
    ("de", "German", "Deutsch", "de.txt"),
    ("it", "Italian", "Italiano", "it.txt"),
    ("es", "Spanish (Spain)", "Español (España)", "es.txt"),
    ("es-419", "Spanish (Latin America)", "Español (Latinoamérica)", "es_419.txt"),
    ("pt", "Portuguese", "Português", "pt.txt"),
    ("pt-BR", "Portuguese (Brazil)", "Português (Brasil)", "pt_BR.txt"),
    ("pl", "Polish", "Polski", "pl.txt"),
    ("tr", "Turkish", "Türkçe", "tr.txt"),
    ("id", "Indonesian", "Bahasa Indonesia", "id.txt"),
    ("vi", "Vietnamese", "Tiếng Việt", "vi.txt"),
    ("th", "Thai", "ไทย", "th.txt"),
    ("ar", "Arabic", "العربية", "ar.txt"),
)


def should_keep_char(ch: str) -> bool:
    category = unicodedata.category(ch)
    codepoint = ord(ch)
    if category[0] == "C" or category in {"Zl", "Zp"}:
        return False
    if category == "Zs":
        return codepoint in {0x20, 0x3000}
    return True


def ordered_unique(text: str) -> str:
    seen: set[str] = set()
    buffer: list[str] = []
    for ch in text:
        if not should_keep_char(ch):
            continue
        if ch not in seen:
            seen.add(ch)
            buffer.append(ch)
    return "".join(buffer)


def sanitize_runtime_font_dump_text(text: str) -> str:
    buffer: list[str] = []
    for ch in text:
        if should_keep_char(ch):
            buffer.append(ch)
    return "".join(buffer)


def read_charset(path: Path) -> str:
    text = path.read_text(encoding="utf-8-sig").replace("\r", "").replace("\n", "")
    return sanitize_runtime_font_dump_text(text)


def codepoints_to_text(codepoints: Iterable[int]) -> str:
    return "".join(chr(codepoint) for codepoint in codepoints)


def range_to_text(start: int, end: int) -> str:
    return codepoints_to_text(range(start, end + 1))


def recover_zh_cn_seed(source_text: str, split_marker: str) -> tuple[str, str]:
    if split_marker:
        marker_index = source_text.find(split_marker)
        if marker_index > 0:
            return source_text[:marker_index], f"检测到扩展边界标记，已从 {marker_index} 位置前截取 zh_CN 基底。"
    return source_text, "未检测到扩展边界标记，直接将输入文件视为 zh_CN 基底。"


def is_han_character(ch: str) -> bool:
    codepoint = ord(ch)
    return any(start <= codepoint <= end for start, end in HAN_RANGES)


def is_digit_char(ch: str) -> bool:
    return unicodedata.category(ch) == "Nd"


def is_punctuation_or_symbol_char(ch: str) -> bool:
    category = unicodedata.category(ch)
    return ch in EXTRA_PUNCTUATION_SYMBOLS or category[0] in {"P", "S"} or category == "Zs"


def split_common_charset(source_text: str) -> tuple[str, str, str]:
    common_letters: list[str] = []
    digits: list[str] = []
    punctuation: list[str] = []

    for ch in source_text:
        if is_han_character(ch):
            continue
        if is_digit_char(ch):
            digits.append(ch)
        elif is_punctuation_or_symbol_char(ch):
            punctuation.append(ch)
        else:
            common_letters.append(ch)

    return (
        ordered_unique("".join(common_letters)),
        ordered_unique("".join(digits)),
        ordered_unique("".join(punctuation)),
    )


def build_standalone_charset(common_letters: str, digits: str, punctuation: str, language_specific: str) -> str:
    return ordered_unique(common_letters + digits + punctuation + language_specific)


def build_punctuation_charset(*texts: str) -> str:
    return ordered_unique(
        "".join(ch for text in texts for ch in text if is_punctuation_or_symbol_char(ch)) + EXTRA_SHARED_SYMBOLS
    )


def build_digits_charset(*texts: str) -> str:
    return ordered_unique("".join(ch for text in texts for ch in text if is_digit_char(ch)))


def build_japanese_charset() -> str:
    hiragana = range_to_text(0x3041, 0x3096) + codepoints_to_text(
        [0x309B, 0x309C, 0x309D, 0x309E, 0x309F]
    )
    katakana = range_to_text(0x30A1, 0x30FA) + codepoints_to_text(
        [
            0x30A0,
            0x30FB,
            0x30FC,
            0x30FD,
            0x30FE,
            0x30FF,
            0x3005,
            0x3006,
            0x3007,
            0x300C,
            0x300D,
            0x300E,
            0x300F,
            0x301C,
        ]
    )
    katakana_ext = range_to_text(0x31F0, 0x31FF)
    halfwidth_katakana = range_to_text(0xFF66, 0xFF9D) + codepoints_to_text([0xFF9E, 0xFF9F])
    japanese_kanji_extras = codepoints_to_text(
        [
            0x50CD,
            0x8FBC,
            0x6803,
            0x7551,
            0x5CE0,
            0x69CA,
            0x51EA,
            0x8FBB,
            0x5302,
            0x96EB,
            0x680A,
            0x69D9,
            0x67A0,
            0x51E7,
            0x88FE,
            0x8974,
            0x5642,
        ]
    )
    return ordered_unique(hiragana + katakana + katakana_ext + halfwidth_katakana + japanese_kanji_extras)


def build_korean_charset() -> str:
    hangul_syllables = range_to_text(0xAC00, 0xD7A3)
    hangul_compat_jamo = range_to_text(0x3131, 0x318E)
    return ordered_unique(hangul_syllables + hangul_compat_jamo)


def build_russian_charset() -> str:
    return ordered_unique(
        codepoints_to_text([0x0401])
        + range_to_text(0x0410, 0x042F)
        + codepoints_to_text([0x0451])
        + range_to_text(0x0430, 0x044F)
        + codepoints_to_text([0x2116])
    )


def build_ukrainian_charset() -> str:
    return ordered_unique(
        "АБВГҐДЕЄЖЗИІЇЙКЛМНОПРСТУФХЦЧШЩЬЮЯ"
        "абвгґдеєжзиіїйклмнопрстуфхцчшщьюя"
    )


def build_turkish_charset() -> str:
    return ordered_unique("ÇĞİÖŞÜçğıöşü")


def build_indonesian_charset() -> str:
    return ""


def build_vietnamese_charset() -> str:
    base_letters = "AĂÂBCDĐEÊGHIKLMNOÔƠPQRSTUƯVXYaăâbcdđeêghiklmnoôơpqrstuưvxy"
    tone_bases = "AĂÂEÊIOÔƠUƯYaăâeêioôơuưy"
    tone_marks = ["", "\u0300", "\u0301", "\u0309", "\u0303", "\u0323"]

    builder: list[str] = [base_letters]
    for base in tone_bases:
        for tone_mark in tone_marks:
            builder.append(unicodedata.normalize("NFC", base + tone_mark))

    return ordered_unique("".join(builder))


def build_thai_charset() -> str:
    return ordered_unique(range_to_text(0x0E01, 0x0E5B))


def build_arabic_charset() -> str:
    punctuation = codepoints_to_text([0x060C, 0x061B, 0x061F, 0x066A, 0x066B, 0x066C, 0x066D])
    letters = range_to_text(0x0621, 0x063A) + range_to_text(0x0640, 0x064A)
    marks = range_to_text(0x064B, 0x0655) + codepoints_to_text([0x0670, 0x0671])
    digits = range_to_text(0x0660, 0x0669) + range_to_text(0x06F0, 0x06F9)
    extras = codepoints_to_text([0x067E, 0x0686, 0x0698, 0x06A4, 0x06AF])
    return ordered_unique(punctuation + letters + marks + digits + extras)


def build_nordic_charset() -> str:
    scandinavian_core = "ÆæØøÅåÄäÖö"
    icelandic_and_faroese = "ÁáÐðÉéÍíÓóÚúÝýÞþ"
    sami_core = "ČčĐđŊŋŠšŦŧŽž"
    sami_extended = "ÂâÃãÕõÏïǦǧǨǩǮǯĸʹ"
    return ordered_unique(scandinavian_core + icelandic_and_faroese + sami_core + sami_extended)


def build_european_charsets() -> dict[str, str]:
    return {
        "es.txt": ordered_unique("¡¿ªºÁÉÍÑÓÚÜáéíñóúü"),
        "es_419.txt": ordered_unique("¡¿ªºÁÉÍÑÓÚÜáéíñóúü"),
        "de.txt": ordered_unique("ÄÖÜẞßäöü„‚€"),
        "fr.txt": ordered_unique("«»‹›ÀÂÆÇÈÉÊËÎÏÔŒÙÛÜŸàâæçèéêëîïôœùûüÿ"),
        "pt.txt": ordered_unique("ÁÀÃÂÇÉÊÍÓÔÕÚÜáàãâçéêíóôõúü"),
        "pt_BR.txt": ordered_unique("ÁÀÃÂÇÉÊÍÓÔÕÚÜáàãâçéêíóôõúü"),
        "it.txt": ordered_unique("ÀÈÉÌÎÒÓÙàèéìîòóù"),
        "pl.txt": ordered_unique("ĄĆĘŁŃÓŚŹŻąćęłńóśźż"),
    }


def load_runtime_font_dump_charsets(output_dir: Path) -> dict[str, str]:
    charsets: dict[str, str] = {}
    path = output_dir / RUNTIME_FONT_DUMP_FILE_NAME
    if path.exists():
        content = ordered_unique(sanitize_runtime_font_dump_text(read_charset(path)))
        if content:
            charsets[path.name] = content
    return charsets


def load_user_managed_charsets(output_dir: Path) -> dict[str, str]:
    charsets: dict[str, str] = {}
    missing_files: list[str] = []

    for file_name in USER_MANAGED_CHARSET_FILE_NAMES:
        path = output_dir / file_name
        if not path.exists():
            missing_files.append(file_name)
            continue

        content = ordered_unique(read_charset(path))
        if content:
            charsets[file_name] = content

    if missing_files:
        missing = ", ".join(missing_files)
        raise FileNotFoundError(f"缺少需要直接复用的现有字符集文件：{missing}")

    return charsets


def write_charset(path: Path, content: str) -> None:
    path.write_text(content, encoding="utf-8")


def build_merged_all_charset(charsets: dict[str, str]) -> str:
    return ordered_unique("".join(charsets.values()))


def build_arg_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="从当前 7000+symbols.txt 还原 zh_CN 基底，并生成 standalone 各语言字符集 txt 与总合并 txt。"
    )
    parser.add_argument(
        "--source",
        type=Path,
        default=DEFAULT_SOURCE_PATH,
        help="输入字符集文件，默认使用仓库根目录下的 7000+symbols.txt",
    )
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=DEFAULT_OUTPUT_DIR,
        help="输出目录，默认写入 tmp/generated-charsets",
    )
    parser.add_argument(
        "--split-marker",
        default=DEFAULT_SPLIT_MARKER,
        help="用于从当前总字符集里自动切出 zh_CN 基底的边界标记。留空则不自动切分。",
    )
    return parser


def main() -> int:
    args = build_arg_parser().parse_args()
    source_path = args.source.resolve()
    output_dir = args.output_dir.resolve()

    if not source_path.exists():
        print(f"输入文件不存在：{source_path}", file=sys.stderr)
        return 1

    source_text = ordered_unique(read_charset(source_path))
    zh_cn_seed, recovery_message = recover_zh_cn_seed(source_text, args.split_marker)
    zh_cn_seed = ordered_unique(zh_cn_seed)
    common_letters, common_digits, common_punctuation = split_common_charset(zh_cn_seed)

    jp = build_standalone_charset(common_letters, common_digits, common_punctuation, build_japanese_charset())
    ru = build_standalone_charset(common_letters, common_digits, common_punctuation, build_russian_charset())
    uk = build_standalone_charset(common_letters, common_digits, common_punctuation, build_ukrainian_charset())
    tr = build_standalone_charset(common_letters, common_digits, common_punctuation, build_turkish_charset())
    id_charset = build_standalone_charset(common_letters, common_digits, common_punctuation, build_indonesian_charset())
    vi = build_standalone_charset(common_letters, common_digits, common_punctuation, build_vietnamese_charset())
    th = build_standalone_charset(common_letters, common_digits, common_punctuation, build_thai_charset())
    ar = build_standalone_charset(common_letters, common_digits, common_punctuation, build_arabic_charset())
    nordic = build_standalone_charset(common_letters, common_digits, common_punctuation, build_nordic_charset())
    european = build_european_charsets()

    charsets: dict[str, str] = {
        "zh_CN.txt": zh_cn_seed,
        "jp.txt": jp,
        "ru.txt": ru,
        "uk.txt": uk,
        **{
            file_name: build_standalone_charset(common_letters, common_digits, common_punctuation, content)
            for file_name, content in european.items()
        },
        "tr.txt": tr,
        "id.txt": id_charset,
        "vi.txt": vi,
        "th.txt": th,
        "ar.txt": ar,
        "nordic.txt": nordic,
    }

    charsets.update(load_user_managed_charsets(output_dir))

    charsets["digits.txt"] = build_digits_charset(*charsets.values(), common_digits)
    charsets["punctuation.txt"] = build_punctuation_charset(*charsets.values())

    runtime_font_dumps = load_runtime_font_dump_charsets(output_dir)
    if runtime_font_dumps:
        charsets.update(runtime_font_dumps)

    merged = build_merged_all_charset(charsets)
    charsets["all.txt"] = merged

    output_dir.mkdir(parents=True, exist_ok=True)
    for file_name, content in charsets.items():
        if file_name in USER_MANAGED_CHARSET_FILE_NAMES:
            continue
        write_charset(output_dir / file_name, content)

    print(recovery_message)
    print(f"输入文件：{source_path}")
    print(f"输出目录：{output_dir}")
    preserved_names = ", ".join(USER_MANAGED_CHARSET_FILE_NAMES)
    print(
        "说明：当前为 standalone 模式；除手工维护文件外，其余语言 txt 会自动生成；"
        f"digits/punctuation 为独立公共文件；{preserved_names} 会直接复用现有文件内容，不会被脚本覆盖；all.txt 会合并它们。"
    )
    if runtime_font_dumps:
        print(f"检测到 {len(runtime_font_dumps)} 个运行时导出的 TMP 字体字符集文件，并已并入 all.txt。")
    print()
    for file_name, content in charsets.items():
        print(f"{file_name}: {len(content)}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
