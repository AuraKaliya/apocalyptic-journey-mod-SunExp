#!/usr/bin/env python3
"""Train an offline sequence Transformer world model and emit policy annotations."""

from __future__ import annotations

import argparse
import copy
import ctypes
import hashlib
import json
import math
import os
import platform
import random
import sys
import tempfile
import threading
import time
from pathlib import Path

try:
    import torch
    from torch import nn
    from torch.utils.data import DataLoader, Dataset, Sampler
except ImportError as exc:
    print(
        "PyTorch is required. Run tools/Setup-AuraTransformerTeacher.ps1 "
        "for the CPU or CUDA backend.",
        file=sys.stderr,
    )
    raise SystemExit(5) from exc


def arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input")
    parser.add_argument("--annotations")
    parser.add_argument("--model")
    parser.add_argument("--report")
    parser.add_argument("--backend", choices=("auto", "cpu", "cuda"), default="auto")
    parser.add_argument("--epochs", type=int, default=12)
    parser.add_argument("--batch-size", type=int, default=64)
    parser.add_argument("--hidden", type=int, default=384)
    parser.add_argument("--layers", type=int, default=6)
    parser.add_argument("--heads", type=int, default=8)
    parser.add_argument("--ffn", type=int, default=1536)
    parser.add_argument("--history", type=int, default=12)
    parser.add_argument("--cpu-threads", type=int, default=0)
    parser.add_argument("--cpu-interop-threads", type=int, default=0)
    parser.add_argument("--micro-batch-size", type=int, default=0)
    parser.add_argument("--loader-workers", type=int, default=0)
    parser.add_argument("--prefetch-batches", type=int, default=2)
    parser.add_argument("--pin-memory", type=int, choices=(0, 1), default=1)
    parser.add_argument("--mixed-precision", type=int, choices=(0, 1), default=1)
    parser.add_argument("--runtime-cache", default="")
    parser.add_argument("--anchor", default="")
    parser.add_argument("--fixed-anchor", type=int, choices=(0, 1), default=1)
    parser.add_argument("--maximum-head-regression", type=float, default=0.05)
    parser.add_argument("--resume-model", default="")
    parser.add_argument("--training-enabled", type=int, choices=(0, 1), default=1)
    parser.add_argument("--seed", type=int, default=1701)
    parser.add_argument("--self-test", action="store_true")
    return parser.parse_args()


PROGRESS_PREFIX = "AURA_TEACHER_PROGRESS "


def working_set_bytes() -> int:
    if os.name != "nt":
        try:
            import resource

            value = int(resource.getrusage(resource.RUSAGE_SELF).ru_maxrss)
            return value if sys.platform == "darwin" else value * 1024
        except (ImportError, OSError, ValueError):
            return 0
    try:
        class ProcessMemoryCounters(ctypes.Structure):
            _fields_ = [
                ("cb", ctypes.c_ulong),
                ("PageFaultCount", ctypes.c_ulong),
                ("PeakWorkingSetSize", ctypes.c_size_t),
                ("WorkingSetSize", ctypes.c_size_t),
                ("QuotaPeakPagedPoolUsage", ctypes.c_size_t),
                ("QuotaPagedPoolUsage", ctypes.c_size_t),
                ("QuotaPeakNonPagedPoolUsage", ctypes.c_size_t),
                ("QuotaNonPagedPoolUsage", ctypes.c_size_t),
                ("PagefileUsage", ctypes.c_size_t),
                ("PeakPagefileUsage", ctypes.c_size_t),
            ]

        counters = ProcessMemoryCounters()
        counters.cb = ctypes.sizeof(counters)
        kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        psapi = ctypes.WinDLL("psapi", use_last_error=True)
        kernel32.GetCurrentProcess.restype = ctypes.c_void_p
        psapi.GetProcessMemoryInfo.argtypes = (
            ctypes.c_void_p,
            ctypes.POINTER(ProcessMemoryCounters),
            ctypes.c_ulong,
        )
        psapi.GetProcessMemoryInfo.restype = ctypes.c_int
        handle = kernel32.GetCurrentProcess()
        if psapi.GetProcessMemoryInfo(
            handle, ctypes.byref(counters), counters.cb
        ):
            return int(counters.WorkingSetSize)
    except (AttributeError, OSError, ValueError):
        pass
    return 0


class ProgressReporter:
    def __init__(self, enabled: bool = True):
        self.enabled = enabled
        self.started = time.perf_counter()
        self.cpu_started = time.process_time()
        self.stage_started = self.started
        self.stage_seconds = {}
        self.peak_working_set_bytes = 0
        self.lock = threading.Lock()
        self.stop_event = threading.Event()
        self.state = {
            "Stage": "starting",
            "Epoch": 0,
            "TotalEpochs": 0,
            "CompletedFrames": 0,
            "TotalFrames": 0,
            "FramesPerSecond": 0.0,
            "EstimatedRemainingSeconds": 0.0,
            "WarmStarted": False,
            "TrainingEnabled": True,
            "Message": "",
        }
        self.thread = threading.Thread(target=self._heartbeat, daemon=True)
        if enabled:
            self.thread.start()

    def update(self, emit: bool = True, **values) -> None:
        if not self.enabled:
            return
        now = time.perf_counter()
        with self.lock:
            next_stage = values.get("Stage")
            current_stage = str(self.state.get("Stage", "starting"))
            if next_stage and str(next_stage) != current_stage:
                self.stage_seconds[current_stage] = (
                    self.stage_seconds.get(current_stage, 0.0)
                    + max(0.0, now - self.stage_started)
                )
                self.stage_started = now
            self.state.update(values)
        if emit:
            self.emit()

    def emit(self) -> None:
        if not self.enabled:
            return
        now = time.perf_counter()
        elapsed = max(1.0e-6, now - self.started)
        cpu = max(0.0, time.process_time() - self.cpu_started)
        working_set = working_set_bytes()
        with self.lock:
            payload = dict(self.state)
            self.peak_working_set_bytes = max(
                self.peak_working_set_bytes, working_set
            )
            current_stage = str(payload.get("Stage", "starting"))
            stage_elapsed = max(0.0, now - self.stage_started)
            stage_seconds = dict(self.stage_seconds)
            stage_seconds[current_stage] = (
                stage_seconds.get(current_stage, 0.0) + stage_elapsed
            )
        payload["ElapsedSeconds"] = elapsed
        payload["ProcessCpuSeconds"] = cpu
        payload["ProcessCpuPercent"] = (
            cpu / elapsed / max(1, os.cpu_count() or 1) * 100.0
        )
        payload["WorkingSetBytes"] = working_set
        payload["PeakWorkingSetBytes"] = self.peak_working_set_bytes
        payload["StageElapsedSeconds"] = stage_elapsed
        payload["StageSeconds"] = stage_seconds
        print(PROGRESS_PREFIX + json.dumps(payload, separators=(",", ":")), flush=True)

    def snapshot_stage_seconds(self) -> dict:
        if not self.enabled:
            return {}
        now = time.perf_counter()
        with self.lock:
            snapshot = dict(self.stage_seconds)
            current_stage = str(self.state.get("Stage", "starting"))
            snapshot[current_stage] = (
                snapshot.get(current_stage, 0.0)
                + max(0.0, now - self.stage_started)
            )
        return snapshot

    def _heartbeat(self) -> None:
        while not self.stop_event.wait(2.0):
            self.emit()

    def close(self) -> None:
        if not self.enabled:
            return
        self.stop_event.set()
        self.thread.join(timeout=3.0)


def choose_device(backend: str) -> torch.device:
    if backend == "cuda":
        if not torch.cuda.is_available():
            raise RuntimeError("CUDA backend was requested but torch.cuda.is_available() is false")
        return torch.device("cuda")
    if backend == "auto" and torch.cuda.is_available():
        return torch.device("cuda")
    return torch.device("cpu")


def configure_runtime(args: argparse.Namespace) -> tuple[torch.device, dict]:
    random.seed(args.seed)
    torch.manual_seed(args.seed)
    if torch.cuda.is_available():
        torch.cuda.manual_seed_all(args.seed)
    default_threads = max(1, min(64, torch.get_num_threads()))
    threads = max(1, min(64, args.cpu_threads)) if args.cpu_threads > 0 else default_threads
    torch.set_num_threads(threads)
    interop = (
        max(1, min(8, args.cpu_interop_threads))
        if args.cpu_interop_threads > 0
        else max(1, min(4, math.ceil(threads / 8)))
    )
    try:
        torch.set_num_interop_threads(interop)
    except RuntimeError:
        interop = torch.get_num_interop_threads()
    device = choose_device(args.backend)
    if device.type == "cuda":
        torch.cuda.reset_peak_memory_stats(device)
    return device, {
        "cpu_threads": threads,
        "cpu_interop_threads": interop,
        "default_cpu_threads": default_threads,
        "logical_processors": max(1, os.cpu_count() or 1),
    }


def normalized(values: list[float]) -> list[float]:
    finite = [max(0.0, float(value)) for value in values]
    total = sum(finite)
    if total <= 0.0:
        return [1.0 / max(1, len(finite)) for _ in finite]
    return [value / total for value in finite]


def load_rows(
    path: Path, history_length: int, progress: ProgressReporter | None = None
) -> list[dict]:
    rows: list[dict] = []
    started = time.perf_counter()
    with path.open("r", encoding="utf-8") as stream:
        for line in stream:
            if not line.strip():
                continue
            row = json.loads(line)
            if len(row.get("A", [])) < 1:
                continue
            row["P"] = normalized(row.get("P", []))
            if not row.get("O"):
                row["O"] = [row["S"]]
            row["_history"] = []
            tensorize_row(row)
            rows.append(row)
            if progress is not None and len(rows) % 64 == 0:
                elapsed = max(1.0e-6, time.perf_counter() - started)
                progress.update(
                    emit=False,
                    CompletedFrames=len(rows),
                    FramesPerSecond=len(rows) / elapsed,
                    Message=f"正在读取并张量化数据 {len(rows):,} 帧",
                )
    by_episode: dict[int, list[dict]] = {}
    for row in rows:
        by_episode.setdefault(int(row["E"]), []).append(row)
    for episode_rows in by_episode.values():
        episode_rows.sort(key=lambda row: (int(row["T"]), int(row["Q"]), int(row["F"])))
        for index, row in enumerate(episode_rows):
            row["_history"] = episode_rows[max(0, index - history_length) : index]
    if progress is not None:
        progress.update(
            Stage="indexing",
            CompletedFrames=0,
            TotalFrames=len(rows),
            FramesPerSecond=0.0,
            Message="正在建立 Transformer 序列历史",
        )
    attach_history_tensors(rows, by_episode, history_length, progress)
    release_raw_arrays(rows)
    return rows


def synthetic_rows(count: int = 96) -> list[dict]:
    rows: list[dict] = []
    generator = random.Random(1701)
    for index in range(count):
        action_count = 3 + index % 3
        state = [generator.uniform(-1.0, 1.0) for _ in range(32)]
        actions = [
            [generator.uniform(-1.0, 1.0) for _ in range(24)]
            for _ in range(action_count)
        ]
        chosen = max(range(action_count), key=lambda action: actions[action][0] + state[0])
        policy = [0.05 / max(1, action_count - 1) for _ in range(action_count)]
        policy[chosen] = 0.95
        rows.append(
            {
                "I": index,
                "E": index // 8,
                "F": index % 8,
                "T": index % 8,
                "Q": index,
                "S": state,
                "O": [
                    [generator.uniform(-1.0, 1.0) for _ in range(32)]
                    for _ in range(4 + index % 4)
                ],
                "A": actions,
                "P": policy,
                "X": chosen,
                "V": math.tanh(state[0] + actions[chosen][0]),
                "G": index % 5,
                "N": [
                    math.tanh(value + actions[chosen][slot % len(actions[chosen])] * 0.05)
                    for slot, value in enumerate(state)
                ],
                "M": 1 if index % 8 != 7 else 0,
                "W": 1.0 if state[0] > 0.0 else 0.0,
                "R": 1.0 if state[0] < -0.5 else 0.0,
                "H": max(0.0, min(1.0, (state[0] + 1.0) * 0.5)),
                "U": float(7 - index % 8),
                "Z": 1 if index % 8 == 7 else 0,
                "_history": [],
            }
        )
    by_episode: dict[int, list[dict]] = {}
    for row in rows:
        by_episode.setdefault(row["E"], []).append(row)
    for episode_rows in by_episode.values():
        for index, row in enumerate(episode_rows):
            row["_history"] = episode_rows[max(0, index - 4) : index]
    return rows


def tensorize_row(row: dict) -> None:
    if "_state_tensor" in row:
        return
    # The persistent JSON remains float-compatible, while CPU-side feature
    # storage uses fp16 until collation. This halves the resident replay tensor
    # footprint without changing fp32 targets or the exported model format.
    storage_dtype = torch.float16
    state = torch.as_tensor(row["S"], dtype=storage_dtype)
    actions = torch.as_tensor(row["A"], dtype=storage_dtype)
    objects = torch.as_tensor(
        row.get("O") or [row["S"]], dtype=storage_dtype
    )
    policy = torch.as_tensor(row["P"], dtype=torch.float32)
    next_state = torch.as_tensor(row.get("N", []), dtype=torch.float32)
    row["_state_tensor"] = state
    row["_action_tensor"] = actions
    row["_object_tensor"] = objects
    row["_policy_tensor"] = policy
    row["_next_state_tensor"] = next_state
    row["_sampling_repeats"] = max(
        1, min(3, int(round(float(row.get("K", 1.0)))))
    )
    row["_outcome_tensor"] = torch.tensor(
        [
            float(row.get("W", 0.0)),
            float(row.get("R", 0.0)),
            float(row.get("H", 0.0)),
            math.tanh(max(0.0, float(row.get("U", 0.0))) / 12.0),
        ],
        dtype=torch.float32,
    )


def attach_history_tensors(
    rows: list[dict],
    by_episode: dict[int, list[dict]],
    history_length: int,
    progress: ProgressReporter | None = None,
) -> None:
    for episode_rows in by_episode.values():
        episode_rows.sort(key=lambda row: (int(row["T"]), int(row["Q"]), int(row["F"])))
        for index, row in enumerate(episode_rows):
            row["_history"] = episode_rows[max(0, index - history_length) : index]
    started = time.perf_counter()
    for row_index, row in enumerate(rows, start=1):
        history = row.get("_history", [])
        if history:
            row["_history_state_tensor"] = torch.stack(
                [prior["_state_tensor"] for prior in history]
            )
            history_actions = []
            for prior in history:
                executed = max(
                    0,
                    min(prior["_action_tensor"].shape[0] - 1, int(prior["X"])),
                )
                history_actions.append(prior["_action_tensor"][executed])
            row["_history_action_tensor"] = torch.stack(history_actions)
        else:
            state_dimensions = row["_state_tensor"].shape[0]
            action_dimensions = row["_action_tensor"].shape[1]
            row["_history_state_tensor"] = torch.empty(
                0, state_dimensions, dtype=torch.float16
            )
            row["_history_action_tensor"] = torch.empty(
                0, action_dimensions, dtype=torch.float16
            )
        row["_history"] = []
        row["_bucket_cost"] = (
            int(row["_action_tensor"].shape[0])
            + int(row["_object_tensor"].shape[0])
            + int(row["_history_state_tensor"].shape[0]) * 2
        )
        row["_tensorized_complete"] = True
        if progress is not None and row_index % 64 == 0:
            elapsed = max(1.0e-6, time.perf_counter() - started)
            progress.update(
                emit=False,
                CompletedFrames=row_index,
                TotalFrames=len(rows),
                FramesPerSecond=row_index / elapsed,
                EstimatedRemainingSeconds=(
                    max(0, len(rows) - row_index)
                    / max(1.0e-6, row_index / elapsed)
                ),
            )


def release_raw_arrays(rows: list[dict]) -> None:
    for row in rows:
        for key in ("S", "A", "O", "P", "N"):
            row.pop(key, None)


def tensorize_rows(rows: list[dict]) -> None:
    if rows and all(row.get("_tensorized_complete") for row in rows):
        return
    for row in rows:
        tensorize_row(row)
    by_episode: dict[int, list[dict]] = {}
    for row in rows:
        by_episode.setdefault(int(row["E"]), []).append(row)
    history_length = max(
        (len(row.get("_history", [])) for row in rows), default=0
    )
    attach_history_tensors(rows, by_episode, history_length)
    release_raw_arrays(rows)


class TeacherDataset(Dataset):
    def __init__(self, rows: list[dict]):
        self.rows = rows

    def __len__(self) -> int:
        return len(self.rows)

    def __getitem__(self, index: int) -> dict:
        return self.rows[index]


class LengthBucketBatchSampler(Sampler[list[int]]):
    def __init__(self, rows: list[dict], batch_size: int, seed: int):
        self.rows = rows
        self.batch_size = max(1, batch_size)
        self.seed = seed
        self.epoch = 0

    def __len__(self) -> int:
        samples = sum(int(row.get("_sampling_repeats", 1)) for row in self.rows)
        return math.ceil(samples / self.batch_size)

    def __iter__(self):
        randomizer = random.Random(self.seed + self.epoch * 104729)
        self.epoch += 1
        indices = sorted(
            (
                index
                for index, row in enumerate(self.rows)
                for _ in range(int(row.get("_sampling_repeats", 1)))
            ),
            key=lambda index: self.rows[index]["_bucket_cost"],
        )
        bucket_size = self.batch_size * 8
        buckets = [
            indices[start : start + bucket_size]
            for start in range(0, len(indices), bucket_size)
        ]
        randomizer.shuffle(buckets)
        for bucket in buckets:
            randomizer.shuffle(bucket)
            for start in range(0, len(bucket), self.batch_size):
                yield bucket[start : start + self.batch_size]


def run_key(row: dict) -> str:
    return str(row.get("Y") or f"episode:{int(row['E'])}")


def stable_partition_score(key: str, seed: int) -> int:
    payload = f"{seed}|{key}".encode("utf-8")
    return int.from_bytes(hashlib.sha256(payload).digest()[:8], "big")


def validation_run_ids(
    rows: list[dict], seed: int, initial_ids: set[str] | None = None
) -> set[str]:
    episodes = sorted({run_key(row) for row in rows})
    validation_ids = set(initial_ids or ()).intersection(episodes)
    if not validation_ids:
        validation_ids = {
            key for key in episodes if stable_partition_score(key, seed) % 5 == 0
        }
    if not validation_ids:
        validation_ids = {
            min(episodes, key=lambda key: (stable_partition_score(key, seed), key))
        }
    strata: dict[tuple[str, str, int], set[str]] = {}
    for row in rows:
        stratum = (
            str(row.get("C", "normal")),
            str(row.get("L", "general")),
            int(row.get("J", 0)),
        )
        strata.setdefault(stratum, set()).add(run_key(row))
    for stratum_ids in strata.values():
        if len(stratum_ids) < 2 or validation_ids.intersection(stratum_ids):
            continue
        candidates = stratum_ids.difference(validation_ids)
        if candidates and len(validation_ids) < len(episodes) - 1:
            validation_ids.add(
                min(candidates, key=lambda key: (stable_partition_score(key, seed), key))
            )
    if len(validation_ids) == len(episodes) and len(episodes) > 1:
        validation_ids.remove(
            max(validation_ids, key=lambda key: (stable_partition_score(key, seed), key))
        )
    return validation_ids


def split_rows(rows: list[dict], seed: int) -> tuple[list[dict], list[dict]]:
    validation_ids = validation_run_ids(rows, seed)
    training = [row for row in rows if run_key(row) not in validation_ids]
    validation = [row for row in rows if run_key(row) in validation_ids]
    if not training:
        training = rows[:-1]
        validation = rows[-1:]
    return training, validation


def write_anchor_rows(path: Path, rows: list[dict]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    with temporary.open("w", encoding="utf-8") as stream:
        for row in rows:
            payload = {key: value for key, value in row.items() if not key.startswith("_")}
            payload["S"] = row["_state_tensor"].float().tolist()
            payload["A"] = row["_action_tensor"].float().tolist()
            payload["O"] = row["_object_tensor"].float().tolist()
            payload["P"] = row["_policy_tensor"].float().tolist()
            payload["N"] = row["_next_state_tensor"].float().tolist()
            stream.write(json.dumps(payload, separators=(",", ":"), ensure_ascii=True))
            stream.write("\n")
    temporary.replace(path)


def training_and_anchor_rows(
    rows: list[dict], args: argparse.Namespace
) -> tuple[list[dict], list[dict], bool]:
    if not args.fixed_anchor or not args.anchor:
        training, validation = split_rows(rows, args.seed)
        return training, validation, False
    anchor_path = Path(args.anchor)
    if anchor_path.exists():
        validation = load_rows(anchor_path, args.history)
        tensorize_rows(validation)
        anchor_keys = {run_key(row) for row in validation}
        expanded_keys = validation_run_ids(rows, args.seed, anchor_keys)
        added_keys = expanded_keys.difference(anchor_keys)
        if added_keys:
            validation.extend(
                row for row in rows if run_key(row) in added_keys
            )
            anchor_keys.update(added_keys)
            write_anchor_rows(anchor_path, validation)
        training = [row for row in rows if run_key(row) not in anchor_keys]
        if training and validation:
            return training, validation, False
    training, validation = split_rows(rows, args.seed)
    write_anchor_rows(anchor_path, validation)
    return training, validation, True


def collate(rows: list[dict]) -> dict[str, torch.Tensor]:
    batch = len(rows)
    state_dimensions = rows[0]["_state_tensor"].shape[0]
    action_dimensions = rows[0]["_action_tensor"].shape[1]
    maximum_actions = max(row["_action_tensor"].shape[0] for row in rows)
    maximum_objects = max(row["_object_tensor"].shape[0] for row in rows)
    maximum_history = max(row["_history_state_tensor"].shape[0] for row in rows)
    states = torch.zeros(batch, state_dimensions)
    actions = torch.zeros(batch, maximum_actions, action_dimensions)
    object_tokens = torch.zeros(batch, maximum_objects, state_dimensions)
    object_mask = torch.zeros(batch, maximum_objects, dtype=torch.bool)
    action_mask = torch.zeros(batch, maximum_actions, dtype=torch.bool)
    policy_supervision_mask = torch.zeros(batch, dtype=torch.bool)
    policies = torch.zeros(batch, maximum_actions)
    history_states = torch.zeros(batch, maximum_history, state_dimensions)
    history_actions = torch.zeros(batch, maximum_history, action_dimensions)
    history_mask = torch.zeros(batch, maximum_history, dtype=torch.bool)
    values = torch.zeros(batch)
    strategies = torch.full((batch,), -1, dtype=torch.long)
    executed_actions = torch.zeros(batch, dtype=torch.long)
    next_states = torch.zeros(batch, state_dimensions)
    transition_mask = torch.zeros(batch, dtype=torch.bool)
    outcomes = torch.zeros(batch, 4)
    terminals = torch.zeros(batch)
    row_ids = torch.zeros(batch, dtype=torch.long)
    for owner, row in enumerate(rows):
        states[owner] = row["_state_tensor"]
        objects = row["_object_tensor"]
        object_count = min(maximum_objects, objects.shape[0])
        object_tokens[owner, :object_count] = objects[:object_count]
        object_mask[owner, :object_count] = True
        action_count = row["_action_tensor"].shape[0]
        actions[owner, :action_count] = row["_action_tensor"]
        action_mask[owner, :action_count] = True
        policy_supervision_mask[owner] = action_count > 1
        policies[owner, :action_count] = row["_policy_tensor"]
        history_states_for_row = row["_history_state_tensor"]
        history_actions_for_row = row["_history_action_tensor"]
        history_count = history_states_for_row.shape[0]
        offset = maximum_history - history_count
        if history_count > 0:
            history_states[owner, offset:] = history_states_for_row
            history_actions[owner, offset:] = history_actions_for_row
            history_mask[owner, offset:] = True
        values[owner] = float(row["V"])
        strategies[owner] = int(row.get("G", -1))
        executed_actions[owner] = max(0, min(action_count - 1, int(row["X"])))
        if (
            int(row.get("M", 0)) > 0
            and row["_next_state_tensor"].numel() == state_dimensions
        ):
            next_states[owner] = row["_next_state_tensor"]
            transition_mask[owner] = True
        outcomes[owner] = row["_outcome_tensor"]
        terminals[owner] = 1.0 if int(row.get("Z", 0)) > 0 else 0.0
        row_ids[owner] = int(row["I"])
    return {
        "states": states,
        "object_tokens": object_tokens,
        "object_mask": object_mask,
        "actions": actions,
        "action_mask": action_mask,
        "policy_supervision_mask": policy_supervision_mask,
        "policies": policies,
        "history_states": history_states,
        "history_actions": history_actions,
        "history_mask": history_mask,
        "values": values,
        "strategies": strategies,
        "executed_actions": executed_actions,
        "next_states": next_states,
        "transition_mask": transition_mask,
        "outcomes": outcomes,
        "terminals": terminals,
        "row_ids": row_ids,
    }


class StrategyTransformer(nn.Module):
    def __init__(
        self,
        state_dimensions: int,
        action_dimensions: int,
        hidden: int,
        layers: int,
        heads: int,
        feedforward: int,
        history_length: int,
    ):
        super().__init__()
        self.hidden = hidden
        self.history_length = history_length
        self.cls = nn.Parameter(torch.zeros(1, 1, hidden))
        self.state_projection = nn.Linear(state_dimensions, hidden)
        self.object_projection = nn.Linear(state_dimensions, hidden)
        self.action_projection = nn.Linear(action_dimensions, hidden)
        self.history_projection = nn.Linear(state_dimensions + action_dimensions, hidden)
        self.type_embedding = nn.Embedding(5, hidden)
        self.position_embedding = nn.Embedding(history_length + 2, hidden)
        layer = nn.TransformerEncoderLayer(
            d_model=hidden,
            nhead=heads,
            dim_feedforward=max(hidden, feedforward),
            dropout=0.05,
            activation="gelu",
            batch_first=True,
            norm_first=True,
        )
        self.encoder = nn.TransformerEncoder(layer, layers, norm=nn.LayerNorm(hidden))
        self.policy_head = nn.Linear(hidden, 1)
        self.value_head = nn.Sequential(nn.Linear(hidden, hidden), nn.GELU(), nn.Linear(hidden, 1))
        self.strategy_head = nn.Linear(hidden, 5)
        self.dynamics_head = nn.Sequential(
            nn.Linear(hidden * 2, hidden),
            nn.GELU(),
            nn.Linear(hidden, state_dimensions),
        )
        self.outcome_head = nn.Sequential(
            nn.Linear(hidden, hidden), nn.GELU(), nn.Linear(hidden, 4), nn.Sigmoid()
        )
        self.terminal_head = nn.Linear(hidden * 2, 1)
        nn.init.normal_(self.cls, std=0.02)

    def forward(self, batch: dict[str, torch.Tensor]) -> tuple[torch.Tensor, ...]:
        states = batch["states"]
        actions = batch["actions"]
        history_states = batch["history_states"]
        history_actions = batch["history_actions"]
        history_mask = batch["history_mask"]
        action_mask = batch["action_mask"]
        object_tokens = batch["object_tokens"]
        object_mask = batch["object_mask"]
        owners = states.shape[0]
        history_count = history_states.shape[1]
        action_count = actions.shape[1]
        object_count = object_tokens.shape[1]
        cls = self.cls.expand(owners, -1, -1) + self.type_embedding.weight[0]
        if history_count:
            history = self.history_projection(
                torch.cat((history_states, history_actions), dim=-1)
            )
            positions = torch.arange(history_count, device=states.device).clamp_max(
                self.history_length - 1
            )
            history = history + self.type_embedding.weight[1] + self.position_embedding(positions)
        else:
            history = states.new_zeros((owners, 0, self.hidden))
        state_token = self.state_projection(states).unsqueeze(1)
        state_token = state_token + self.type_embedding.weight[2]
        objects = self.object_projection(object_tokens) + self.type_embedding.weight[3]
        action_tokens = self.action_projection(actions) + self.type_embedding.weight[4]
        tokens = torch.cat((cls, history, objects, state_token, action_tokens), dim=1)
        padding = torch.cat(
            (
                torch.zeros(owners, 1, dtype=torch.bool, device=states.device),
                ~history_mask,
                ~object_mask,
                torch.zeros(owners, 1, dtype=torch.bool, device=states.device),
                ~action_mask,
            ),
            dim=1,
        )
        encoded = self.encoder(tokens, src_key_padding_mask=padding)
        cls_encoded = encoded[:, 0]
        action_start = 1 + history_count + object_count + 1
        action_encoded = encoded[:, action_start : action_start + action_count]
        policy = self.policy_head(action_encoded).squeeze(-1)
        policy = policy.masked_fill(~action_mask, -1.0e9)
        value = torch.tanh(self.value_head(cls_encoded).squeeze(-1))
        strategy = self.strategy_head(cls_encoded)
        executed = batch["executed_actions"].clamp(0, max(0, action_count - 1))
        executed_encoded = action_encoded[
            torch.arange(owners, device=states.device), executed
        ]
        transition_context = torch.cat((cls_encoded, executed_encoded), dim=-1)
        next_state = self.dynamics_head(transition_context)
        outcome = self.outcome_head(cls_encoded)
        terminal = self.terminal_head(transition_context).squeeze(-1)
        return policy, value, strategy, next_state, outcome, terminal


def move(batch: dict[str, torch.Tensor], device: torch.device) -> dict[str, torch.Tensor]:
    return {key: value.to(device, non_blocking=device.type == "cuda") for key, value in batch.items()}


def slice_batch(
    batch: dict[str, torch.Tensor], start: int, end: int
) -> dict[str, torch.Tensor]:
    return {key: value[start:end] for key, value in batch.items()}


def precision_context(device: torch.device, precision: str):
    enabled = device.type == "cuda" and precision != "float32"
    dtype = torch.bfloat16 if precision == "bfloat16" else torch.float16
    return torch.autocast(device_type=device.type, dtype=dtype, enabled=enabled)


def loader_options(plan: dict, device: torch.device) -> dict:
    workers = max(0, int(plan["loader_workers"]))
    result = {
        "num_workers": workers,
        "pin_memory": bool(plan["pinned_memory"] and device.type == "cuda"),
        "persistent_workers": workers > 0,
    }
    if workers > 0:
        result["prefetch_factor"] = max(1, int(plan["prefetch_batches"]))
    return result


def runtime_cache_key(
    args: argparse.Namespace,
    device: torch.device,
    state_dimensions: int,
    action_dimensions: int,
) -> str:
    device_name = (
        torch.cuda.get_device_name(device)
        if device.type == "cuda"
        else platform.processor() or platform.machine()
    )
    payload = "|".join(
        str(value)
        for value in (
            "transformer-runtime-auto-tune-v2-backprop",
            platform.system(),
            platform.machine(),
            platform.processor(),
            os.cpu_count(),
            torch.__version__,
            device.type,
            device_name,
            state_dimensions,
            action_dimensions,
            args.hidden,
            args.layers,
            args.heads,
            args.ffn,
            args.history,
            args.batch_size,
        )
    )
    return hashlib.sha256(payload.encode("utf-8")).hexdigest()


def load_runtime_cache(path: str, key: str) -> dict | None:
    if not path:
        return None
    try:
        payload = json.loads(Path(path).read_text(encoding="utf-8"))
        entry = payload.get("entries", {}).get(key)
        return entry if isinstance(entry, dict) else None
    except (OSError, ValueError, TypeError):
        return None


def save_runtime_cache(path: str, key: str, plan: dict) -> None:
    if not path:
        return
    destination = Path(path)
    try:
        payload = json.loads(destination.read_text(encoding="utf-8")) if destination.exists() else {}
    except (OSError, ValueError, TypeError):
        payload = {}
    entries = payload.setdefault("entries", {})
    entries[key] = {
        "cpu_threads": int(plan["cpu_threads"]),
        "cpu_interop_threads": int(plan["cpu_interop_threads"]),
        "micro_batch_size": int(plan["micro_batch_size"]),
        "loader_workers": int(plan["loader_workers"]),
        "prefetch_batches": int(plan["prefetch_batches"]),
        "pinned_memory": bool(plan["pinned_memory"]),
        "precision": str(plan["precision"]),
        "measured_utc": time.time(),
    }
    payload["protocol"] = "transformer-runtime-auto-tune-v2-backprop"
    destination.parent.mkdir(parents=True, exist_ok=True)
    temporary = destination.with_suffix(destination.suffix + ".tmp")
    temporary.write_text(json.dumps(payload, indent=2), encoding="utf-8")
    temporary.replace(destination)


def cpu_thread_candidates(default_threads: int, logical_processors: int) -> list[int]:
    limit = max(1, min(64, logical_processors))
    preferred = {
        max(1, default_threads // 2),
        default_threads,
        limit,
        4,
        6,
        8,
        12,
        14,
        16,
        24,
        32,
    }
    available = sorted(value for value in preferred if value <= limit)
    if len(available) <= 6:
        return available
    mandatory = {default_threads, limit, max(1, default_threads // 2)}
    optional = sorted(
        (value for value in available if value not in mandatory),
        key=lambda value: (abs(value - default_threads), value),
    )
    return sorted(mandatory.union(optional[: max(0, 6 - len(mandatory))]))


def benchmark_cpu_threads(
    model: StrategyTransformer,
    raw: dict[str, torch.Tensor],
    runtime: dict,
) -> int:
    sample_size = min(32, raw["states"].shape[0])
    sample = slice_batch(raw, 0, sample_size)
    candidates = cpu_thread_candidates(
        int(runtime["default_cpu_threads"]), int(runtime["logical_processors"])
    )
    best_threads = int(runtime["cpu_threads"])
    best_rate = -1.0
    model.train()
    for threads in candidates:
        torch.set_num_threads(threads)
        model.zero_grad(set_to_none=True)
        warmup, _ = loss_for(model, sample)
        warmup.backward()
        model.zero_grad(set_to_none=True)
        started = time.perf_counter()
        repeats = 2
        for _ in range(repeats):
            total, _ = loss_for(model, sample)
            total.backward()
            model.zero_grad(set_to_none=True)
        elapsed = max(1.0e-6, time.perf_counter() - started)
        rate = sample_size * repeats / elapsed
        if rate > best_rate:
            best_rate = rate
            best_threads = threads
    torch.set_num_threads(best_threads)
    return best_threads


def choose_gpu_micro_batch(
    model: StrategyTransformer,
    raw: dict[str, torch.Tensor],
    device: torch.device,
    precision: str,
    maximum: int,
) -> int:
    candidate = max(1, min(maximum, raw["states"].shape[0]))
    while candidate >= 1:
        try:
            sample = move(slice_batch(raw, 0, candidate), device)
            model.zero_grad(set_to_none=True)
            with precision_context(device, precision):
                total, _ = loss_for(model, sample)
            total.backward()
            model.zero_grad(set_to_none=True)
            torch.cuda.synchronize(device)
            return candidate
        except (torch.cuda.OutOfMemoryError, RuntimeError) as error:
            if not isinstance(error, torch.cuda.OutOfMemoryError) and "out of memory" not in str(error).lower():
                raise
            model.zero_grad(set_to_none=True)
            torch.cuda.empty_cache()
            candidate //= 2
    raise RuntimeError("Transformer teacher cannot fit a single frame in GPU memory")


def resolve_runtime_plan(
    model: StrategyTransformer,
    sample_raw: dict[str, torch.Tensor],
    args: argparse.Namespace,
    device: torch.device,
    runtime: dict,
) -> dict:
    started = time.perf_counter()
    state_dimensions = sample_raw["states"].shape[1]
    action_dimensions = sample_raw["actions"].shape[2]
    key = runtime_cache_key(args, device, state_dimensions, action_dimensions)
    cached = load_runtime_cache(args.runtime_cache, key)
    precision = "float32"
    if device.type == "cuda" and args.mixed_precision:
        precision = "bfloat16" if torch.cuda.is_bf16_supported() else "float16"
    plan = {
        **runtime,
        "micro_batch_size": args.micro_batch_size or args.batch_size,
        "loader_workers": (
            args.loader_workers
            if args.loader_workers > 0
            else (min(2, max(1, (os.cpu_count() or 1) // 4)) if device.type == "cuda" else 0)
        ),
        "prefetch_batches": max(1, min(8, args.prefetch_batches)),
        "pinned_memory": bool(args.pin_memory and device.type == "cuda"),
        "precision": precision,
        "auto_tuned": False,
        "cache_hit": cached is not None,
    }
    if cached is not None:
        used_cache = False
        if args.cpu_threads <= 0:
            plan["cpu_threads"] = int(cached.get("cpu_threads", plan["cpu_threads"]))
            torch.set_num_threads(plan["cpu_threads"])
            used_cache = True
        if args.micro_batch_size <= 0:
            plan["micro_batch_size"] = int(
                cached.get("micro_batch_size", plan["micro_batch_size"])
            )
            used_cache = True
        if args.loader_workers <= 0:
            plan["loader_workers"] = int(
                cached.get("loader_workers", plan["loader_workers"])
            )
            used_cache = True
        plan["cache_hit"] = used_cache
        plan["auto_tuned"] = used_cache
    elif not args.self_test:
        if device.type == "cpu" and args.cpu_threads <= 0:
            plan["cpu_threads"] = benchmark_cpu_threads(model, sample_raw, runtime)
            plan["auto_tuned"] = True
        if device.type == "cuda" and args.micro_batch_size <= 0:
            plan["micro_batch_size"] = choose_gpu_micro_batch(
                model,
                sample_raw,
                device,
                precision,
                args.batch_size,
            )
            plan["auto_tuned"] = True
        save_runtime_cache(args.runtime_cache, key, plan)
    plan["micro_batch_size"] = max(
        1, min(args.batch_size, int(plan["micro_batch_size"]))
    )
    plan["prefetch_batches"] = (
        max(1, int(plan["prefetch_batches"]))
        if int(plan["loader_workers"]) > 0
        else 0
    )
    plan["calibration_seconds"] = time.perf_counter() - started
    return plan


def loss_for(
    model: StrategyTransformer,
    batch: dict[str, torch.Tensor],
    effective_counts: dict[str, int] | None = None,
) -> tuple[torch.Tensor, dict[str, float]]:
    (
        policy_logits,
        values,
        strategy_logits,
        predicted_next_states,
        predicted_outcomes,
        terminal_logits,
    ) = model(batch)
    log_policy = torch.log_softmax(policy_logits, dim=-1)
    policy_supervision_mask = batch["policy_supervision_mask"]
    policy_losses = -(batch["policies"] * log_policy).sum(dim=-1)
    policy_loss = (
        policy_losses[policy_supervision_mask].mean()
        if policy_supervision_mask.any()
        else policy_losses.sum() * 0.0
    )
    value_loss = torch.nn.functional.mse_loss(values, batch["values"])
    valid_strategy = batch["strategies"] >= 0
    if valid_strategy.any():
        strategy_loss = torch.nn.functional.cross_entropy(
            strategy_logits[valid_strategy], batch["strategies"][valid_strategy]
        )
    else:
        strategy_loss = policy_loss.new_zeros(())
    transition_mask = batch["transition_mask"]
    if transition_mask.any():
        dynamics_loss = torch.nn.functional.mse_loss(
            predicted_next_states[transition_mask],
            batch["next_states"][transition_mask],
        )
    else:
        dynamics_loss = policy_loss.new_zeros(())
    outcome_loss = torch.nn.functional.mse_loss(
        predicted_outcomes, batch["outcomes"]
    )
    terminal_loss = torch.nn.functional.binary_cross_entropy_with_logits(
        terminal_logits, batch["terminals"]
    )
    if effective_counts is None:
        policy_weight = 1.0
        sample_weight = 1.0
        strategy_weight = 1.0
        dynamics_weight = 1.0
    else:
        policy_weight = int(policy_supervision_mask.sum()) / max(
            1, effective_counts["policies"]
        )
        sample_weight = batch["states"].shape[0] / max(
            1, effective_counts["samples"]
        )
        strategy_weight = int(valid_strategy.sum()) / max(
            1, effective_counts["strategies"]
        )
        dynamics_weight = int(transition_mask.sum()) / max(
            1, effective_counts["transitions"]
        )
    total = (
        policy_loss * policy_weight
        + value_loss * 0.35 * sample_weight
        + strategy_loss * 0.20 * strategy_weight
        + dynamics_loss * 0.15 * dynamics_weight
        + outcome_loss * 0.20 * sample_weight
        + terminal_loss * 0.10 * sample_weight
    )
    return total, {
        "policy": float(policy_loss.detach()),
        "value": float(value_loss.detach()),
        "strategy": float(strategy_loss.detach()),
        "dynamics": float(dynamics_loss.detach()),
        "outcome": float(outcome_loss.detach()),
        "terminal": float(terminal_loss.detach()),
    }


def verify_micro_batch_accumulation(
    model: StrategyTransformer,
    raw: dict[str, torch.Tensor],
    device: torch.device,
) -> None:
    full_model = copy.deepcopy(model).to(device).eval()
    micro_model = copy.deepcopy(model).to(device).eval()
    full_batch = move(raw, device)
    full_loss, _ = loss_for(full_model, full_batch)
    full_loss.backward()
    effective_counts = {
        "samples": int(raw["states"].shape[0]),
        "policies": int(raw["policy_supervision_mask"].sum()),
        "strategies": int((raw["strategies"] >= 0).sum()),
        "transitions": int(raw["transition_mask"].sum()),
    }
    for start in range(0, effective_counts["samples"], 3):
        micro = move(
            slice_batch(raw, start, min(effective_counts["samples"], start + 3)),
            device,
        )
        micro_loss, _ = loss_for(micro_model, micro, effective_counts)
        micro_loss.backward()
    for full_parameter, micro_parameter in zip(
        full_model.parameters(), micro_model.parameters()
    ):
        if full_parameter.grad is None and micro_parameter.grad is None:
            continue
        assert full_parameter.grad is not None and micro_parameter.grad is not None
        if not torch.allclose(
            full_parameter.grad,
            micro_parameter.grad,
            rtol=2.0e-4,
            atol=2.0e-5,
        ):
            raise AssertionError(
                "microbatch accumulation changed effective-batch gradients"
            )


@torch.no_grad()
def evaluate(
    model: StrategyTransformer,
    loader: DataLoader,
    device: torch.device,
    precision: str = "float32",
) -> dict[str, float]:
    model.eval()
    count = 0
    policy_count = 0
    policy_cross_entropy = 0.0
    uniform_policy_cross_entropy = 0.0
    policy_correct = 0
    value_error = 0.0
    strategy_count = 0
    strategy_correct = 0
    dynamics_squared_error = 0.0
    dynamics_elements = 0
    outcome_absolute_error = 0.0
    outcome_elements = 0
    death_squared_error = 0.0
    terminal_correct = 0
    for raw in loader:
        batch = move(raw, device)
        with precision_context(device, precision):
            (
                policy_logits,
                values,
                strategy_logits,
                predicted_next_states,
                predicted_outcomes,
                terminal_logits,
            ) = model(batch)
        log_policy = torch.log_softmax(policy_logits, dim=-1)
        size = batch["states"].shape[0]
        policy_mask = batch["policy_supervision_mask"]
        policy_count += int(policy_mask.sum())
        if policy_mask.any():
            policy_cross_entropy += float(
                (-(batch["policies"] * log_policy).sum(dim=-1))[policy_mask].sum()
            )
            policy_correct += int(
                (
                    policy_logits[policy_mask].argmax(dim=-1)
                    == batch["policies"][policy_mask].argmax(dim=-1)
                ).sum()
            )
            uniform_policy_cross_entropy += float(
                batch["action_mask"][policy_mask].sum(dim=-1).float().log().sum()
            )
        value_error += float((values - batch["values"]).abs().sum())
        valid = batch["strategies"] >= 0
        strategy_count += int(valid.sum())
        if valid.any():
            strategy_correct += int(
                (
                    strategy_logits[valid].argmax(dim=-1)
                    == batch["strategies"][valid]
                ).sum()
            )
        transition_mask = batch["transition_mask"]
        if transition_mask.any():
            difference = (
                predicted_next_states[transition_mask]
                - batch["next_states"][transition_mask]
            )
            dynamics_squared_error += float((difference * difference).sum())
            dynamics_elements += difference.numel()
        outcome_difference = predicted_outcomes - batch["outcomes"]
        outcome_absolute_error += float(outcome_difference.abs().sum())
        outcome_elements += outcome_difference.numel()
        death_squared_error += float((outcome_difference[:, 1] ** 2).sum())
        terminal_correct += int(
            ((terminal_logits >= 0.0) == (batch["terminals"] >= 0.5)).sum()
        )
        count += size
    return {
        "policy_ce": policy_cross_entropy / max(1, policy_count),
        "uniform_policy_ce": uniform_policy_cross_entropy / max(1, policy_count),
        "policy_accuracy": policy_correct / max(1, policy_count),
        "value_mae": value_error / max(1, count),
        "strategy_accuracy": strategy_correct / max(1, strategy_count),
        "dynamics_mse": dynamics_squared_error / max(1, dynamics_elements),
        "dynamics_frames": sum(
            int(row.get("M", 0)) > 0 for row in loader.dataset.rows
        ),
        "outcome_mae": outcome_absolute_error / max(1, outcome_elements),
        "death_brier": death_squared_error / max(1, count),
        "terminal_accuracy": terminal_correct / max(1, count),
    }


def load_warm_start(
    model: StrategyTransformer,
    path: str,
    args: argparse.Namespace,
    device: torch.device,
) -> tuple[bool, int]:
    if not path:
        return False, 0
    source = Path(path)
    if not source.exists():
        return False, 0
    try:
        checkpoint = torch.load(source, map_location=device, weights_only=True)
    except TypeError:
        checkpoint = torch.load(source, map_location=device)
    expected = {
        "hidden_dimensions": args.hidden,
        "layers": args.layers,
        "heads": args.heads,
        "feedforward_dimensions": args.ffn,
        "history_length": args.history,
    }
    incompatible = [
        key
        for key, value in expected.items()
        if int(checkpoint.get(key, -1)) != int(value)
    ]
    if incompatible:
        raise RuntimeError(
            "Transformer warm-start checkpoint is incompatible: "
            + ", ".join(incompatible)
        )
    model.load_state_dict(checkpoint["state_dict"], strict=True)
    return True, max(0, int(checkpoint.get("teacher_generation", 0)))


def composite_score(metrics: dict[str, float]) -> float:
    return (
        metrics["policy_ce"]
        + metrics["value_mae"] * 0.35
        + metrics["dynamics_mse"] * 0.15
        + metrics["outcome_mae"] * 0.20
        + metrics["death_brier"] * 0.20
    )


def head_regression_passed(
    baseline: dict[str, float],
    candidate: dict[str, float],
    maximum_regression: float,
) -> bool:
    allowed = max(0.0, min(0.50, float(maximum_regression)))
    for key in ("value_mae", "outcome_mae", "death_brier"):
        reference = float(baseline[key])
        tolerance = max(1.0e-6, abs(reference) * allowed)
        if not math.isfinite(float(candidate[key])) or candidate[key] > reference + tolerance:
            return False
    return True


def train(
    rows: list[dict],
    args: argparse.Namespace,
    device: torch.device,
    runtime: dict,
    progress: ProgressReporter,
) -> tuple[StrategyTransformer, dict[str, float], int, int, int, dict, float, bool, dict, dict]:
    training_rows, validation_rows, anchor_created = training_and_anchor_rows(
        rows, args
    )
    model = StrategyTransformer(
        rows[0]["_state_tensor"].shape[0],
        rows[0]["_action_tensor"].shape[1],
        args.hidden,
        args.layers,
        args.heads,
        args.ffn,
        args.history,
    ).to(device)
    warm_started, prior_generation = load_warm_start(
        model, args.resume_model, args, device
    )
    progress.update(
        Stage="calibrating",
        TotalEpochs=max(0, args.epochs),
        WarmStarted=warm_started,
        TrainingEnabled=bool(args.training_enabled),
        Message="正在校准 Transformer 执行计划",
    )
    sample_raw = collate(training_rows[: min(args.batch_size, len(training_rows))])
    plan = resolve_runtime_plan(model, sample_raw, args, device, runtime)
    worker_options = loader_options(plan, device)
    training_dataset = TeacherDataset(training_rows)
    training_loader = DataLoader(
        training_dataset,
        batch_sampler=LengthBucketBatchSampler(
            training_rows, args.batch_size, args.seed
        ),
        collate_fn=collate,
        **worker_options,
    )
    validation_loader = DataLoader(
        TeacherDataset(validation_rows),
        batch_size=plan["micro_batch_size"],
        shuffle=False,
        collate_fn=collate,
        **worker_options,
    )
    optimizer = torch.optim.AdamW(model.parameters(), lr=3.0e-4, weight_decay=1.0e-3)
    use_scaler = device.type == "cuda" and plan["precision"] == "float16"
    try:
        scaler = torch.amp.GradScaler("cuda", enabled=use_scaler)
    except (AttributeError, TypeError):
        scaler = torch.cuda.amp.GradScaler(enabled=use_scaler)
    best_state = copy.deepcopy(model.state_dict())
    evaluation_started = time.perf_counter()
    best_metrics = evaluate(model, validation_loader, device, plan["precision"])
    baseline_metrics = dict(best_metrics)
    evaluation_seconds = time.perf_counter() - evaluation_started
    best_loss = composite_score(best_metrics)
    update_accepted = False
    head_gate_passed = True
    stale = 0
    executed = 0
    processed_frames = 0
    training_started = time.perf_counter()
    total_frame_work = sum(
        int(row.get("_sampling_repeats", 1)) for row in training_rows
    ) * max(0, args.epochs)
    if not args.training_enabled:
        progress.update(
            Stage="evaluating",
            TotalEpochs=0,
            TotalFrames=len(validation_rows),
            Message="正在评估复用的 Transformer 教师",
        )
        return (
            model,
            best_metrics,
            0,
            len(training_rows),
            len(validation_rows),
            plan,
            0.0,
            warm_started,
            {
                "training": 0.0,
                "evaluating": evaluation_seconds,
            },
            {
                "baseline_metrics": baseline_metrics,
                "baseline_score": best_loss,
                "validation_score": best_loss,
                "update_accepted": False,
                "head_gate_passed": True,
                "teacher_generation": prior_generation,
                "anchor_frames": len(validation_rows),
                "anchor_created": anchor_created,
            },
        )
    training_seconds = 0.0
    for epoch in range(1, args.epochs + 1):
        progress.update(
            Stage="training",
            Epoch=epoch,
            TotalEpochs=args.epochs,
            CompletedFrames=processed_frames,
            TotalFrames=total_frame_work,
            Message=f"正在训练 Epoch {epoch}/{args.epochs}",
        )
        model.train()
        epoch_training_started = time.perf_counter()
        for raw in training_loader:
            optimizer.zero_grad(set_to_none=True)
            batch_count = raw["states"].shape[0]
            effective_counts = {
                "samples": batch_count,
                "policies": int(raw["policy_supervision_mask"].sum()),
                "strategies": int((raw["strategies"] >= 0).sum()),
                "transitions": int(raw["transition_mask"].sum()),
            }
            micro_batch = max(1, int(plan["micro_batch_size"]))
            for start in range(0, batch_count, micro_batch):
                end = min(batch_count, start + micro_batch)
                batch = move(slice_batch(raw, start, end), device)
                with precision_context(device, plan["precision"]):
                    total, _ = loss_for(model, batch, effective_counts)
                scaler.scale(total).backward()
            if use_scaler:
                scaler.unscale_(optimizer)
            torch.nn.utils.clip_grad_norm_(model.parameters(), 1.0)
            scaler.step(optimizer)
            scaler.update()
            processed_frames += batch_count
            elapsed = max(1.0e-6, time.perf_counter() - training_started)
            rate = processed_frames / elapsed
            progress.update(
                emit=False,
                CompletedFrames=processed_frames,
                FramesPerSecond=rate,
                EstimatedRemainingSeconds=(
                    max(0, total_frame_work - processed_frames) / max(1.0e-6, rate)
                ),
            )
        training_seconds += time.perf_counter() - epoch_training_started
        executed = epoch
        progress.update(
            Stage="evaluating",
            Epoch=epoch,
            Message=f"正在评估 Epoch {epoch}/{args.epochs}",
        )
        evaluation_started = time.perf_counter()
        metrics = evaluate(model, validation_loader, device, plan["precision"])
        evaluation_seconds += time.perf_counter() - evaluation_started
        score = composite_score(metrics)
        epoch_head_gate = (
            not warm_started
            or head_regression_passed(
                baseline_metrics,
                metrics,
                args.maximum_head_regression,
            )
        )
        print(
            f"epoch={epoch}/{args.epochs} policyCE={metrics['policy_ce']:.6f} "
            f"top1={metrics['policy_accuracy']:.4f} valueMAE={metrics['value_mae']:.6f} "
            f"dynamicsMSE={metrics['dynamics_mse']:.6f}",
            flush=True,
        )
        if score < best_loss - 1.0e-5 and epoch_head_gate:
            best_loss = score
            best_metrics = metrics
            best_state = copy.deepcopy(model.state_dict())
            stale = 0
            update_accepted = True
            head_gate_passed = True
        else:
            stale += 1
            if score < best_loss - 1.0e-5 and not epoch_head_gate:
                head_gate_passed = False
        if epoch >= 4 and stale >= 4:
            break
    model.load_state_dict(best_state)
    head_gate_passed = (
        not warm_started
        or head_regression_passed(
            baseline_metrics,
            best_metrics,
            args.maximum_head_regression,
        )
    )
    training_seconds = max(1.0e-6, training_seconds)
    return (
        model,
        best_metrics,
        executed,
        len(training_rows),
        len(validation_rows),
        plan,
        processed_frames / training_seconds,
        warm_started,
        {
            "training": training_seconds,
            "evaluating": evaluation_seconds,
        },
        {
            "baseline_metrics": baseline_metrics,
            "baseline_score": composite_score(baseline_metrics),
            "validation_score": composite_score(best_metrics),
            "update_accepted": update_accepted,
            "head_gate_passed": head_gate_passed,
            "teacher_generation": prior_generation + (1 if update_accepted else 0),
            "anchor_frames": len(validation_rows),
            "anchor_created": anchor_created,
        },
    )


@torch.no_grad()
def annotate(
    model: StrategyTransformer,
    rows: list[dict],
    plan: dict,
    device: torch.device,
    path: Path,
    progress: ProgressReporter,
) -> tuple[float, float]:
    loader = DataLoader(
        TeacherDataset(rows),
        batch_size=plan["micro_batch_size"],
        shuffle=False,
        collate_fn=collate,
        **loader_options(plan, device),
    )
    model.eval()
    completed = 0
    progress.update(
        Stage="annotating",
        Epoch=0,
        TotalEpochs=0,
        CompletedFrames=0,
        TotalFrames=len(rows),
        EstimatedRemainingSeconds=0.0,
        Message="正在生成 Transformer 蒸馏标注",
    )
    started = time.perf_counter()
    with path.open("w", encoding="utf-8", newline="\n") as stream:
        for raw in loader:
            batch = move(raw, device)
            with precision_context(device, plan["precision"]):
                logits, _, _, _, _, _ = model(batch)
            probabilities = torch.softmax(logits, dim=-1).cpu()
            masks = raw["action_mask"]
            row_ids = raw["row_ids"]
            for owner in range(probabilities.shape[0]):
                count = int(masks[owner].sum())
                payload = {
                    "I": int(row_ids[owner]),
                    "P": [float(value) for value in probabilities[owner, :count]],
                }
                stream.write(json.dumps(payload, separators=(",", ":")) + "\n")
            completed += int(probabilities.shape[0])
            elapsed = max(1.0e-6, time.perf_counter() - started)
            rate = completed / elapsed
            progress.update(
                emit=False,
                CompletedFrames=completed,
                FramesPerSecond=rate,
                EstimatedRemainingSeconds=(
                    max(0, len(rows) - completed) / max(1.0e-6, rate)
                ),
            )
    elapsed = max(1.0e-6, time.perf_counter() - started)
    return elapsed, completed / elapsed


def write_report(path: Path, payload: dict) -> None:
    path.write_text(json.dumps(payload, indent=2, ensure_ascii=True), encoding="utf-8")


def main() -> int:
    args = arguments()
    progress = ProgressReporter(enabled=not args.self_test)
    try:
        return execute(args, progress)
    finally:
        progress.close()


def execute(args: argparse.Namespace, progress: ProgressReporter) -> int:
    progress.update(Stage="configuring", Message="正在配置 PyTorch 执行环境")
    device, runtime = configure_runtime(args)
    if args.self_test:
        args.epochs = min(args.epochs, 2)
        args.batch_size = min(args.batch_size, 16)
        args.hidden = min(args.hidden, 64)
        args.layers = min(args.layers, 2)
        args.heads = min(args.heads, 4)
        args.ffn = min(args.ffn, 128)
        rows = synthetic_rows()
        rows[0]["K"] = 2.0
        rows[-1]["A"] = rows[-1]["A"][:1]
        rows[-1]["P"] = [1.0]
        rows[-1]["X"] = 0
        tensorize_rows(rows)
        assert rows[0]["_sampling_repeats"] == 2
        assert not bool(collate(rows[-1:])["policy_supervision_mask"][0])
        (
            model,
            metrics,
            executed,
            training_count,
            validation_count,
            plan,
            throughput,
            warm_started,
            _,
            training_gate,
        ) = train(
            rows, args, device, runtime, progress
        )
        assert math.isfinite(metrics["policy_ce"])
        assert math.isfinite(metrics["dynamics_mse"])
        assert math.isfinite(metrics["outcome_mae"])
        assert executed > 0 and training_count > 0 and validation_count > 0
        assert training_gate["anchor_frames"] == validation_count
        verify_micro_batch_accumulation(
            model,
            collate(rows[: min(8, len(rows))]),
            device,
        )
        with tempfile.TemporaryDirectory(prefix="aura-teacher-self-test-") as root:
            checkpoint_path = Path(root) / "warm.pt"
            torch.save(
                {
                    "protocol": "aura.combat-transformer-world-model.v2",
                    "state_dimensions": rows[0]["_state_tensor"].shape[0],
                    "action_dimensions": rows[0]["_action_tensor"].shape[1],
                    "hidden_dimensions": args.hidden,
                    "layers": args.layers,
                    "heads": args.heads,
                    "feedforward_dimensions": args.ffn,
                    "history_length": args.history,
                    "state_dict": model.state_dict(),
                },
                checkpoint_path,
            )
            prior_resume = args.resume_model
            prior_training_enabled = args.training_enabled
            prior_epochs = args.epochs
            prior_anchor = args.anchor
            prior_fixed_anchor = args.fixed_anchor
            try:
                args.resume_model = str(checkpoint_path)
                args.training_enabled = 0
                args.epochs = 0
                args.anchor = str(Path(root) / "fixed-anchor.jsonl")
                args.fixed_anchor = 1
                (
                    _,
                    warm_metrics,
                    warm_executed,
                    _,
                    _,
                    _,
                    _,
                    warm_started,
                    _,
                    warm_gate,
                ) = train(rows, args, device, runtime, progress)
                assert warm_started and warm_executed == 0
                assert not warm_gate["update_accepted"]
                assert warm_gate["anchor_created"]
                assert Path(args.anchor).exists()
                assert math.isfinite(warm_metrics["policy_ce"])
            finally:
                args.resume_model = prior_resume
                args.training_enabled = prior_training_enabled
                args.epochs = prior_epochs
                args.anchor = prior_anchor
                args.fixed_anchor = prior_fixed_anchor
        if os.name == "nt":
            assert working_set_bytes() > 0
        print(
            json.dumps(
                {
                    "success": True,
                    "device": str(device),
                    "torch": torch.__version__,
                    "policyCE": metrics["policy_ce"],
                    "uniformPolicyCE": metrics["uniform_policy_ce"],
                    "dynamicsMSE": metrics["dynamics_mse"],
                    "outcomeMAE": metrics["outcome_mae"],
                    "cpuThreads": plan["cpu_threads"],
                    "microBatch": plan["micro_batch_size"],
                    "throughput": throughput,
                }
            )
        )
        return 0
    required = (args.input, args.annotations, args.model, args.report)
    if any(not value for value in required):
        raise RuntimeError("input, annotations, model, and report paths are required")
    started = time.perf_counter()
    cpu_started = time.process_time()
    progress.update(Stage="loading", Message="正在读取 Transformer 数据集")
    loading_started = time.perf_counter()
    rows = load_rows(Path(args.input), args.history, progress)
    loading_seconds = time.perf_counter() - loading_started
    if not rows:
        raise RuntimeError("Transformer teacher dataset contains no usable frames")
    preparation_started = time.perf_counter()
    progress.update(
        Stage="preparing",
        TotalFrames=len(rows),
        Message="正在张量化并建立序列历史",
    )
    tensorize_rows(rows)
    preparation_seconds = time.perf_counter() - preparation_started
    (
        model,
        metrics,
        executed,
        training_count,
        validation_count,
        plan,
        throughput,
        warm_started,
        training_timings,
        training_gate,
    ) = train(
        rows, args, device, runtime, progress
    )
    annotation_seconds, annotation_throughput = annotate(
        model, rows, plan, device, Path(args.annotations), progress
    )
    progress.update(Stage="saving", Message="正在写入模型和教师报告")
    saving_started = time.perf_counter()
    checkpoint = {
        "protocol": "aura.combat-transformer-world-model.v2",
        "state_dimensions": rows[0]["_state_tensor"].shape[0],
        "action_dimensions": rows[0]["_action_tensor"].shape[1],
        "hidden_dimensions": args.hidden,
        "layers": args.layers,
        "heads": args.heads,
        "feedforward_dimensions": args.ffn,
        "history_length": args.history,
        "teacher_generation": int(training_gate["teacher_generation"]),
        "state_dict": model.state_dict(),
    }
    torch.save(checkpoint, args.model)
    saving_seconds = time.perf_counter() - saving_started
    device_name = (
        torch.cuda.get_device_name(device) if device.type == "cuda" else platform.processor()
    )
    stage_seconds = progress.snapshot_stage_seconds()
    stage_seconds.update(
        {
            "loading": loading_seconds,
            "preparing": preparation_seconds,
            "calibrating": float(plan["calibration_seconds"]),
            "training": float(training_timings["training"]),
            "evaluating": float(training_timings["evaluating"]),
            "annotating": annotation_seconds,
            "saving": saving_seconds,
        }
    )
    report = {
        "Protocol": "aura.combat-transformer-world-model-report.v2",
        "Success": True,
        "EffectiveBackend": device.type,
        "DeviceName": device_name,
        "PythonVersion": platform.python_version(),
        "TorchVersion": torch.__version__,
        "NumpyVersion": __import__("numpy").__version__,
        "RuntimeAutoTuned": bool(plan["auto_tuned"]),
        "RuntimeAutoTuneCacheHit": bool(plan["cache_hit"]),
        "EffectiveCpuThreads": int(plan["cpu_threads"]),
        "EffectiveCpuInteropThreads": int(plan["cpu_interop_threads"]),
        "EffectiveBatchSize": int(args.batch_size),
        "EffectiveMicroBatchSize": int(plan["micro_batch_size"]),
        "EffectiveDataLoaderWorkers": int(plan["loader_workers"]),
        "EffectivePrefetchBatches": int(plan["prefetch_batches"]),
        "PinnedMemoryEnabled": bool(plan["pinned_memory"]),
        "NumericPrecision": str(plan["precision"]),
        "ParameterCount": sum(parameter.numel() for parameter in model.parameters()),
        "HiddenDimensions": args.hidden,
        "Layers": args.layers,
        "AttentionHeads": args.heads,
        "FeedForwardDimensions": args.ffn,
        "TrainingFrames": training_count,
        "ValidationFrames": validation_count,
        "EpochsExecuted": executed,
        "RequestedEpochs": max(0, args.epochs),
        "WarmStarted": warm_started,
        "TrainingRefreshed": bool(args.training_enabled),
        "UpdateAccepted": bool(training_gate["update_accepted"]),
        "TeacherGeneration": int(training_gate["teacher_generation"]),
        "ResumeModelPath": args.resume_model if warm_started else "",
        "ValidationPolicyCrossEntropy": metrics["policy_ce"],
        "ValidationUniformPolicyCrossEntropy": metrics["uniform_policy_ce"],
        "ValidationPolicyTop1Accuracy": metrics["policy_accuracy"],
        "ValidationValueMae": metrics["value_mae"],
        "ValidationStrategyAccuracy": metrics["strategy_accuracy"],
        "ValidationDynamicsMse": metrics["dynamics_mse"],
        "DynamicsTrainingFrames": metrics["dynamics_frames"],
        "ValidationOutcomeMae": metrics["outcome_mae"],
        "ValidationDeathBrier": metrics["death_brier"],
        "ValidationTerminalAccuracy": metrics["terminal_accuracy"],
        "AnchorValidationFrames": int(training_gate["anchor_frames"]),
        "AnchorCreated": bool(training_gate["anchor_created"]),
        "AnchorPath": args.anchor if args.fixed_anchor else "",
        "BaselinePolicyCrossEntropy": training_gate["baseline_metrics"]["policy_ce"],
        "BaselineValueMae": training_gate["baseline_metrics"]["value_mae"],
        "BaselineOutcomeMae": training_gate["baseline_metrics"]["outcome_mae"],
        "BaselineDeathBrier": training_gate["baseline_metrics"]["death_brier"],
        "ValidationCompositeScore": training_gate["validation_score"],
        "BaselineCompositeScore": training_gate["baseline_score"],
        "CompositeImprovement": (
            training_gate["baseline_score"] - training_gate["validation_score"]
        ),
        "HeadRegressionGatePassed": bool(training_gate["head_gate_passed"]),
        "ElapsedSeconds": time.perf_counter() - started,
        "ProcessCpuSeconds": time.process_time() - cpu_started,
        "PeakWorkingSetBytes": max(
            progress.peak_working_set_bytes, working_set_bytes()
        ),
        "DataLoadingSeconds": loading_seconds,
        "DataPreparationSeconds": preparation_seconds,
        "RuntimeCalibrationSeconds": float(plan["calibration_seconds"]),
        "TrainingSeconds": float(training_timings["training"]),
        "EvaluationSeconds": float(training_timings["evaluating"]),
        "AnnotationSeconds": annotation_seconds,
        "SavingSeconds": saving_seconds,
        "StageSeconds": stage_seconds,
        "TrainingFramesPerSecond": throughput,
        "AnnotationFramesPerSecond": annotation_throughput,
        "PeakDeviceMemoryBytes": (
            int(torch.cuda.max_memory_allocated(device)) if device.type == "cuda" else 0
        ),
        "Message": "Transformer teacher training completed.",
    }
    write_report(Path(args.report), report)
    progress.update(
        Stage="completed",
        CompletedFrames=len(rows),
        TotalFrames=len(rows),
        EstimatedRemainingSeconds=0.0,
        WarmStarted=warm_started,
        TrainingEnabled=bool(args.training_enabled),
        Message="Transformer 教师已完成",
    )
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(f"Transformer teacher failed: {exc}", file=sys.stderr)
        raise
