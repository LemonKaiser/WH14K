from __future__ import annotations

import hmac
import logging
import os
import re
import time as _time_mod
import unicodedata
from contextlib import asynccontextmanager
from dataclasses import dataclass
from functools import lru_cache
from pathlib import Path
from threading import RLock
from typing import Any, Literal

import ctranslate2
from fastapi import FastAPI, Header, HTTPException, Request
from fastapi.responses import JSONResponse
from pydantic import AliasChoices, BaseModel, ConfigDict, Field
from transformers import AutoTokenizer

_logger = logging.getLogger("wh40k.translation")

try:
    from py3langid.langid import LanguageIdentifier, MODEL_FILE
except Exception as _langid_err:
    LanguageIdentifier = None
    MODEL_FILE = None
    _logger.warning("py3langid unavailable, language detection will use script heuristics only: %s", _langid_err)

from app.cache import TTLCache
from app.glossary import Glossary

SupportedLanguage = Literal["RU", "EN"]
_ALLOWED_TRANSLATION_PUNCTUATION = frozenset(".,!?;:'\"()[]{}<>-_=+/\\|@#$%^&*~`")
_PUNCTUATION_REPLACEMENTS = {
    "\u2018": "'",
    "\u2019": "'",
    "\u201B": "'",
    "\u2032": "'",
    "\u201C": '"',
    "\u201D": '"',
    "\u201F": '"',
    "\u2033": '"',
    "\u2013": "-",
    "\u2014": "-",
    "\u2015": "-",
    "\u2212": "-",
    "\u2026": "...",
    "\u00AB": '"',
    "\u00BB": '"',
}
_SILENT_DROP_CATEGORIES = {"Mn", "Mc", "Me", "Cc", "Cf", "Cs", "Co", "Cn"}
_CYRILLIC_RE = re.compile(r"[\u0400-\u052F]")
_LATIN_RE = re.compile(r"[A-Za-z]")
_REPEATED_ALPHA_RE = re.compile(r"([A-Za-z\u0400-\u052F])\1{3,}")
_REPEATED_PUNCTUATION_RE = re.compile(r"([!?.,])\1{3,}")
_SENTENCE_BREAK_RE = re.compile(r"[.!?\n]+(?:\s+|$)")

# Model artifact patterns: OPUS-MT generates these on exclamatory inputs
_ARTIFACT_EN_RE = re.compile(r"\s*-\s*What\s*\?\s*", re.IGNORECASE)
_ARTIFACT_RU_RE = re.compile(r"\s*-\s*Я не знаю\.?\s*", re.IGNORECASE)
_ARTIFACT_COLONS_RE = re.compile(r"^\s*::\s*|\s*::\s*$|\s+::\s+")
_TRANSLATION_TOKEN_RE = re.compile(r"\s+|[A-Za-z\u0400-\u052F]+(?:['-][A-Za-z\u0400-\u052F]+)*|\d+(?:[.,:/-]\d+)*|.")
_LATIN_TO_CYRILLIC_LAYOUT = str.maketrans(
    "qwertyuiop[]asdfghjkl;'zxcvbnm,./`QWERTYUIOP{}ASDFGHJKL:\"ZXCVBNM<>?~",
    "йцукенгшщзхъфывапролджэячсмитьбю.ёЙЦУКЕНГШЩЗХЪФЫВАПРОЛДЖЭЯЧСМИТЬБЮ,Ё",
)
_CYRILLIC_TO_LATIN_LAYOUT = str.maketrans(
    "йцукенгшщзхъфывапролджэячсмитьбю.ёЙЦУКЕНГШЩЗХЪФЫВАПРОЛДЖЭЯЧСМИТЬБЮ,Ё",
    "qwertyuiop[]asdfghjkl;'zxcvbnm,./`QWERTYUIOP{}ASDFGHJKL:\"ZXCVBNM<>?~",
)
_ENGLISH_CANONICAL_PHRASES = (
    ("adeptus mechanicus", "Adeptus Mechanicus"),
    ("dark mechanicum", "Dark Mechanicum"),
    ("chaos undivided", "Chaos Undivided"),
)
_ENGLISH_CANONICAL_WORDS = (
    ("emperor", "Emperor"),
    ("imperium", "Imperium"),
    ("khorne", "Khorne"),
    ("nurgle", "Nurgle"),
    ("slaanesh", "Slaanesh"),
    ("tzeentch", "Tzeentch"),
    ("skrizhal", "Skrizhal"),
    ("geneblade", "Geneblade"),
)
_ENGLISH_CANONICAL_PHRASE_PATTERNS = tuple(
    (re.compile(rf"(?<!\w){re.escape(src)}(?!\w)", re.IGNORECASE), repl)
    for src, repl in _ENGLISH_CANONICAL_PHRASES
)
_ENGLISH_CANONICAL_WORD_PATTERNS = tuple(
    (re.compile(rf"(?<!\w){re.escape(src)}(?!\w)", re.IGNORECASE), repl)
    for src, repl in _ENGLISH_CANONICAL_WORDS
)
_ENGLISH_PRONOUN_RE = re.compile(r"(?<!\w)i(?:'m|'ve|'ll|'d)?(?!\w)", re.IGNORECASE)
_EN_VOWELS = frozenset("aeiouyAEIOUY")
_RU_VOWELS = frozenset("аеёиоуыэюяАЕЁИОУЫЭЮЯ")
_WORD_ONLY_TOKEN_RE = re.compile(r"[A-Za-z\u0400-\u052F]+(?:['-][A-Za-z\u0400-\u052F]+)*")
_ENGLISH_LAYOUT_REPAIR_WHITELIST = frozenset(
    {
        "hello",
        "hi",
        "hey",
        "help",
        "medic",
        "doctor",
        "engineer",
        "bridge",
        "line",
        "point",
        "flank",
        "entrance",
        "position",
        "move",
        "go",
        "hold",
        "secure",
        "defend",
        "fallback",
        "backup",
        "enemy",
        "ready",
        "commissar",
        "guardsman",
        "psyker",
        "vox",
        "north",
        "south",
        "east",
        "west",
        "now",
        "yes",
        "no",
    }
)


@dataclass(frozen=True)
class LanguageGuess:
    language: SupportedLanguage | None
    confidence: float
    cyrillic_letters: int
    latin_letters: int


def _is_word_char(ch: str) -> bool:
    return ch.isalpha() or ch == "'"


def _is_sentence_ending_punctuation(ch: str) -> bool:
    return ch in ".!?"


def _is_mostly_uppercase(text: str) -> bool:
    letters = [char for char in text if char.isalpha()]
    if len(letters) < 4:
        return False

    uppercase = sum(1 for char in letters if char.isupper())
    return uppercase / len(letters) >= 0.8


def _lowercase_word(word: str) -> str:
    return "".join(char.lower() if char.isalpha() else char for char in word)


def _capitalize_word(word: str) -> str:
    builder: list[str] = []
    seen_letter = False

    for char in word:
        if not char.isalpha():
            builder.append(char)
            continue

        if seen_letter:
            builder.append(char.lower())
            continue

        builder.append(char.upper())
        seen_letter = True

    return "".join(builder)


def _should_convert_english_title_case(original_text: str, translated_text: str) -> bool:
    if _is_mostly_uppercase(original_text):
        return False

    sentence_start = True
    title_case_mid_words = 0
    mid_words = 0
    index = 0

    while index < len(translated_text):
        char = translated_text[index]
        if not _is_word_char(char):
            if _is_sentence_ending_punctuation(char):
                sentence_start = True
            index += 1
            continue

        start = index
        while index < len(translated_text) and _is_word_char(translated_text[index]):
            index += 1

        word = translated_text[start:index]
        letters = [letter for letter in word if letter.isalpha()]
        if len(letters) <= 1:
            sentence_start = False
            continue

        if not sentence_start:
            mid_words += 1
            if letters[0].isupper() and all(letter.islower() for letter in letters[1:]):
                title_case_mid_words += 1

        sentence_start = False

    return mid_words >= 3 and title_case_mid_words * 2 >= mid_words


def _convert_english_title_case_to_sentence_case(text: str) -> str:
    builder: list[str] = []
    sentence_start = True
    index = 0

    while index < len(text):
        char = text[index]
        if not _is_word_char(char):
            builder.append(char)
            if _is_sentence_ending_punctuation(char):
                sentence_start = True
            index += 1
            continue

        start = index
        while index < len(text) and _is_word_char(text[index]):
            index += 1

        word = text[start:index]
        builder.append(_capitalize_word(word) if sentence_start else _lowercase_word(word))
        sentence_start = False

    return "".join(builder)


def _capitalize_sentence_starts(text: str) -> str:
    builder: list[str] = []
    sentence_start = True

    for char in text:
        if sentence_start and char.isalpha():
            builder.append(char.upper())
            sentence_start = False
            continue

        builder.append(char)

        if char.isalpha() or char.isdigit():
            sentence_start = False
            continue

        if _is_sentence_ending_punctuation(char):
            sentence_start = True

    return "".join(builder)


def _canonicalize_english_pronouns(text: str) -> str:
    return _ENGLISH_PRONOUN_RE.sub(
        lambda match: "I" + match.group(0)[1:].lower(),
        text,
    )


def _canonicalize_english_proper_nouns(text: str) -> str:
    for pattern, replacement in _ENGLISH_CANONICAL_PHRASE_PATTERNS:
        text = pattern.sub(replacement, text)

    for pattern, replacement in _ENGLISH_CANONICAL_WORD_PATTERNS:
        text = pattern.sub(replacement, text)

    return text


def _apply_contextual_phrase_case(match: re.Match[str], replacement: str) -> str:
    source = match.group(0)
    if source.isupper():
        return replacement.upper()

    prefix = match.string[:match.start()].rstrip()
    if not prefix or prefix[-1] in ".!?":
        return replacement

    return replacement[0].lower() + replacement[1:]


_RUSSIAN_TACTICAL_PHRASE_PATTERNS = tuple(
    (re.compile(src, re.IGNORECASE), re.compile(tgt, re.IGNORECASE), repl)
    for src, tgt, repl in (
        (r"(?<!\w)hold (?:the )?line(?!\w)", r"(?<!\w)(?:стойкая линия|Держать линию|держать линию)(?!\w)", "Держите линию"),
        (r"(?<!\w)hold (?:your )?position(?!\w)", r"(?<!\w)(?:держать позицию|Держать позицию)(?!\w)", "Удерживайте позицию"),
        (r"(?<!\w)secure (?:the )?point(?!\w)", r"(?<!\w)(?:безопасная точка|Безопасная точка|обеспечить безопасность точки|Обеспечить безопасность точки)(?!\w)", "Закрепитесь на точке"),
        (r"(?<!\w)defend (?:the )?point(?!\w)", r"(?<!\w)(?:защищать точку|Защищать точку)(?!\w)", "Защитите точку"),
    )
)


def _canonicalize_russian_tactical_phrases(original_text: str, translated_text: str) -> str:
    normalized_original = _normalize_text(original_text).lower()
    normalized_translated = translated_text

    for source_pattern, target_pattern, replacement in _RUSSIAN_TACTICAL_PHRASE_PATTERNS:
        if not source_pattern.search(normalized_original):
            continue

        normalized_translated = target_pattern.sub(
            lambda match, r=replacement: _apply_contextual_phrase_case(match, r),
            normalized_translated,
            count=1,
        )

    return normalized_translated


def _postprocess_translation(original_text: str, translated_text: str, target_language: SupportedLanguage) -> str:
    normalized = _normalize_text(translated_text)
    if not normalized:
        return ""

    # Strip OPUS-MT model artifacts ("- What?", "- Я не знаю.", "::" separators)
    normalized = _ARTIFACT_EN_RE.sub(" ", normalized)
    normalized = _ARTIFACT_RU_RE.sub(" ", normalized)
    normalized = _ARTIFACT_COLONS_RE.sub(" ", normalized)
    normalized = normalized.strip()
    if not normalized:
        return ""

    if target_language == "EN":
        if _should_convert_english_title_case(original_text, normalized):
            normalized = _convert_english_title_case_to_sentence_case(normalized)

        normalized = _capitalize_sentence_starts(normalized)
        normalized = _canonicalize_english_pronouns(normalized)
        normalized = _canonicalize_english_proper_nouns(normalized)
        return _normalize_text(normalized)

    normalized = _canonicalize_russian_tactical_phrases(original_text, normalized)
    return _normalize_text(_capitalize_sentence_starts(normalized))


def _normalize_language(language: str | None) -> SupportedLanguage | None:
    if language is None:
        return None

    upper = language.strip().upper()
    if upper == "RU":
        return "RU"
    if upper == "EN":
        return "EN"
    return None


def _normalize_text(text: str) -> str:
    if not text:
        return ""

    builder: list[str] = []
    pending_whitespace = False

    for raw_char in unicodedata.normalize("NFKC", text):
        mapped = _PUNCTUATION_REPLACEMENTS.get(raw_char)
        if mapped is None:
            if raw_char.isalnum() or raw_char in _ALLOWED_TRANSLATION_PUNCTUATION:
                mapped = raw_char
            elif raw_char.isspace():
                mapped = " "

        if mapped is None:
            if unicodedata.category(raw_char) in _SILENT_DROP_CATEGORIES:
                continue

            if builder:
                pending_whitespace = True
            continue

        for ch in mapped:
            if ch.isspace():
                pending_whitespace = True
                continue

            if pending_whitespace and builder:
                builder.append(" ")

            pending_whitespace = False
            builder.append(ch)

    normalized = "".join(builder).strip()
    normalized = _REPEATED_ALPHA_RE.sub(lambda match: match.group(1) * 2, normalized)
    normalized = _REPEATED_PUNCTUATION_RE.sub(lambda match: match.group(1) * 3, normalized)
    return normalized


def _prepare_detection_text(text: str) -> str:
    normalized = _normalize_text(text)
    letters_only = re.sub(r"[^A-Za-z\u0400-\u052F]+", " ", normalized)
    return re.sub(r"\s+", " ", letters_only).strip().lower()


def _count_script_letters(text: str) -> tuple[int, int]:
    return len(_CYRILLIC_RE.findall(text)), len(_LATIN_RE.findall(text))


def _opposite_language(language: SupportedLanguage) -> SupportedLanguage:
    return "EN" if language == "RU" else "RU"


def _apply_match_case(source: str, replacement: str) -> str:
    if not source:
        return replacement

    if source.isupper():
        return replacement.upper()

    if len(source) > 1 and source[0].isupper() and source[1:].islower():
        return replacement[0].upper() + replacement[1:]

    return replacement


def _count_vowels(text: str, language: SupportedLanguage) -> int:
    vowels = _RU_VOWELS if language == "RU" else _EN_VOWELS
    return sum(1 for char in text if char in vowels)


def _looks_layout_broken(text: str, language: SupportedLanguage) -> bool:
    prepared = _prepare_detection_text(text)
    if not prepared:
        return False

    cyrillic, latin = _count_script_letters(prepared)
    letter_count = cyrillic + latin
    if letter_count < 4:
        return False

    if language == "RU" and cyrillic == 0:
        return False
    if language == "EN" and latin == 0:
        return False

    vowel_count = _count_vowels(prepared, language)
    if vowel_count == 0:
        return True

    return letter_count >= 6 and (vowel_count / letter_count) < 0.18


def _convert_token_layout(token: str, expected_language: SupportedLanguage) -> str:
    if expected_language == "RU":
        return token.translate(_LATIN_TO_CYRILLIC_LAYOUT)

    return token.translate(_CYRILLIC_TO_LATIN_LAYOUT)


def _repair_keyboard_layout_token(
    token: str,
    expected_language: SupportedLanguage,
    preserve_language: SupportedLanguage | None = None,
) -> str:
    if len(token) < 4:
        return token

    token_language = _detect_token_language(token)
    if token_language == expected_language:
        return token

    candidate = _convert_token_layout(token, expected_language)
    if candidate == token:
        return token

    candidate_lower = candidate.lower()
    if _is_whitelist_layout_candidate(token, expected_language, candidate_lower, token_language):
        return _apply_match_case(token, candidate)

    if preserve_language is not None and token_language == preserve_language and not _looks_layout_broken(token, preserve_language):
        return token

    candidate_guess = _guess_language(candidate)
    if candidate_guess.language != expected_language:
        return token

    if _looks_layout_broken(candidate, expected_language):
        return token

    if token_language is not None and token_language != expected_language:
        if not _looks_layout_broken(token, token_language):
            return token

    return _apply_match_case(token, candidate)


def _repair_keyboard_layout_tokens(
    text: str,
    expected_language: SupportedLanguage,
    preserve_language: SupportedLanguage | None = None,
) -> str:
    return _WORD_ONLY_TOKEN_RE.sub(
        lambda match: _repair_keyboard_layout_token(match.group(0), expected_language, preserve_language),
        text,
    )


def _is_whitelist_layout_candidate(
    token: str,
    expected_language: SupportedLanguage,
    candidate_lower: str | None = None,
    token_language: SupportedLanguage | None = None,
) -> bool:
    if expected_language != "EN":
        return False

    detected = token_language or _detect_token_language(token)
    if detected != "RU":
        return False

    candidate = candidate_lower or _convert_token_layout(token, expected_language).lower()
    return candidate in _ENGLISH_LAYOUT_REPAIR_WHITELIST


def _count_whitelist_layout_candidates(text: str, expected_language: SupportedLanguage) -> int:
    count = 0
    for match in _WORD_ONLY_TOKEN_RE.finditer(text):
        token = match.group(0)
        if _is_whitelist_layout_candidate(token, expected_language):
            count += 1

    return count


_TACTICAL_PHRASE_PATTERNS = tuple(
    (re.compile(pattern, re.IGNORECASE), replacement)
    for pattern, replacement in (
        (r"(?<!\w)hold line(?!\w)", "hold the line"),
        (r"(?<!\w)hold point(?!\w)", "hold the point"),
        (r"(?<!\w)hold bridge(?!\w)", "hold the bridge"),
        (r"(?<!\w)hold flank(?!\w)", "hold the flank"),
        (r"(?<!\w)hold entrance(?!\w)", "hold the entrance"),
        (r"(?<!\w)hold position(?!\w)", "hold your position"),
        (r"(?<!\w)secure point(?!\w)", "secure the point"),
        (r"(?<!\w)secure bridge(?!\w)", "secure the bridge"),
        (r"(?<!\w)defend point(?!\w)", "defend the point"),
        (r"(?<!\w)defend bridge(?!\w)", "defend the bridge"),
        (r"(?<!\w)need medic(?!\w)", "need a medic"),
        (r"(?<!\w)need doctor(?!\w)", "need a doctor"),
        (r"(?<!\w)need engineer(?!\w)", "need an engineer"),
    )
)


def _normalize_tactical_phrases(text: str, source_language: SupportedLanguage) -> str:
    if source_language != "EN":
        return text

    normalized = text
    for pattern, replacement in _TACTICAL_PHRASE_PATTERNS:
        normalized = pattern.sub(
            lambda match, r=replacement: _apply_match_case(match.group(0), r),
            normalized,
        )

    return normalized


@lru_cache(maxsize=1)
def _get_language_identifier() -> Any | None:
    if LanguageIdentifier is None or MODEL_FILE is None:
        return None

    identifier = LanguageIdentifier.from_pickled_model(MODEL_FILE, norm_probs=True)
    identifier.set_languages(["en", "ru"])
    return identifier


def _map_language_code(language_code: str | None) -> SupportedLanguage | None:
    if language_code == "ru":
        return "RU"
    if language_code == "en":
        return "EN"
    return None


def _guess_language_from_prepared_text(text: str) -> LanguageGuess:
    cyrillic, latin = _count_script_letters(text)
    if not cyrillic and not latin:
        return LanguageGuess(None, 0.0, cyrillic, latin)

    if cyrillic and not latin:
        return LanguageGuess("RU", 1.0, cyrillic, latin)

    if latin and not cyrillic:
        return LanguageGuess("EN", 1.0, cyrillic, latin)

    dominant_ratio = max(cyrillic, latin) / max(cyrillic + latin, 1)
    identifier = _get_language_identifier()
    if identifier is not None and len(text.replace(" ", "")) >= 3:
        detected_code, probability = identifier.classify(text)
        detected_language = _map_language_code(detected_code)
        probability = float(probability)
        if detected_language is not None:
            if probability >= 0.80:
                return LanguageGuess(detected_language, probability, cyrillic, latin)

            if probability >= 0.55:
                if detected_language == "RU" and cyrillic >= latin * 1.15:
                    return LanguageGuess(detected_language, probability, cyrillic, latin)

                if detected_language == "EN" and latin >= cyrillic * 1.15:
                    return LanguageGuess(detected_language, probability, cyrillic, latin)

    if cyrillic >= latin * 1.5:
        return LanguageGuess("RU", dominant_ratio, cyrillic, latin)

    if latin >= cyrillic * 1.5:
        return LanguageGuess("EN", dominant_ratio, cyrillic, latin)

    return LanguageGuess(None, dominant_ratio, cyrillic, latin)


def _guess_language(text: str) -> LanguageGuess:
    return _guess_language_from_prepared_text(_prepare_detection_text(text))


def _repair_keyboard_layout(text: str, expected_language: SupportedLanguage | None = None) -> str:
    prepared = _prepare_detection_text(text)
    if len(prepared.replace(" ", "")) < 4:
        return text

    base_guess = _guess_language_from_prepared_text(prepared)
    candidates: list[tuple[str, SupportedLanguage]] = []
    if base_guess.latin_letters and not base_guess.cyrillic_letters:
        candidates.append((text.translate(_LATIN_TO_CYRILLIC_LAYOUT), "RU"))
    elif base_guess.cyrillic_letters and not base_guess.latin_letters:
        candidates.append((text.translate(_CYRILLIC_TO_LATIN_LAYOUT), "EN"))
    else:
        candidates.append((text.translate(_LATIN_TO_CYRILLIC_LAYOUT), "RU"))
        candidates.append((text.translate(_CYRILLIC_TO_LATIN_LAYOUT), "EN"))

    best_text = text
    best_confidence = base_guess.confidence
    for candidate_text, candidate_language in candidates:
        if candidate_text == text:
            continue

        if expected_language is not None and candidate_language != expected_language:
            continue

        candidate_prepared = _prepare_detection_text(candidate_text)
        if len(candidate_prepared.replace(" ", "")) < 4:
            continue

        candidate_guess = _guess_language_from_prepared_text(candidate_prepared)
        improvement = candidate_guess.confidence - base_guess.confidence
        if candidate_guess.language != candidate_language:
            continue

        if candidate_guess.confidence >= 0.95 and improvement >= 0.25:
            best_text = candidate_text
            best_confidence = candidate_guess.confidence
            continue

        if base_guess.confidence < 0.60 and candidate_guess.confidence >= 0.90 and improvement >= 0.15:
            best_text = candidate_text
            best_confidence = candidate_guess.confidence

    return best_text if best_confidence > base_guess.confidence else text


def _normalize_translation_text(
    text: str,
    expected_language: SupportedLanguage | None = None,
    preserve_language: SupportedLanguage | None = None,
) -> str:
    normalized = _normalize_text(text)
    if not normalized:
        return ""

    repaired = _repair_keyboard_layout(normalized, expected_language)
    if expected_language is not None:
        repaired = _repair_keyboard_layout_tokens(repaired, expected_language, preserve_language)
    return _normalize_text(repaired)


def _detect_token_language(token: str) -> SupportedLanguage | None:
    prepared = _prepare_detection_text(token)
    if not prepared:
        return None

    cyrillic, latin = _count_script_letters(prepared)
    if cyrillic and not latin:
        return "RU"
    if latin and not cyrillic:
        return "EN"
    if cyrillic >= latin * 2 and cyrillic >= 2:
        return "RU"
    if latin >= cyrillic * 2 and latin >= 2:
        return "EN"
    return None


def _split_sentence_chunks(text: str) -> list[str]:
    chunks: list[str] = []
    cursor = 0
    for match in _SENTENCE_BREAK_RE.finditer(text):
        end = match.end()
        chunks.append(text[cursor:end])
        cursor = end

    if cursor < len(text):
        chunks.append(text[cursor:])

    return [chunk for chunk in chunks if chunk]


def _has_mixed_language_words(
    text: str,
    source_language: SupportedLanguage,
    target_language: SupportedLanguage,
) -> bool:
    source_words = 0
    target_words = 0
    for token in _TRANSLATION_TOKEN_RE.findall(text):
        token_language = _detect_token_language(token)
        if token_language == source_language:
            source_words += 1
        elif token_language == target_language:
            target_words += 1

        if source_words and target_words:
            return True

    return False


def _classify_token_action(
    token: str,
    source_language: SupportedLanguage,
    target_language: SupportedLanguage,
) -> Literal["translate", "preserve"] | None:
    if not any(char.isalpha() for char in token):
        return None

    token_language = _detect_token_language(token)
    if token_language == source_language:
        return "translate"
    if token_language == target_language:
        return "preserve"

    prepared = _prepare_detection_text(token)
    cyrillic, latin = _count_script_letters(prepared)
    if source_language == "RU" and cyrillic > latin:
        return "translate"
    if source_language == "EN" and latin > cyrillic:
        return "translate"
    if target_language == "RU" and cyrillic > latin:
        return "preserve"
    if target_language == "EN" and latin > cyrillic:
        return "preserve"
    return None


def _count_language_word_tokens(text: str) -> tuple[int, int]:
    ru_tokens = 0
    en_tokens = 0

    for token in _TRANSLATION_TOKEN_RE.findall(text):
        if not any(char.isalpha() for char in token):
            continue

        token_language = _detect_token_language(token)
        if token_language == "RU":
            ru_tokens += 1
        elif token_language == "EN":
            en_tokens += 1

    return ru_tokens, en_tokens


def _detect_layout_swapped_source(text: str) -> SupportedLanguage | None:
    prepared = _prepare_detection_text(text)
    if not prepared:
        return None

    cyrillic, latin = _count_script_letters(prepared)
    if latin and not cyrillic:
        repaired = _normalize_text(text.translate(_LATIN_TO_CYRILLIC_LAYOUT))
        repaired_guess = _guess_language(repaired)
        if repaired_guess.language == "RU" and repaired_guess.confidence >= 0.90 and _looks_layout_broken(text, "EN"):
            return "RU"

    if cyrillic and not latin:
        repaired = _normalize_text(text.translate(_CYRILLIC_TO_LATIN_LAYOUT))
        repaired_guess = _guess_language(repaired)
        if repaired_guess.language == "EN" and repaired_guess.confidence >= 0.90:
            if _count_whitelist_layout_candidates(text, "EN") > 0 or _looks_layout_broken(text, "RU"):
                return "EN"

    return None


def _detect_source_language(text: str, requested_targets: list[SupportedLanguage]) -> SupportedLanguage | None:
    layout_swapped = _detect_layout_swapped_source(text)
    if layout_swapped is not None:
        return layout_swapped

    if len(requested_targets) == 1:
        if requested_targets[0] == "RU" and _count_whitelist_layout_candidates(text, "EN") > 0:
            return "EN"

        ru_tokens, en_tokens = _count_language_word_tokens(text)
        if ru_tokens > 0 and en_tokens > 0:
            return _opposite_language(requested_targets[0])

    return _detect_language(text)


def _count_sentences(text: str) -> int:
    normalized = _normalize_text(text)
    if not normalized:
        return 0

    matches = re.findall(r"[.!?]+", normalized)
    return max(1, len(matches))


def _count_short_sentence_breaks(text: str) -> int:
    normalized = _normalize_text(text)
    if not normalized:
        return 0

    fragments = [fragment.strip() for fragment in re.split(r"[.!?]+", normalized) if fragment.strip()]
    if len(fragments) <= 1:
        return 0

    short_breaks = 0
    for fragment in fragments[:-1]:
        words = re.findall(r"[A-Za-z\u0400-\u052F]+(?:['-][A-Za-z\u0400-\u052F]+)*", fragment)
        if len(words) <= 2:
            short_breaks += 1

    return short_breaks


def _mixed_translation_penalty(
    original_text: str,
    translated_text: str,
    target_language: SupportedLanguage,
) -> float:
    normalized = _normalize_text(translated_text)
    if not normalized:
        return 1_000_000.0

    ru_tokens, en_tokens = _count_language_word_tokens(normalized)
    original_sentences = _count_sentences(original_text)
    translated_sentences = _count_sentences(normalized)
    short_breaks = _count_short_sentence_breaks(normalized)

    penalty = 0.0
    if target_language == "RU":
        penalty += en_tokens * 6.0
        if ru_tokens == 0 and en_tokens > 0:
            penalty += 25.0
    else:
        penalty += ru_tokens * 6.0
        if en_tokens == 0 and ru_tokens > 0:
            penalty += 25.0

    if translated_sentences > original_sentences:
        penalty += (translated_sentences - original_sentences) * 4.0

    penalty += short_breaks * 5.0
    penalty += abs(len(normalized) - len(_normalize_text(original_text))) / 80.0
    return penalty


def _detect_language(text: str) -> SupportedLanguage | None:
    return _guess_language(text).language


@dataclass(frozen=True)
class Settings:
    host: str = os.getenv("WH40K_TRANSLATION_HOST", "0.0.0.0")
    port: int = int(os.getenv("WH40K_TRANSLATION_PORT", "8090"))
    api_key: str = os.getenv("WH40K_TRANSLATION_API_KEY", "")
    model_root: Path = Path(os.getenv("WH40K_TRANSLATION_MODEL_ROOT", "./models"))
    tokenizer_root: Path = Path(os.getenv("WH40K_TRANSLATION_TOKENIZER_ROOT", "./tokenizers"))
    glossary_path: Path = Path(os.getenv("WH40K_TRANSLATION_GLOSSARY_PATH", "./config/glossary.json"))
    max_text_length: int = int(os.getenv("WH40K_TRANSLATION_MAX_TEXT_LENGTH", "240"))
    cache_ttl_seconds: int = int(os.getenv("WH40K_TRANSLATION_CACHE_TTL_SECONDS", "3600"))
    cache_max_items: int = int(os.getenv("WH40K_TRANSLATION_CACHE_MAX_ITEMS", "10000"))
    inter_threads: int = int(os.getenv("WH40K_TRANSLATION_INTER_THREADS", "1"))
    intra_threads: int = int(os.getenv("WH40K_TRANSLATION_INTRA_THREADS", "2"))
    beam_size: int = int(os.getenv("WH40K_TRANSLATION_BEAM_SIZE", "1"))
    max_decoding_length: int = int(os.getenv("WH40K_TRANSLATION_MAX_DECODING_LENGTH", "128"))
    compute_type: str = os.getenv("WH40K_TRANSLATION_COMPUTE_TYPE", "default")
    debug_delay_ms: int = int(os.getenv("WH40K_TRANSLATION_DEBUG_DELAY_MS", "0"))

    def __post_init__(self) -> None:
        if self.port < 1 or self.port > 65535:
            raise ValueError(f"port must be 1-65535, got {self.port}")
        if self.cache_ttl_seconds < 1:
            raise ValueError(f"cache_ttl_seconds must be >= 1, got {self.cache_ttl_seconds}")
        if self.cache_max_items < 1:
            raise ValueError(f"cache_max_items must be >= 1, got {self.cache_max_items}")
        if self.max_text_length < 1:
            raise ValueError(f"max_text_length must be >= 1, got {self.max_text_length}")
        if self.beam_size < 1:
            raise ValueError(f"beam_size must be >= 1, got {self.beam_size}")
        if self.max_decoding_length < 1:
            raise ValueError(f"max_decoding_length must be >= 1, got {self.max_decoding_length}")
        if self.debug_delay_ms < 0:
            raise ValueError(f"debug_delay_ms must be >= 0, got {self.debug_delay_ms}")


class TranslateRequest(BaseModel):
    model_config = ConfigDict(populate_by_name=True)

    text: str = Field(min_length=1, validation_alias=AliasChoices("text", "Text"))
    source_language: SupportedLanguage | None = Field(
        default=None,
        validation_alias=AliasChoices("source_language", "SourceLanguage"),
    )
    target_languages: list[SupportedLanguage] = Field(
        default_factory=list,
        validation_alias=AliasChoices("target_languages", "TargetLanguages"),
    )
    channel: str | None = Field(default=None, validation_alias=AliasChoices("channel", "Channel"))


class TranslateResponse(BaseModel):
    model_config = ConfigDict(populate_by_name=True)

    source_language: SupportedLanguage = Field(serialization_alias="SourceLanguage")
    original_text: str = Field(serialization_alias="OriginalText")
    translations: dict[SupportedLanguage, str] = Field(serialization_alias="Translations")
    provider: str = Field(default="ctranslate2-opus-mt", serialization_alias="Provider")
    cache_hits: list[SupportedLanguage] = Field(default_factory=list, serialization_alias="CacheHits")


@dataclass
class ModelRuntime:
    translator: ctranslate2.Translator
    tokenizer: object


class TranslationService:
    def __init__(self, settings: Settings) -> None:
        self.settings = settings
        self.glossary = Glossary.load(settings.glossary_path)
        self.cache: TTLCache[str] = TTLCache(settings.cache_max_items, settings.cache_ttl_seconds)
        self.models: dict[tuple[SupportedLanguage, SupportedLanguage], ModelRuntime] = {}

    def load(self) -> None:
        self.models[("EN", "RU")] = self._load_runtime("opus-mt-en-ru")
        self.models[("RU", "EN")] = self._load_runtime("opus-mt-ru-en")

    def translate(self, source_language: SupportedLanguage, target_language: SupportedLanguage, text: str) -> tuple[str, bool]:
        cache_key = f"{source_language}:{target_language}:{_normalize_text(text)}"
        cached = self.cache.get(cache_key)
        if cached is not None:
            return cached, True

        runtime = self.models[(source_language, target_language)]
        translated = self.glossary.translate(
            source_language,
            target_language,
            text,
            lambda chunk: self._translate_chunk(runtime, source_language, target_language, chunk),
        )
        translated = _postprocess_translation(text, translated, target_language)
        self.cache.put(cache_key, translated)
        return translated, False

    def _translate_chunk(
        self,
        runtime: ModelRuntime,
        source_language: SupportedLanguage,
        target_language: SupportedLanguage,
        text: str,
    ) -> str:
        if not text:
            return text

        leading = text[:len(text) - len(text.lstrip())]
        trailing = text[len(text.rstrip()):]
        core = text.strip()
        if not core:
            return text

        normalized_core = _normalize_translation_text(
            core,
            expected_language=source_language,
            preserve_language=target_language,
        )
        if not normalized_core:
            return text

        translated = self._translate_segmented_text(runtime, source_language, target_language, normalized_core)
        return f"{leading}{_normalize_text(translated)}{trailing}"

    def _translate_segmented_text(
        self,
        runtime: ModelRuntime,
        source_language: SupportedLanguage,
        target_language: SupportedLanguage,
        text: str,
    ) -> str:
        chunks = _split_sentence_chunks(text)
        if not chunks:
            chunks = [text]

        rebuilt: list[str] = []
        for chunk in chunks:
            if not chunk.strip():
                rebuilt.append(chunk)
                continue

            if _has_mixed_language_words(chunk, source_language, target_language):
                rebuilt.append(self._translate_mixed_chunk(runtime, source_language, target_language, chunk))
                continue

            detected_language = _detect_language(chunk)
            if detected_language == target_language:
                rebuilt.append(chunk)
                continue

            rebuilt.append(self._translate_direct_chunk(runtime, chunk, source_language, target_language))

        return "".join(rebuilt)

    def _translate_mixed_chunk(
        self,
        runtime: ModelRuntime,
        source_language: SupportedLanguage,
        target_language: SupportedLanguage,
        text: str,
    ) -> str:
        direct_candidate = self._translate_direct_chunk(runtime, text, source_language, target_language)

        groups: list[tuple[Literal["translate", "preserve"], str]] = []
        current_action: Literal["translate", "preserve"] | None = None
        current_tokens: list[str] = []
        pending_neutral: list[str] = []

        for token in _TRANSLATION_TOKEN_RE.findall(text):
            action = _classify_token_action(token, source_language, target_language)
            if action is None:
                pending_neutral.append(token)
                continue

            if current_action is None:
                current_tokens.extend(pending_neutral)
                pending_neutral.clear()
                current_action = action
                current_tokens.append(token)
                continue

            if action == current_action:
                current_tokens.extend(pending_neutral)
                pending_neutral.clear()
                current_tokens.append(token)
                continue

            current_tokens.extend(pending_neutral)
            pending_neutral.clear()
            groups.append((current_action, "".join(current_tokens)))
            current_action = action
            current_tokens = [token]

        if current_action is None:
            return self._translate_direct_chunk(runtime, text, source_language, target_language)

        current_tokens.extend(pending_neutral)
        groups.append((current_action, "".join(current_tokens)))

        rebuilt: list[str] = []
        for action, group_text in groups:
            if action == "preserve":
                rebuilt.append(group_text)
                continue

            rebuilt.append(self._translate_direct_chunk(runtime, group_text, source_language, target_language))

        segmented_candidate = "".join(rebuilt)
        segmented_penalty = _mixed_translation_penalty(text, segmented_candidate, target_language)
        direct_penalty = _mixed_translation_penalty(text, direct_candidate, target_language)
        return direct_candidate if direct_penalty + 0.5 < segmented_penalty else segmented_candidate

    def _translate_direct_chunk(
        self,
        runtime: ModelRuntime,
        text: str,
        expected_source_language: SupportedLanguage,
        target_language: SupportedLanguage,
    ) -> str:
        if not text:
            return text

        leading = text[:len(text) - len(text.lstrip())]
        trailing = text[len(text.rstrip()):]
        core = text.strip()
        if not core:
            return text

        normalized_core = _normalize_translation_text(
            core,
            expected_language=expected_source_language,
            preserve_language=target_language,
        )
        if not normalized_core:
            return text

        normalized_core = _normalize_tactical_phrases(normalized_core, expected_source_language)

        token_ids = runtime.tokenizer.encode(normalized_core)
        source_tokens = runtime.tokenizer.convert_ids_to_tokens(token_ids)
        results = runtime.translator.translate_batch(
            [source_tokens],
            beam_size=self.settings.beam_size,
            max_decoding_length=self.settings.max_decoding_length,
        )
        output_tokens = results[0].hypotheses[0]
        translated = runtime.tokenizer.decode(
            runtime.tokenizer.convert_tokens_to_ids(output_tokens),
            skip_special_tokens=True,
        )
        return f"{leading}{_normalize_text(translated)}{trailing}"

    def _load_runtime(self, model_name: str) -> ModelRuntime:
        model_path = self.settings.model_root / model_name
        if not model_path.exists():
            raise RuntimeError(f"missing model directory: {model_path}")

        tokenizer_path = self.settings.tokenizer_root / model_name
        if not tokenizer_path.exists():
            tokenizer_path = model_path

        translator = ctranslate2.Translator(
            str(model_path),
            device="cpu",
            compute_type=self.settings.compute_type,
            inter_threads=self.settings.inter_threads,
            intra_threads=self.settings.intra_threads,
        )
        tokenizer = AutoTokenizer.from_pretrained(tokenizer_path)
        return ModelRuntime(translator=translator, tokenizer=tokenizer)


_RATE_LIMIT_WINDOW = 10.0
_RATE_LIMIT_MAX_REQUESTS = 100
_RATE_LIMIT_CLEANUP_INTERVAL = 60.0
_rate_limit_hits: dict[str, list[float]] = {}
_rate_limit_lock = RLock()
_rate_limit_last_cleanup: float = 0.0


def _check_rate_limit(client_ip: str) -> bool:
    global _rate_limit_last_cleanup
    now = _time_mod.monotonic()
    cutoff = now - _RATE_LIMIT_WINDOW
    with _rate_limit_lock:
        if now - _rate_limit_last_cleanup > _RATE_LIMIT_CLEANUP_INTERVAL:
            _rate_limit_last_cleanup = now
            stale_ips = [ip for ip, ts in _rate_limit_hits.items() if not ts or ts[-1] <= cutoff]
            for ip in stale_ips:
                del _rate_limit_hits[ip]
        timestamps = _rate_limit_hits.get(client_ip, [])
        recent = [t for t in timestamps if t > cutoff]
        if len(recent) >= _RATE_LIMIT_MAX_REQUESTS:
            _rate_limit_hits[client_ip] = recent
            return False
        recent.append(now)
        _rate_limit_hits[client_ip] = recent
        return True


settings = Settings()
service: TranslationService | None = None


@asynccontextmanager
async def _lifespan(application: FastAPI):
    global service
    if settings.api_key in ("", "change-me"):
        _logger.warning(
            "API key is %s — the service is effectively unprotected. "
            "Set WH40K_TRANSLATION_API_KEY to a strong secret before exposing this endpoint.",
            repr(settings.api_key) if settings.api_key else "empty",
        )
    svc = TranslationService(settings)
    svc.load()
    service = svc
    _logger.info("Translation service ready (glossary_terms=%d)", svc.glossary.term_count)
    yield
    service = None


app = FastAPI(title="WH40K Translation Service", version="1.0.0", lifespan=_lifespan)


@app.get("/health")
def health() -> dict[str, object]:
    if service is None:
        raise HTTPException(status_code=503, detail="service_not_ready")
    return {
        "status": "ok",
        "directions": sorted(f"{source}->{target}" for source, target in service.models.keys()),
        "glossary_terms": service.glossary.term_count,
        "cache_size": service.cache.size,
    }


@app.post("/translate", response_model=TranslateResponse, response_model_by_alias=True)
def translate(
    request: TranslateRequest,
    x_api_key: str | None = Header(default=None),
    x_debug_delay_ms: int | None = Header(default=None),
    http_request: Request = None,
) -> TranslateResponse:
    if settings.api_key and not hmac.compare_digest((x_api_key or "").encode(), settings.api_key.encode()):
        raise HTTPException(status_code=401, detail="invalid_api_key")

    client_ip = http_request.client.host if http_request and http_request.client else "unknown"
    if not _check_rate_limit(client_ip):
        raise HTTPException(status_code=429, detail="rate_limit_exceeded")

    if service is None:
        raise HTTPException(status_code=503, detail="service_not_ready")

    delay_ms = x_debug_delay_ms if x_debug_delay_ms is not None else settings.debug_delay_ms
    if delay_ms > 0:
        _time_mod.sleep(delay_ms / 1000.0)

    requested_source_language = _normalize_language(request.source_language)
    normalized_text = _normalize_text(request.text)
    if not normalized_text:
        raise HTTPException(status_code=400, detail="empty_text")

    requested_targets = []
    for target in request.target_languages:
        normalized_target = _normalize_language(target)
        if normalized_target is None or normalized_target in requested_targets:
            continue
        requested_targets.append(normalized_target)

    source_language = requested_source_language or _detect_source_language(normalized_text, requested_targets)
    if source_language is None:
        raise HTTPException(status_code=400, detail="unsupported_source_language")

    requested_targets = [target for target in requested_targets if target != source_language]

    preserve_language = requested_targets[0] if len(requested_targets) == 1 else None
    repaired = _repair_keyboard_layout(normalized_text, source_language)
    if source_language is not None:
        repaired = _repair_keyboard_layout_tokens(repaired, source_language, preserve_language)
    normalized_text = _normalize_text(repaired)
    if not normalized_text:
        raise HTTPException(status_code=400, detail="empty_text")

    if len(normalized_text) > settings.max_text_length:
        raise HTTPException(status_code=400, detail="text_too_long")

    translations: dict[SupportedLanguage, str] = {}
    cache_hits: list[SupportedLanguage] = []
    svc = service
    if svc is None:
        raise HTTPException(status_code=503, detail="service_not_ready")
    for target_language in requested_targets:
        translated, cache_hit = svc.translate(source_language, target_language, normalized_text)
        translations[target_language] = translated
        if cache_hit:
            cache_hits.append(target_language)

    return TranslateResponse(
        source_language=source_language,
        original_text=normalized_text,
        translations=translations,
        cache_hits=cache_hits,
    )


@app.post("/glossary/reload")
def glossary_reload(x_api_key: str | None = Header(default=None)) -> dict[str, object]:
    if settings.api_key and not hmac.compare_digest((x_api_key or "").encode(), settings.api_key.encode()):
        raise HTTPException(status_code=401, detail="invalid_api_key")

    if service is None:
        raise HTTPException(status_code=503, detail="service_not_ready")

    service.glossary = Glossary.load(settings.glossary_path)
    _logger.info("Glossary reloaded (terms=%d)", service.glossary.term_count)
    return {"status": "ok", "glossary_terms": service.glossary.term_count}
