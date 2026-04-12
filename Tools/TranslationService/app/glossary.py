from __future__ import annotations

from collections.abc import Callable
import json
import re
from dataclasses import dataclass
from pathlib import Path

AUTO_PRESERVE_PATTERNS = [
    re.compile(r"https?://\S+", re.IGNORECASE),
    re.compile(r"\b[A-Za-z0-9_.:-]*[_/\\][A-Za-z0-9_.:-]*\b"),
]


@dataclass(frozen=True)
class Replacement:
    source: str
    target: str


@dataclass(frozen=True)
class _SegmentReplacement:
    start: int
    end: int
    text: str


class Glossary:
    def __init__(self, preserve: list[str], ru_en: list[Replacement], en_ru: list[Replacement]) -> None:
        self._preserve = [item for item in preserve if item.strip()]
        self._ru_en = ru_en
        self._en_ru = en_ru
        self._preserve_compiled = [self._compile_literal_pattern(item) for item in self._preserve]
        self._ru_en_compiled = [(self._compile_literal_pattern(r.source), r.target) for r in self._ru_en]
        self._en_ru_compiled = [(self._compile_literal_pattern(r.source), r.target) for r in self._en_ru]

    @property
    def term_count(self) -> int:
        return len(self._preserve) + len(self._ru_en) + len(self._en_ru)

    @classmethod
    def load(cls, path: str | Path) -> "Glossary":
        file_path = Path(path)
        example_path = file_path.with_name("glossary.example.json")

        payloads: list[dict] = []
        if example_path.exists() and example_path != file_path:
            payloads.append(json.loads(example_path.read_text(encoding="utf-8")))

        if file_path.exists():
            payloads.append(json.loads(file_path.read_text(encoding="utf-8")))

        if not payloads and example_path.exists():
            payloads.append(json.loads(example_path.read_text(encoding="utf-8")))

        if not payloads:
            return cls([], [], [])

        preserve = cls._merge_preserve(payloads)
        ru_en = cls._merge_replacements(payloads, "ru_en")
        en_ru = cls._merge_replacements(payloads, "en_ru")
        return cls(preserve, ru_en, en_ru)

    def translate(
        self,
        source_language: str,
        target_language: str,
        text: str,
        translate_chunk: Callable[[str], str],
    ) -> str:
        replacements = self._collect_replacements(source_language, target_language, text)
        if not replacements:
            return translate_chunk(text)

        rebuilt: list[str] = []
        cursor = 0
        for replacement in replacements:
            before = translate_chunk(text[cursor:replacement.start])
            rebuilt.append(before)
            # Ensure spacing between a translated chunk and a glossary term
            if before and replacement.text:
                if before[-1].isalnum() and replacement.text[0].isalnum():
                    rebuilt.append(" ")
            rebuilt.append(replacement.text)
            cursor = replacement.end

        tail = translate_chunk(text[cursor:])
        # Ensure spacing between last glossary term and trailing text
        if rebuilt and tail:
            last = rebuilt[-1]
            if last and last[-1].isalnum() and tail[0].isalnum():
                rebuilt.append(" ")
        rebuilt.append(tail)
        return "".join(rebuilt)

    def _collect_replacements(self, source_language: str, target_language: str, text: str) -> list[_SegmentReplacement]:
        matches: list[_SegmentReplacement] = []

        for pattern in AUTO_PRESERVE_PATTERNS:
            for match in pattern.finditer(text):
                matches.append(_SegmentReplacement(match.start(), match.end(), match.group(0)))

        for compiled in self._preserve_compiled:
            for match in compiled.finditer(text):
                matches.append(_SegmentReplacement(match.start(), match.end(), match.group(0)))

        direction_compiled = self._ru_en_compiled if source_language == "RU" and target_language == "EN" else self._en_ru_compiled
        for compiled, target in direction_compiled:
            for match in compiled.finditer(text):
                matches.append(_SegmentReplacement(match.start(), match.end(), self._apply_match_case(match.group(0), target)))

        matches.sort(key=lambda item: (item.start, -(item.end - item.start)))

        chosen: list[_SegmentReplacement] = []
        cursor = 0
        for replacement in matches:
            if replacement.start < cursor:
                continue

            chosen.append(replacement)
            cursor = replacement.end

        return chosen

    @staticmethod
    def _compile_literal_pattern(source: str) -> re.Pattern[str]:
        escaped = re.escape(source)
        if all(char.isalnum() or char in "-'" for char in source):
            return re.compile(rf"(?<!\w){escaped}(?!\w)", re.IGNORECASE)

        return re.compile(escaped, re.IGNORECASE)

    @staticmethod
    def _apply_match_case(matched: str, target: str) -> str:
        if not matched or not target:
            return target
        if matched.isupper():
            return target.upper()
        if len(matched) > 1 and matched[0].isupper() and matched[1:].islower():
            return target[0].upper() + target[1:]
        if matched.islower():
            return target.lower()
        return target

    @staticmethod
    def _merge_preserve(payloads: list[dict]) -> list[str]:
        merged: list[str] = []
        seen: set[str] = set()

        for payload in payloads:
            for item in payload.get("preserve", []):
                if not isinstance(item, str):
                    continue

                literal = item.strip()
                if not literal or literal in seen:
                    continue

                seen.add(literal)
                merged.append(literal)

        return merged

    @staticmethod
    def _merge_replacements(payloads: list[dict], key: str) -> list[Replacement]:
        merged: dict[str, str] = {}

        for payload in payloads:
            for item in payload.get(key, []):
                if not isinstance(item, dict):
                    continue

                source = str(item.get("source", "")).strip()
                target = str(item.get("target", "")).strip()
                if not source or not target:
                    continue

                merged[source] = target

        return [Replacement(source, target) for source, target in merged.items()]
