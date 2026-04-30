from __future__ import annotations

import csv
import json
import re
import sys
from collections import Counter
from pathlib import Path
from typing import Any, Iterable

DEFAULT_DATA_ROOT = Path(r"g:\SteamLibrary\steamapps\common\Ostranauts\Ostranauts_Data\StreamingAssets\data")
DEFAULT_OUTPUT_PATH = Path(r"g:\SteamLibrary\steamapps\common\Ostranauts\OstranautsTranslator\workspace\reference\glossary.json")

CATEGORIES = ("people", "places", "entities", "ships", "items")

EXTRA_PEOPLE = (
    "Gideon Pruitt",
    "Camellia Middlemist",
    "Harbourmaster Adeyemi",
    "Dalisay",
    "Chen Yue",
    "Taylor Ejogo",
    "Nwabudike Morgan",
    "Josephus Tanner",
    "Valeria Valencia",
)

EXTRA_PLACES = (
    "K-Leg",
    "Nnamdi Azikiwe Station",
    "Ring Station",
    "Quanzhan",
    "Mescaform",
    "Old Emporium",
    "Terrace Gardens",
    "West Lake",
)

EXTRA_ENTITIES = (
    "Ayotimiwa",
    "GalCon",
    "CCRE",
    "Black Bull Kape",
    "Bismertnaya",
    "Damask Rose",
    "Unicorn Dream",
    "Oneirotix",
    "Renbao PDA",
    "R3N-B00",
    "Ogiso's Register",
    "Mobile Space Systems",
    "DuraFlor Int",
    "Titan Shipyards",
    "Zhuangzi",
    "Testudo",
    "Van Hummel",
    "Miura",
    "Weber",
    "Green Energy Company",
    "Peacock Pictures",
    "Ubiq Mobile",
    "Renske International",
)

PEOPLE_EXACT_BLACKLIST = {
    "playerRand",
    "OKLG Port Authority",
}

PLACE_EXACT_BLACKLIST = {
    "Vessel",
    "Station Sink",
}

PLACE_PREFIX_BLACKLIST = (
    "Poster:",
    "T-shirt:",
    "Leggings:",
    "Embassy Services:",
    "Port Authority ",
)

PLACE_SUBSTRING_BLACKLIST = (
    "History",
    "Statistics",
    "Government",
    "Crime",
    "Smartphone",
)

ENTITY_PREFIX_BLACKLIST = (
    "Floor",
    "Wall",
    "Whipple Framework",
    "Conduit",
    "Battery:",
    "Bottle of",
    "Box:",
    "Pill",
    "Permit:",
    "Shoe:",
    "RCS ",
    "Seat:",
    "Tote:",
    "Treadmill:",
    "Travel ",
    "Gig Nexus",
    "Faction ",
    "Cargo Trade",
    "Career",
    "Medical",
    "Transit",
    "Furnishings",
    "Real Estate",
    "Supply",
    "Clothes",
    "Shoes",
    "Feature Vote",
)

ENTITY_SUBSTRING_BLACKLIST = (
    ".BIN",
    ".TXT",
    ".VID",
    ".SND",
    ".IMG",
    "History",
    "Statistics",
    "Marketing",
    "Infrastructure",
    "Technology",
    "Catalogue",
    "Controversy",
    "Layout",
    "Overview",
    "Piracy",
    "Advertisement",
    "Lucky says",
    "Role in",
    "Communications",
    "West Lake",
    "Boneyard",
    "Shipbreaker's Yard",
    "Technical Design",
    "DataGeneric",
    "University",
    "Twilight's Last Gleaming",
    "Personality Matrices",
    "Survivor Sensation",
)

ENTITY_EXACT_BLACKLIST = {
    "n/a",
    "Custom",
    "Cargo",
    "ASC",
    "BNW",
    "BIN",
    "White",
    "Orange",
    "Europa",
    "Atlantis",
    "Hangzhou",
    "Jade Rabbit",
    "Newcal",
    "Prokofiev",
    "Tharsis Landing",
    "Virginia",
    "Voltaire",
    "Mescaform",
    "Ship Broker",
    "Faction",
    "Self Care",
    "Aerostat Scrap",
    "Flotilla Scrap",
    "OKLG Licensed Supply",
    "OKLG Scrap",
}

SHIP_SUBSTRING_BLACKLIST = (
    "PAX2020",
    "Chargen",
    "Hull Batch",
    "Room",
    "Field",
    "Projectile",
    "Missile",
    "Buoy",
    "Waypoint",
    "Station",
    "break zone",
    "ReactorComponents",
    "Normals",
    "Repairshop",
    "SignalBeacon",
    "Entrance",
    "Bureaus",
    "Roof",
    "Bridge",
    "Club",
    "HullBatch",
    "Pilots",
)

GENERIC_STOP_TERMS = {
    "Panel A",
    "Panel B",
    "Panel C",
    "Panel D",
    "Panel E",
    "Panel F",
    "Electrical",
    "Inventory",
    "DropItem",
    "PickupItem",
    "Sensor",
    "Nav Controls",
    "DNA Samples",
    "Biological Samples from Victim",
    "Cigarette Box",
    "Cigarette",
    "Cigarette Stub",
    "Hygiene Pack",
    "DataStore",
    "DEFAULT",
    "Normal",
    "Melee",
}

VARIANT_SUFFIX_RE = re.compile(r"\s+\((?:Lit|Loose|Damaged|Patched|Off)\)$")
MULTISPACE_RE = re.compile(r"\s+")
PROPER_PHRASE_RE = re.compile(
    r"""
    (?<!\[)
    (?:
        [A-Z][A-Za-z0-9'’.-]*
        |[A-Z]{2,}[A-Za-z0-9-]*
        |[A-Za-z]+-[A-Za-z0-9]+(?:-[A-Za-z0-9]+)*
        |[A-Z]\d+[A-Za-z-]*
        |\d+[A-Za-z][A-Za-z0-9-]*
    )
    (?:
        \s+
        (?:of|the|and|de|do|da|del|la|le|van|von|der|du|y)?
        \s*
        (?:
            [A-Z][A-Za-z0-9'’.-]*
            |[A-Z]{2,}[A-Za-z0-9-]*
            |[A-Za-z]+-[A-Za-z0-9]+(?:-[A-Za-z0-9]+)*
            |[A-Z]\d+[A-Za-z-]*
            |\d+[A-Za-z][A-Za-z0-9-]*
        )
    ){0,4}
    """,
    re.VERBOSE,
)

SHIP_SKIP_SUBSTRINGS = (
    "test",
    "debug",
    "training",
    "template",
    "alltiles",
    "cluster",
    "chromastronauts",
    "lootspawn",
    "fight",
    "weight",
    "explosion",
    "scaffold",
    "dock",
    "floor batch",
    "room alpha",
    "storageall",
    "robottestfacility",
)

SKIP_PATH_PARTS = {
    "schemas",
    "tokens",
    "verbs",
    "names_first",
    "names_last",
    "names_full",
    "names_robots",
    "names_ship",
    "names_ship_adjectives",
    "names_ship_nouns",
}

PLACE_HINTS = (
    "Station",
    "Orbital",
    "Landing",
    "Base",
    "City",
    "Mine",
    "Port",
    "Flotilla",
    "Labyrinth",
    "Emporium",
    "Lounge",
    "Gardens",
    "District",
)

ENTITY_HINTS = (
    "Company",
    "Corp",
    "Corporation",
    "Shipyards",
    "Security",
    "Systems",
    "Register",
    "Command",
    "Electric",
    "Industrial",
    "Industries",
    "Council",
    "Works",
    "Union",
    "Optics",
    "PDA",
)

ITEM_HINTS = (
    "Cigarette Box",
    "Cigarette",
    "Vodka",
    "Permit",
    "Laser",
    "Regulator",
    "Torch",
    "Kiosk",
    "Poster",
    "T-shirt",
    "PDA",
)

DATAFILE_SUFFIX_PATTERNS = [
    re.compile(r"\s+History(?:\s+pt\s*\d+)?$", re.IGNORECASE),
    re.compile(r"\s+Statistics$", re.IGNORECASE),
    re.compile(r"\s+Infrastructure(?:\s+and\s+Development)?$", re.IGNORECASE),
    re.compile(r"\s+Development\s+&\s+Decay$", re.IGNORECASE),
    re.compile(r"\s+Marketing(?:\s+Controversy)?$", re.IGNORECASE),
    re.compile(r"\s+Technology(?:\s+Overview)?$", re.IGNORECASE),
    re.compile(r"\s+Technical\s+Capabilities$", re.IGNORECASE),
    re.compile(r"\s+Tourism(?:\s+&\s+Immigration)?$", re.IGNORECASE),
    re.compile(r"\s+Role$", re.IGNORECASE),
    re.compile(r"\s+Layout$", re.IGNORECASE),
    re.compile(r"\s+Phenomenology$", re.IGNORECASE),
    re.compile(r"\s+Ship\s+Ratings$", re.IGNORECASE),
    re.compile(r"\s+Insurance$", re.IGNORECASE),
    re.compile(r"\s+Security$", re.IGNORECASE),
    re.compile(r"\s+Notable\s+Inmates$", re.IGNORECASE),
    re.compile(r"\s+Catalogue$", re.IGNORECASE),
    re.compile(r"\s+Overview$", re.IGNORECASE),
]

ENTITY_FROM_NAME_PATTERNS = [
    re.compile(r'^Poster:\s+(?P<term>.+?)(?:\s+\(.*\))?$'),
    re.compile(r'^T-shirt:\s+(?P<term>.+?)(?:\s+\(.*\))?$'),
    re.compile(r'^Terminal\s+\((?P<term>.+?)\)$'),
    re.compile(r'^(?P<term>.+?)\s+Kiosk(?:\s+\(.*\))?$'),
    re.compile(r'^(?:Wall|Floor|Whipple Framework).*?:\s+(?P<term>[^"]+?)\s+"[^"]+"$'),
    re.compile(r'^(?P<term>.+?)\s+Cigarette(?:\s+Box)?(?:\s+\(Lit\))?$'),
    re.compile(r'^Bottle of\s+"?(?P<term>.+?)"?\s+Vodka$'),
    re.compile(r'^(?P<term>.+?)\s+Vodka(?:\s+.+)?$'),
    re.compile(r'^(?P<term>.+?)\s+Salvage Permit$'),
    re.compile(r'^(?P<term>.+?)\s+PDA(?:\s+.+)?$'),
    re.compile(r'^(?P<term>.+?)\s+Optics$'),
    re.compile(r'^(?P<term>.+?)\s+Laser Torch$'),
    re.compile(r'^(?P<term>.+?)\s+Fusion Torch Ignition Laser$'),
    re.compile(r'^(?P<term>.+?)\s+"[^"]+"\s+.+$'),
]


def normalize_term(term: str) -> str:
    value = term.replace("\u201c", '"').replace("\u201d", '"').replace("\u2019", "'")
    value = MULTISPACE_RE.sub(" ", value.strip())
    value = value.strip(" \t\r\n,;:.!?/")
    value = VARIANT_SUFFIX_RE.sub("", value)
    value = MULTISPACE_RE.sub(" ", value.strip())
    return value


def looks_like_internal_identifier(term: str) -> bool:
    if not term:
        return True
    if "[" in term or "]" in term:
        return True
    if term.startswith(("Plot_", "PLOT_", "CT", "TIs", "TREL", "CNDO", "CNDOL", "ACT", "Itm", "CO-")):
        return True
    if re.fullmatch(r"[A-Z_0-9-]{4,}", term) and not re.search(r"[a-z]", term):
        return True
    return False


def is_low_value_term(term: str) -> bool:
    if not term:
        return True
    if term in GENERIC_STOP_TERMS:
        return True
    if looks_like_internal_identifier(term):
        return True
    if len(term) < 2:
        return True
    if re.fullmatch(r"[\W_]+", term):
        return True
    return False


def strip_item_variants(term: str) -> str:
    value = normalize_term(term)
    value = re.sub(r"\s+\((?:Lit|Loose|Damaged|Patched|Off)\)$", "", value)
    return value


class GlossaryBuilder:
    def __init__(self) -> None:
        self.terms = {category: Counter() for category in CATEGORIES}
        self.all_texts: list[str] = []

    def add(self, category: str, term: str, weight: int = 1) -> None:
        if category not in self.terms:
            raise KeyError(category)
        normalized = normalize_term(term)
        if is_low_value_term(normalized):
            return
        self.terms[category][normalized] += weight

    def add_text(self, text: str) -> None:
        normalized = MULTISPACE_RE.sub(" ", text.strip())
        if normalized and "[" not in normalized:
            self.all_texts.append(normalized)

    def process_all(self, data_root: Path) -> None:
        for path in sorted(data_root.rglob("*")):
            if not path.is_file() or path.suffix.lower() not in {".json", ".tsv"}:
                continue
            self.process_file(path, data_root)

        self.add_extras()

    def add_extras(self) -> None:
        for term in EXTRA_PEOPLE:
            self.add("people", term, weight=5)
        for term in EXTRA_PLACES:
            self.add("places", term, weight=5)
        for term in EXTRA_ENTITIES:
            self.add("entities", term, weight=5)

    def process_file(self, path: Path, data_root: Path) -> None:
        relative_parts = {part.lower() for part in path.relative_to(data_root).parts}
        if relative_parts & SKIP_PATH_PARTS:
            return

        if path.suffix.lower() == ".json":
            self.process_json(path)
        else:
            self.process_tsv(path)

    def process_json(self, path: Path) -> None:
        try:
            with path.open("r", encoding="utf-8-sig") as handle:
                payload = json.load(handle)
        except json.JSONDecodeError:
            return

        lower_parts = [part.lower() for part in path.parts]
        lower_path = "/".join(lower_parts)

        if "/homeworlds/" in lower_path:
            self.process_homeworlds(payload)
        if "/personspecs/" in lower_path:
            self.process_personspecs(payload)
        if "/ships/" in lower_path:
            self.process_ship_file(path, payload)
        if path.name.lower() == "conditions_simple.json":
            self.process_conditions_simple(payload)

        self.walk_json(path, payload, ())

    def process_tsv(self, path: Path) -> None:
        try:
            with path.open("r", encoding="utf-8-sig", newline="") as handle:
                reader = csv.reader(handle, delimiter="\t")
                for row in reader:
                    for cell in row:
                        value = normalize_term(cell)
                        if value:
                            self.add_text(value)
        except UnicodeDecodeError:
            return

    def process_homeworlds(self, payload: Any) -> None:
        if not isinstance(payload, list):
            return
        for entry in payload:
            if not isinstance(entry, dict):
                continue
            colony_name = entry.get("strColonyName")
            if not isinstance(colony_name, str):
                continue
            primary = normalize_term(colony_name.split(",", 1)[0])
            if not primary:
                continue
            self.add("places", primary, weight=3)
            for part in re.split(r"\s*/\s*", primary):
                if part and part != primary:
                    self.add("places", part, weight=2)

    def process_personspecs(self, payload: Any) -> None:
        if not isinstance(payload, list):
            return
        for entry in payload:
            if not isinstance(entry, dict):
                continue
            first = normalize_term(str(entry.get("strFirstName") or ""))
            last = normalize_term(str(entry.get("strLastName") or ""))
            if first and last:
                person = f"{first} {last}"
                if self.keep_person(person):
                    self.add("people", person, weight=3)
            elif first:
                if self.keep_person(first):
                    self.add("people", first, weight=2)
            elif last:
                if self.keep_person(last):
                    self.add("people", last, weight=2)

    def process_ship_file(self, path: Path, payload: Any) -> None:
        ship_name = None
        make_name = None
        if isinstance(payload, list) and payload and isinstance(payload[0], dict):
            ship_name = payload[0].get("strName")
            make_name = payload[0].get("make")
        elif isinstance(payload, dict):
            ship_name = payload.get("strName")
            make_name = payload.get("make")

        stem = path.stem
        if isinstance(ship_name, str):
            stem = normalize_term(ship_name)

        lower_stem = stem.lower()
        if not stem.startswith("_") and not any(token in lower_stem for token in SHIP_SKIP_SUBSTRINGS):
            if not lower_stem.startswith("station_") and not lower_stem.startswith("res"):
                self.add("ships", stem, weight=3)

        if isinstance(make_name, str):
            self.add("entities", make_name, weight=2)

    def process_conditions_simple(self, payload: Any) -> None:
        if not isinstance(payload, list):
            return
        for entry in payload:
            if not isinstance(entry, list) or len(entry) < 3:
                continue
            name = entry[1]
            desc = entry[2]
            if isinstance(name, str) and isinstance(desc, str) and "brand item" in desc.lower():
                self.add("entities", name, weight=2)

    def walk_json(self, path: Path, node: Any, key_path: tuple[str, ...]) -> None:
        if isinstance(node, dict):
            for key, value in node.items():
                self.walk_json(path, value, (*key_path, key))
            return

        if isinstance(node, list):
            for value in node:
                self.walk_json(path, value, key_path)
            return

        if not isinstance(node, str):
            return

        key = key_path[-1] if key_path else ""
        value = normalize_term(node)
        if not value:
            return

        lower_path = "/".join(part.lower() for part in path.parts)
        lower_key = key.lower()

        if lower_key in {"strdesc", "description"}:
            return

        if lower_key == "strcolonyname":
            self.add("places", value.split(",", 1)[0], weight=3)
            return

        if lower_key in {"strnamefriendly", "strfriendlyname"}:
            self.handle_display_name(path, value)
            return

        if lower_key == "strtitle":
            if any(part in lower_path for part in ("/ads/", "/interactions/interactions_datafiles", "/headlines/")):
                self.handle_display_name(path, value)
            return

        if lower_key == "strname":
            if "/ads/" in lower_path:
                self.handle_display_name(path, value)
            return

        if lower_key == "make":
            if "/ships/" in lower_path:
                self.add("entities", value, weight=2)
            return

        if lower_key in {"strfirstname", "strlastname"}:
            return

        if path.suffix.lower() == ".tsv":
            self.add_text(value)

    def handle_display_name(self, path: Path, value: str) -> None:
        lower_path = "/".join(part.lower() for part in path.parts)
        term = strip_item_variants(value)
        if is_low_value_term(term):
            return

        if "/homeworlds/" in lower_path:
            self.add("places", term, weight=3)
            return

        if "/cooverlays/" in lower_path or "/guipropmaps/" in lower_path:
            entity_term = self.extract_entity_from_name(term)
            if entity_term:
                self.add("entities", entity_term, weight=2)
            return

        if any(part in lower_path for part in ("/items/", "/condowners/", "/jobitems/")):
            if self.looks_like_named_item(term):
                self.add("items", term, weight=2)
                entity_term = self.extract_entity_from_name(term)
                if entity_term:
                    self.add("entities", entity_term, weight=2)
            return

        if any(part in lower_path for part in ("/ads/", "/interactions/interactions_datafiles", "/headlines/")):
            self.classify_topic_term(term)
            return

    def classify_topic_term(self, term: str) -> None:
        term = self.normalize_topic_term(term)
        if is_low_value_term(term):
            return
        if self.looks_like_named_place(term):
            self.add("places", term)
            return
        if self.looks_like_named_item(term):
            self.add("items", term)
            entity_term = self.extract_entity_from_name(term)
            if entity_term:
                self.add("entities", entity_term)
            return
        self.add("entities", term)

    def normalize_topic_term(self, term: str) -> str:
        value = normalize_term(term)
        if "," in value:
            value = normalize_term(value.split(",", 1)[0])
        for pattern in DATAFILE_SUFFIX_PATTERNS:
            value = normalize_term(pattern.sub("", value))
        return value

    def looks_like_named_place(self, term: str) -> bool:
        if is_low_value_term(term):
            return False
        if term in {"K-Leg", "The Flotilla", "Mescaform", "Old Emporium", "Terrace Gardens"}:
            return True
        if any(hint in term for hint in PLACE_HINTS):
            return True
        if re.search(r"\b(?:Mars|Venus|Mercury|Luna|Earth|Europa|Ganymede|Ceres|Titan|Deimos)\b", term):
            return True
        return False

    def looks_like_entity(self, term: str) -> bool:
        if is_low_value_term(term):
            return False
        if any(hint in term for hint in ENTITY_HINTS):
            return True
        if term in {
            "Ayotimiwa",
            "GalCon",
            "CCRE",
            "Testudo",
            "Van Hummel",
            "Miura",
            "Weber",
            "Zhuangzi",
            "Bismertnaya",
            "Damask Rose",
            "Unicorn Dream",
            "Black Bull Kape",
            "Oneirotix",
            "R3N-B00",
            "Renbao PDA",
            "DuraFlor Int",
            "Mobile Space Systems",
            "Titan Shipyards",
            "Ogiso's Register",
        }:
            return True
        return False

    def looks_like_named_item(self, term: str) -> bool:
        if is_low_value_term(term):
            return False
        if term.startswith(("Poster:", "T-shirt:", "Wall:", "Floor:", "Whipple Framework")):
            return False
        if any(hint in term for hint in ITEM_HINTS):
            return True
        if '"' in term and not any(prefix in term for prefix in ("Wall:", "Floor:")):
            return True
        return False

    def extract_entity_from_name(self, term: str) -> str | None:
        for pattern in ENTITY_FROM_NAME_PATTERNS:
            match = pattern.match(term)
            if match:
                candidate = normalize_term(match.group("term"))
                if not is_low_value_term(candidate):
                    return candidate
        return None

    def to_payload(self) -> dict[str, list[str]]:
        raw_payload: dict[str, list[str]] = {}
        for category, counter in self.terms.items():
            raw_payload[category] = [
                term
                for term, _ in sorted(
                    counter.items(),
                    key=lambda item: (-item[1], item[0].lower()),
                )
            ]
        return self.clean_payload(raw_payload)

    def clean_payload(self, payload: dict[str, list[str]]) -> dict[str, list[str]]:
        people = [term for term in payload["people"] if self.keep_person(term)]
        places = [term for term in payload["places"] if self.keep_place(term)]
        entities = [term for term in payload["entities"] if self.keep_entity(term)]
        ships = [term for term in payload["ships"] if self.keep_ship(term)]
        items = [term for term in payload["items"] if self.keep_item(term)]

        place_set = set(places)
        people_set = set(people)
        item_set = set(items)
        ships = [term for term in ships if term not in place_set]
        ship_set = set(ships)
        entities = [term for term in entities if term not in place_set and term not in people_set and term not in ship_set and term not in item_set]

        return {
            "people": dedupe_preserve_order(people),
            "places": dedupe_preserve_order(places),
            "entities": dedupe_preserve_order(entities),
            "ships": dedupe_preserve_order(ships),
            "items": dedupe_preserve_order(items),
        }

    def keep_person(self, term: str) -> bool:
        if term in PEOPLE_EXACT_BLACKLIST:
            return False
        if term in EXTRA_PEOPLE:
            return True
        return bool(re.fullmatch(r"[A-Z][a-z]+(?:\s+[A-Z][A-Za-z'’.-]+){1,2}", term) or re.fullmatch(r"[A-Z][A-Za-z'’.-]+\s+\d+", term))

    def keep_place(self, term: str) -> bool:
        if term in PLACE_EXACT_BLACKLIST:
            return False
        if any(term.startswith(prefix) for prefix in PLACE_PREFIX_BLACKLIST):
            return False
        if any(fragment in term for fragment in PLACE_SUBSTRING_BLACKLIST):
            return False
        return True

    def keep_entity(self, term: str) -> bool:
        if term in EXTRA_ENTITIES:
            return True
        if term in ENTITY_EXACT_BLACKLIST:
            return False
        if ":" in term:
            return False
        if any(term.startswith(prefix) for prefix in ENTITY_PREFIX_BLACKLIST):
            return False
        if any(fragment in term for fragment in ENTITY_SUBSTRING_BLACKLIST):
            return False
        if looks_like_internal_identifier(term):
            return False
        return True

    def keep_ship(self, term: str) -> bool:
        if any(fragment.lower() in term.lower() for fragment in SHIP_SUBSTRING_BLACKLIST):
            return False
        if re.fullmatch(r"\d+", term):
            return False
        return True

    def keep_item(self, term: str) -> bool:
        if term.startswith(("Poster:", "T-shirt:", "Wall:", "Floor:")):
            return False
        return True


def dedupe_preserve_order(values: Iterable[str]) -> list[str]:
    seen: set[str] = set()
    result: list[str] = []
    for value in values:
        if value in seen:
            continue
        seen.add(value)
        result.append(value)
    return result


def main(argv: list[str]) -> int:
    data_root = Path(argv[1]) if len(argv) > 1 else DEFAULT_DATA_ROOT
    output_path = Path(argv[2]) if len(argv) > 2 else DEFAULT_OUTPUT_PATH

    builder = GlossaryBuilder()
    builder.process_all(data_root)
    payload = builder.to_payload()

    output_path.parent.mkdir(parents=True, exist_ok=True)
    with output_path.open("w", encoding="utf-8", newline="\n") as handle:
        json.dump(payload, handle, ensure_ascii=False, indent=2)
        handle.write("\n")

    print(f"Scanned data root: {data_root}")
    print(f"Wrote glossary: {output_path}")
    for category in CATEGORIES:
        print(f"  {category}: {len(payload[category])}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
