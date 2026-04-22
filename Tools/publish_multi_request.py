#!/usr/bin/env python3

import argparse
import requests
import os
import subprocess
from typing import Iterable

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
    resp = session.post(f"{cdn_url}fork/{fork_id}/publish/start", json=data, headers=headers)
    resp.raise_for_status()
    print("Publish started, adding files...")

    for file in get_files_to_publish():
        print(f"  Uploading {file}")
        with open(file, "rb") as f:
            headers = {
                "Content-Type": "application/octet-stream",
                "Robust-Cdn-Publish-File": os.path.basename(file),
                "Robust-Cdn-Publish-Version": VERSION
            }
            resp = session.post(f"{cdn_url}fork/{fork_id}/publish/file", data=f, headers=headers)

        resp.raise_for_status()

    print("Files pushed, finishing publish...")

    data = {
        "version": VERSION
    }
    headers = {
        "Content-Type": "application/json"
    }
    resp = session.post(f"{cdn_url}fork/{fork_id}/publish/finish", json=data, headers=headers)
    resp.raise_for_status()

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
    for file in os.listdir(RELEASE_DIR):
        yield os.path.join(RELEASE_DIR, file)


def get_engine_version() -> str:
    proc = subprocess.run(["git", "describe","--tags", "--abbrev=0"], stdout=subprocess.PIPE, cwd="RobustToolbox", check=True, encoding="UTF-8")
    tag = proc.stdout.strip()
    assert tag.startswith("v")
    return tag[1:] # Cut off v prefix.


if __name__ == '__main__':
    main()
