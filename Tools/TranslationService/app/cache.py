from __future__ import annotations

from collections import OrderedDict
from dataclasses import dataclass
from threading import RLock
from time import monotonic
from typing import Generic, TypeVar

T = TypeVar("T")


@dataclass
class _Entry(Generic[T]):
    value: T
    expires_at: float


class TTLCache(Generic[T]):
    def __init__(self, max_items: int, ttl_seconds: int) -> None:
        self._max_items = max(1, max_items)
        self._ttl_seconds = max(1, ttl_seconds)
        self._items: OrderedDict[str, _Entry[T]] = OrderedDict()
        self._lock = RLock()
        self._last_prune: float = 0.0
        self._prune_interval: float = min(ttl_seconds / 4.0, 60.0)

    @property
    def size(self) -> int:
        with self._lock:
            return len(self._items)

    def get(self, key: str) -> T | None:
        with self._lock:
            entry = self._items.get(key)
            if entry is None:
                return None

            if entry.expires_at <= monotonic():
                self._items.pop(key, None)
                return None

            self._items.move_to_end(key)
            self._maybe_prune_locked()
            return entry.value

    def put(self, key: str, value: T) -> None:
        now = monotonic()
        with self._lock:
            self._items.pop(key, None)
            self._items[key] = _Entry(value=value, expires_at=now + self._ttl_seconds)

            while len(self._items) > self._max_items:
                self._items.popitem(last=False)

            self._maybe_prune_locked()

    def _maybe_prune_locked(self) -> None:
        now = monotonic()
        if now - self._last_prune < self._prune_interval:
            return
        self._last_prune = now
        expired_keys = [k for k, e in self._items.items() if e.expires_at <= now]
        for k in expired_keys:
            self._items.pop(k, None)
