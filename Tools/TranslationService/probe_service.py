from __future__ import annotations

import json
import os
import statistics
import sys
import time
import urllib.error
import urllib.request
from dataclasses import dataclass


@dataclass(frozen=True)
class ProbeCase:
    text: str
    source_language: str
    target_language: str


DEFAULT_CASES = [
    ProbeCase("Привет, комиссар", "RU", "EN"),
    ProbeCase("Император защищает", "RU", "EN"),
    ProbeCase("Псайкер на мостике", "RU", "EN"),
    ProbeCase("Еретик в техтоннелях", "RU", "EN"),
    ProbeCase("Готов ли ты отдать душу за императора?", "RU", "EN"),
    ProbeCase("Добро пожаловать на службу гвардеец, ну что готов надирать зад еретикам?", "RU", "EN"),
    ProbeCase("SS14 WH40K A12 код красный", "RU", "EN"),
    ProbeCase("For the Emperor", "EN", "RU"),
    ProbeCase("Are you ready to give your life for the Emperor?", "EN", "RU"),
    ProbeCase("Psyker on the bridge", "EN", "RU"),
    ProbeCase("Heretic in maintenance", "EN", "RU"),
    ProbeCase("Commissar to A12", "EN", "RU"),
    ProbeCase("Welcome to the service, guardsman. So, are you ready to kick some heretic ass?", "EN", "RU"),
]


def send_request(base_url: str, api_key: str | None, case: ProbeCase) -> tuple[str, float]:
    payload = json.dumps(
        {
            "Text": case.text,
            "SourceLanguage": case.source_language,
            "TargetLanguages": [case.target_language],
            "Channel": "Local",
        }
    ).encode("utf-8")

    request = urllib.request.Request(
        f"{base_url.rstrip('/')}/translate",
        data=payload,
        method="POST",
        headers={"Content-Type": "application/json; charset=utf-8"},
    )

    if api_key:
        request.add_header("X-Api-Key", api_key)

    started = time.perf_counter()
    with urllib.request.urlopen(request, timeout=15) as response:
        body = json.loads(response.read().decode("utf-8"))
    elapsed_ms = (time.perf_counter() - started) * 1000.0

    translations = body.get("Translations") or body.get("translations") or {}
    translated = translations.get(case.target_language, "")
    return translated, elapsed_ms


def main() -> int:
    base_url = os.getenv("WH40K_TRANSLATION_SERVICE_URL", "http://127.0.0.1:8090")
    api_key = os.getenv("WH40K_TRANSLATION_API_KEY") or None

    results = []
    for case in DEFAULT_CASES:
        try:
            translated, elapsed_ms = send_request(base_url, api_key, case)
        except urllib.error.HTTPError as error:
            print(f"HTTP {error.code} for {case.source_language}->{case.target_language}: {case.text}", file=sys.stderr)
            print(error.read().decode("utf-8", errors="replace"), file=sys.stderr)
            return 1

        results.append(elapsed_ms)
        print(f"[{case.source_language}->{case.target_language}] {case.text}")
        print(f"  -> {translated}")
        print(f"  {elapsed_ms:.1f} ms")

    print()
    print(
        "Latency summary: "
        f"min={min(results):.1f} ms, avg={statistics.mean(results):.1f} ms, "
        f"p95={statistics.quantiles(results, n=20, method='inclusive')[18]:.1f} ms, max={max(results):.1f} ms"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
