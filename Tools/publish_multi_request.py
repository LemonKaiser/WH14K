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
# CONFIGURATION PARAMETERS
# Forks should change these to publish to their own infrastructure.
#

# Primary CDN — game server CDN (simplestation), server gets builds from here.
PRIMARY_CDN_URL = "https://cdn.simplestation.org/"
PRIMARY_FORK_ID = "ebengrad"

# Mirror CDN — your own CDN, clients should download from here.
# Set MIRROR_PUBLISH_TOKEN env var to enable mirror publishing.
MIRROR_CDN_URL = "https://cdn.heretec.online/"
MIRROR_FORK_ID = "heretec-online"

UPLOAD_RETRIES = 4
TRANSIENT_STATUS_CODES = {408, 425, 429, 500, 502, 503, 504}
RETRYABLE_EXCEPTIONS = (
    requests.exceptions.ConnectionError,
    requests.exceptions.SSLError,
    requests.exceptions.Timeout,
)


def post_json(session: requests.Session, url: str, *, payload: dict, headers: dict | None = None) -> requests.Response:
    resp = session.post(url, json=payload, headers=headers, timeout=(15, 120))
    resp.raise_for_status()
    return resp


def upload_file(cdn_url: str, fork_id: str, token: str, file_path: str) -> None:
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

    for attempt in range(1, UPLOAD_RETRIES + 1):
        try:
            with requests.Session() as upload_session:
                with open(file_path, "rb") as file_handle:
                    resp = upload_session.post(
                        upload_url,
                        data=file_handle,
                        headers=headers,
                        timeout=(30, 600),
                    )

            if resp.status_code in TRANSIENT_STATUS_CODES and attempt < UPLOAD_RETRIES:
                wait_seconds = attempt * 2
                print(
                    f"    Transient HTTP {resp.status_code} while uploading {file_name} "
                    f"(attempt {attempt}/{UPLOAD_RETRIES}), retrying in {wait_seconds}s..."
                )
                time.sleep(wait_seconds)
                continue

            resp.raise_for_status()
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

    raise RuntimeError(f"Failed to upload {file_name} after {UPLOAD_RETRIES} attempts.") from last_error


def publish_to_cdn(cdn_url, fork_id, token, engine_version, label="CDN"):
    """Publish the release to a single Robust.Cdn instance."""
    session = requests.Session()
    session.headers = {
        "Authorization": f"Bearer {token}",
    }

    print(f"\n===== Publishing to {label}: {cdn_url}fork/{fork_id}/ =====")
    print(f"Version: {VERSION}")

    data = {
        "version": VERSION,
        "engineVersion": engine_version,
    }
    headers = {
        "Content-Type": "application/json"
    }
    post_json(session, f"{cdn_url}fork/{fork_id}/publish/start", payload=data, headers=headers)
    print("Publish started, adding files...")

    for file in get_files_to_publish():
        print(f"  Uploading {file}")
        upload_file(cdn_url, fork_id, token, file)

    print("Files pushed, finishing publish...")

    data = {
        "version": VERSION
    }
    headers = {
        "Content-Type": "application/json"
    }
    post_json(session, f"{cdn_url}fork/{fork_id}/publish/finish", payload=data, headers=headers)

    print(f"===== {label} publish SUCCESS! =====\n")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--fork-id", default=PRIMARY_FORK_ID)
    parser.add_argument("--mirror-fork-id", default=MIRROR_FORK_ID)

    args = parser.parse_args()

    engine_version = get_engine_version()

    # 1. Publish to primary CDN (game server CDN)
    publish_to_cdn(
        cdn_url=PRIMARY_CDN_URL,
        fork_id=args.fork_id,
        token=PUBLISH_TOKEN,
        engine_version=engine_version,
        label="Primary (game server CDN)"
    )

    # 2. Publish to mirror CDN (your own CDN for client downloads)
    mirror_token = os.environ.get("MIRROR_PUBLISH_TOKEN", "")
    if mirror_token:
        publish_to_cdn(
            cdn_url=MIRROR_CDN_URL,
            fork_id=args.mirror_fork_id,
            token=mirror_token,
            engine_version=engine_version,
            label="Mirror (client download CDN)"
        )
    else:
        print("MIRROR_PUBLISH_TOKEN not set, skipping mirror CDN publish.")


def get_files_to_publish() -> Iterable[str]:
    for file in sorted(os.listdir(RELEASE_DIR)):
        yield os.path.join(RELEASE_DIR, file)


def get_engine_version() -> str:
    proc = subprocess.run(["git", "describe","--tags", "--abbrev=0"], stdout=subprocess.PIPE, cwd="RobustToolbox", check=True, encoding="UTF-8")
    tag = proc.stdout.strip()
    assert tag.startswith("v")
    return tag[1:] # Cut off v prefix.


if __name__ == '__main__':
    main()
