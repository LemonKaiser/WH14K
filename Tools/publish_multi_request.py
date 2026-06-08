#!/usr/bin/env python3

import argparse
import os
import subprocess
import time
from typing import Iterable

import requests

PUBLISH_TOKEN = os.environ["PUBLISH_TOKEN"]
VERSION = os.environ["GITHUB_SHA"]

RELEASE_DIR = "release"

#
# WH14K CDN configuration
#

CDN_URL = "https://heretec.online/cdn/"
FORK_ID = "wh14k"

UPLOAD_RETRIES = 4
TRANSIENT_STATUS_CODES = {408, 425, 429, 500, 502, 503, 504}
RETRYABLE_EXCEPTIONS = (
    requests.exceptions.ConnectionError,
    requests.exceptions.SSLError,
    requests.exceptions.Timeout,
)


def normalize_cdn_url(cdn_url: str) -> str:
    """
    Robust.Cdn publish URLs must look like:
    https://heretec.online/cdn/fork/wh14k/publish/start

    So CDN_URL must always end with slash.
    """
    return cdn_url.rstrip("/") + "/"


def post_with_retry(
    url: str,
    *,
    token: str,
    json_payload: dict | None = None,
    headers: dict | None = None,
    timeout=(15, 120),
    label: str = "request",
) -> requests.Response:
    request_headers = {
        "Authorization": f"Bearer {token}",
        "Connection": "close",
    }

    if headers is not None:
        request_headers.update(headers)

    last_error = None
    last_response = None

    for attempt in range(1, UPLOAD_RETRIES + 1):
        try:
            with requests.Session() as session:
                response = session.post(
                    url,
                    json=json_payload,
                    headers=request_headers,
                    timeout=timeout,
                )

            if response.status_code in TRANSIENT_STATUS_CODES and attempt < UPLOAD_RETRIES:
                last_response = response
                wait_seconds = attempt * 2
                print(
                    f"    Transient HTTP {response.status_code} during {label} "
                    f"(attempt {attempt}/{UPLOAD_RETRIES}), retrying in {wait_seconds}s..."
                )
                time.sleep(wait_seconds)
                continue

            response.raise_for_status()
            return response

        except RETRYABLE_EXCEPTIONS as exc:
            last_error = exc

            if attempt >= UPLOAD_RETRIES:
                break

            wait_seconds = attempt * 2
            print(
                f"    Transient network error during {label}: {exc} "
                f"(attempt {attempt}/{UPLOAD_RETRIES}), retrying in {wait_seconds}s..."
            )
            time.sleep(wait_seconds)

    if last_response is not None:
        last_response.raise_for_status()

    raise RuntimeError(f"Failed during {label} after {UPLOAD_RETRIES} attempts.") from last_error


def upload_file(cdn_url: str, fork_id: str, token: str, file_path: str) -> None:
    cdn_url = normalize_cdn_url(cdn_url)

    upload_url = f"{cdn_url}fork/{fork_id}/publish/file"
    file_name = os.path.basename(file_path)

    headers = {
        "Authorization": f"Bearer {token}",
        "Connection": "close",
        "Content-Type": "application/octet-stream",
        "Robust-Cdn-Publish-File": file_name,
        "Robust-Cdn-Publish-Version": VERSION,
    }

    last_error = None
    last_response = None

    for attempt in range(1, UPLOAD_RETRIES + 1):
        try:
            with requests.Session() as upload_session:
                with open(file_path, "rb") as file_handle:
                    response = upload_session.post(
                        upload_url,
                        data=file_handle,
                        headers=headers,
                        timeout=(30, 600),
                    )

            if response.status_code in TRANSIENT_STATUS_CODES and attempt < UPLOAD_RETRIES:
                last_response = response
                wait_seconds = attempt * 2
                print(
                    f"    Transient HTTP {response.status_code} while uploading {file_name} "
                    f"(attempt {attempt}/{UPLOAD_RETRIES}), retrying in {wait_seconds}s..."
                )
                time.sleep(wait_seconds)
                continue

            response.raise_for_status()
            return

        except RETRYABLE_EXCEPTIONS as exc:
            last_error = exc

            if attempt >= UPLOAD_RETRIES:
                break

            wait_seconds = attempt * 2
            print(
                f"    Transient upload error for {file_name}: {exc} "
                f"(attempt {attempt}/{UPLOAD_RETRIES}), retrying in {wait_seconds}s..."
            )
            time.sleep(wait_seconds)

    if last_response is not None:
        last_response.raise_for_status()

    raise RuntimeError(f"Failed to upload {file_name} after {UPLOAD_RETRIES} attempts.") from last_error


def publish_to_cdn(cdn_url: str, fork_id: str, token: str, engine_version: str) -> None:
    cdn_url = normalize_cdn_url(cdn_url)

    print(f"\n===== Publishing WH14K to Robust.Cdn =====")
    print(f"CDN: {cdn_url}")
    print(f"Fork: {fork_id}")
    print(f"Version: {VERSION}")
    print(f"Engine version: {engine_version}")

    start_payload = {
        "version": VERSION,
        "engineVersion": engine_version,
    }

    post_with_retry(
        f"{cdn_url}fork/{fork_id}/publish/start",
        token=token,
        json_payload=start_payload,
        headers={"Content-Type": "application/json"},
        label="starting publish",
    )

    print("Publish started, uploading files...")

    for file_path in get_files_to_publish():
        print(f"  Uploading {file_path}")
        upload_file(cdn_url, fork_id, token, file_path)

    print("Files uploaded, finishing publish...")

    finish_payload = {
        "version": VERSION,
    }

    post_with_retry(
        f"{cdn_url}fork/{fork_id}/publish/finish",
        token=token,
        json_payload=finish_payload,
        headers={"Content-Type": "application/json"},
        label="finishing publish",
    )

    print("===== WH14K publish SUCCESS =====")
    print(f"Build page: {cdn_url}fork/{fork_id}")
    print(f"Manifest: {cdn_url}fork/{fork_id}/manifest\n")


def get_files_to_publish() -> Iterable[str]:
    if not os.path.isdir(RELEASE_DIR):
        raise RuntimeError(f"Release directory does not exist: {RELEASE_DIR}")

    files = sorted(os.listdir(RELEASE_DIR))

    if not files:
        raise RuntimeError(f"Release directory is empty: {RELEASE_DIR}")

    for file_name in files:
        file_path = os.path.join(RELEASE_DIR, file_name)

        if os.path.isfile(file_path):
            yield file_path


def get_engine_version() -> str:
    proc = subprocess.run(
        ["git", "describe", "--tags", "--abbrev=0"],
        stdout=subprocess.PIPE,
        cwd="RobustToolbox",
        check=True,
        encoding="UTF-8",
    )

    tag = proc.stdout.strip()

    if not tag.startswith("v"):
        raise RuntimeError(f"Unexpected RobustToolbox tag format: {tag}")

    return tag[1:]


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--cdn-url", default=CDN_URL)
    parser.add_argument("--fork-id", default=FORK_ID)

    args = parser.parse_args()

    engine_version = get_engine_version()

    publish_to_cdn(
        cdn_url=args.cdn_url,
        fork_id=args.fork_id,
        token=PUBLISH_TOKEN,
        engine_version=engine_version,
    )


if __name__ == "__main__":
    main()
