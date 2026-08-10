#!/usr/bin/env python3
"""Train an offline sequence Transformer world model and emit policy annotations."""

from __future__ import annotations

import argparse
from collections import OrderedDict, deque
import copy
import ctypes
import gc
import hashlib
import json
import math
import os
import platform
import random
import shutil
import sqlite3
import sys
import tempfile
import threading
import time
from pathlib import Path

# CUDA deterministic GEMM requires this to be present before the first CUDA
# context is initialized. The value is intentionally fixed for repeatable
# continuation runs and does not depend on host-local tuning.
os.environ.setdefault("CUBLAS_WORKSPACE_CONFIG", ":4096:8")

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
    parser.add_argument(
        "--dataset-storage",
        choices=("auto", "resident", "sharded"),
        default="auto",
    )
    parser.add_argument("--dataset-shard-frames", type=int, default=512)
    parser.add_argument("--corpus-frames", type=int, default=0)
    parser.add_argument("--resident-dataset-maximum-frames", type=int, default=4096)
    parser.add_argument("--pin-memory", type=int, choices=(0, 1), default=1)
    parser.add_argument("--mixed-precision", type=int, choices=(0, 1), default=1)
    parser.add_argument("--deterministic", type=int, choices=(0, 1), default=1)
    parser.add_argument("--runtime-cache", default="")
    parser.add_argument("--annotation-cache", default="")
    parser.add_argument("--anchor", default="")
    parser.add_argument("--fixed-anchor", type=int, choices=(0, 1), default=1)
    parser.add_argument("--maximum-head-regression", type=float, default=0.05)
    parser.add_argument("--resume-model", default="")
    parser.add_argument("--prior-report", default="")
    parser.add_argument("--training-selection", default="")
    parser.add_argument("--annotation-selection", default="")
    parser.add_argument("--training-enabled", type=int, choices=(0, 1), default=1)
    parser.add_argument("--seed", type=int, default=1701)
    parser.add_argument("--self-test", action="store_true")
    return parser.parse_args()


PROGRESS_PREFIX = "AURA_TEACHER_PROGRESS "
NUMPY_LEGACY_SEED_MASK = (1 << 32) - 1


class CudaTrainingOutOfMemory(RuntimeError):
    """Raised after every safe CUDA micro-batch backoff is exhausted."""


def is_cuda_out_of_memory(error: BaseException) -> bool:
    return isinstance(error, torch.cuda.OutOfMemoryError) or (
        isinstance(error, RuntimeError)
        and "out of memory" in str(error).lower()
    )


def numpy_legacy_seed(seed: int) -> int:
    """Map a signed host seed into NumPy's legacy uint32 seed domain."""
    return int(seed) & NUMPY_LEGACY_SEED_MASK


def reseed(seed: int) -> None:
    """Reset every training RNG after host-local runtime calibration."""
    random.seed(seed)
    try:
        __import__("numpy").random.seed(numpy_legacy_seed(seed))
    except ImportError:
        pass
    torch.manual_seed(seed)
    if torch.cuda.is_available():
        torch.cuda.manual_seed_all(seed)


def verify_reseed_compatibility() -> None:
    """Verify signed host seeds remain deterministic across RNG backends."""
    numpy = __import__("numpy")
    signed_seed_cases = (
        -2_147_483_648,
        -1_465_117_897,
        -1,
        0,
        1,
        1701,
        2_147_483_647,
    )
    expected_numpy_seeds = (
        2_147_483_648,
        2_829_849_399,
        4_294_967_295,
        0,
        1,
        1701,
        2_147_483_647,
    )
    for seed, expected_numpy_seed in zip(
        signed_seed_cases, expected_numpy_seeds
    ):
        assert numpy_legacy_seed(seed) == expected_numpy_seed

        reseed(seed)
        expected_python = [random.random() for _ in range(8)]
        expected_numpy = numpy.random.random_sample(8).tolist()
        expected_torch = torch.rand(8)

        reseed(seed)
        assert expected_python == [random.random() for _ in range(8)]
        assert expected_numpy == numpy.random.random_sample(8).tolist()
        assert torch.equal(expected_torch, torch.rand(8))


def live_process_tree_ids() -> set[int]:
    process_ids = {os.getpid()}
    try:
        import multiprocessing

        process_ids.update(
            int(child.pid)
            for child in multiprocessing.active_children()
            if child.pid is not None
        )
    except (ImportError, OSError, RuntimeError):
        pass
    return process_ids


def process_cpu_times_by_pid() -> dict[int, float]:
    if os.name != "nt":
        return {os.getpid(): time.process_time()}
    try:
        class FileTime(ctypes.Structure):
            _fields_ = [
                ("low", ctypes.c_ulong),
                ("high", ctypes.c_ulong),
            ]

        kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        kernel32.GetCurrentProcess.restype = ctypes.c_void_p
        kernel32.OpenProcess.argtypes = (
            ctypes.c_ulong,
            ctypes.c_int,
            ctypes.c_ulong,
        )
        kernel32.OpenProcess.restype = ctypes.c_void_p
        kernel32.CloseHandle.argtypes = (ctypes.c_void_p,)
        kernel32.CloseHandle.restype = ctypes.c_int
        kernel32.GetProcessTimes.argtypes = (
            ctypes.c_void_p,
            ctypes.POINTER(FileTime),
            ctypes.POINTER(FileTime),
            ctypes.POINTER(FileTime),
            ctypes.POINTER(FileTime),
        )
        kernel32.GetProcessTimes.restype = ctypes.c_int
        result: dict[int, float] = {}
        for process_id in live_process_tree_ids():
            owned_handle = process_id != os.getpid()
            handle = (
                kernel32.OpenProcess(0x1000, 0, process_id)
                if owned_handle
                else kernel32.GetCurrentProcess()
            )
            if not handle:
                continue
            try:
                creation = FileTime()
                exit_time = FileTime()
                kernel = FileTime()
                user = FileTime()
                if kernel32.GetProcessTimes(
                    handle,
                    ctypes.byref(creation),
                    ctypes.byref(exit_time),
                    ctypes.byref(kernel),
                    ctypes.byref(user),
                ):
                    kernel_ticks = (int(kernel.high) << 32) | int(kernel.low)
                    user_ticks = (int(user.high) << 32) | int(user.low)
                    result[process_id] = (kernel_ticks + user_ticks) / 10_000_000.0
            finally:
                if owned_handle:
                    kernel32.CloseHandle(handle)
        return result
    except (AttributeError, OSError, ValueError):
        return {os.getpid(): time.process_time()}


class ProcessTreeCpuTracker:
    def __init__(self):
        initial = process_cpu_times_by_pid()
        self.baseline = dict(initial)
        self.high_water = dict(initial)
        self.lock = threading.Lock()

    def elapsed_seconds(self) -> float:
        current = process_cpu_times_by_pid()
        with self.lock:
            for process_id, value in current.items():
                self.high_water[process_id] = max(
                    value, self.high_water.get(process_id, 0.0)
                )
            return sum(
                max(0.0, value - self.baseline.get(process_id, 0.0))
                for process_id, value in self.high_water.items()
            )


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
        kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        psapi = ctypes.WinDLL("psapi", use_last_error=True)
        kernel32.GetCurrentProcess.restype = ctypes.c_void_p
        kernel32.OpenProcess.argtypes = (
            ctypes.c_ulong,
            ctypes.c_int,
            ctypes.c_ulong,
        )
        kernel32.OpenProcess.restype = ctypes.c_void_p
        kernel32.CloseHandle.argtypes = (ctypes.c_void_p,)
        kernel32.CloseHandle.restype = ctypes.c_int
        psapi.GetProcessMemoryInfo.argtypes = (
            ctypes.c_void_p,
            ctypes.POINTER(ProcessMemoryCounters),
            ctypes.c_ulong,
        )
        psapi.GetProcessMemoryInfo.restype = ctypes.c_int
        total = 0
        for process_id in live_process_tree_ids():
            owned_handle = process_id != os.getpid()
            handle = (
                kernel32.OpenProcess(0x1000 | 0x0010, 0, process_id)
                if owned_handle
                else kernel32.GetCurrentProcess()
            )
            if not handle:
                continue
            try:
                counters = ProcessMemoryCounters()
                counters.cb = ctypes.sizeof(counters)
                if psapi.GetProcessMemoryInfo(
                    handle, ctypes.byref(counters), counters.cb
                ):
                    total += int(counters.WorkingSetSize)
            finally:
                if owned_handle:
                    kernel32.CloseHandle(handle)
        return total
    except (AttributeError, OSError, ValueError):
        pass
    return 0


class ProgressReporter:
    def __init__(self, enabled: bool = True):
        self.enabled = enabled
        self.started = time.perf_counter()
        self.cpu_tracker = ProcessTreeCpuTracker()
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
        cpu = self.cpu_tracker.elapsed_seconds()
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

    def process_cpu_seconds(self) -> float:
        return self.cpu_tracker.elapsed_seconds()

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
    reseed(args.seed)
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
    deterministic = bool(args.deterministic)
    torch.use_deterministic_algorithms(deterministic)
    if hasattr(torch.backends, "cudnn"):
        torch.backends.cudnn.deterministic = deterministic
        torch.backends.cudnn.benchmark = False
    if device.type == "cuda":
        torch.cuda.reset_peak_memory_stats(device)
    return device, {
        "cpu_threads": threads,
        "cpu_interop_threads": interop,
        "default_cpu_threads": default_threads,
        "logical_processors": max(1, os.cpu_count() or 1),
        "deterministic": deterministic,
    }


def normalized(values: list[float]) -> list[float]:
    finite = [max(0.0, float(value)) for value in values]
    total = sum(finite)
    if total <= 0.0:
        return [1.0 / max(1, len(finite)) for _ in finite]
    return [value / total for value in finite]


def dense_feature_tensor(
    payload,
    dtype: torch.dtype,
    expected_dimensions: int | None = None,
) -> torch.Tensor:
    """Decode sparse-v1 index/value objects and legacy dense arrays."""
    if isinstance(payload, dict):
        dimensions = int(payload.get("D", 0))
        indices = payload.get("I", [])
        values = payload.get("V", [])
        if dimensions <= 0 or dimensions > 16384:
            raise ValueError(f"invalid sparse feature dimension: {dimensions}")
        if expected_dimensions is not None and dimensions != expected_dimensions:
            raise ValueError(
                "sparse feature dimensions do not match: "
                f"expected={expected_dimensions}, actual={dimensions}"
            )
        if len(indices) != len(values):
            raise ValueError("sparse feature index/value lengths do not match")
        result = torch.zeros(dimensions, dtype=dtype)
        if indices:
            index_tensor = torch.as_tensor(indices, dtype=torch.long)
            if bool((index_tensor < 0).any()) or bool(
                (index_tensor >= dimensions).any()
            ):
                raise ValueError("sparse feature index is outside its dimension")
            if int(torch.unique(index_tensor).numel()) != len(indices):
                raise ValueError("sparse feature indices must be unique")
            value_tensor = torch.as_tensor(values, dtype=dtype)
            if not bool(torch.isfinite(value_tensor).all()):
                raise ValueError("sparse feature values must be finite")
            result.index_copy_(0, index_tensor, value_tensor)
        return result
    result = torch.as_tensor(payload or [], dtype=dtype)
    if result.ndim != 1:
        raise ValueError("dense feature payload must be one-dimensional")
    if expected_dimensions is not None and result.numel() != expected_dimensions:
        raise ValueError(
            "dense feature dimensions do not match: "
            f"expected={expected_dimensions}, actual={result.numel()}"
        )
    if not bool(torch.isfinite(result).all()):
        raise ValueError("dense feature values must be finite")
    return result


def feature_matrix(
    payloads,
    dtype: torch.dtype,
    dimensions: int,
) -> torch.Tensor:
    values = list(payloads or [])
    if not values:
        return torch.empty(0, dimensions, dtype=dtype)
    return torch.stack(
        [dense_feature_tensor(value, dtype, dimensions) for value in values]
    )


def sparse_feature_payload(tensor: torch.Tensor) -> dict:
    flattened = tensor.detach().float().reshape(-1)
    indices = torch.nonzero(flattened, as_tuple=False).reshape(-1)
    return {
        "D": int(flattened.numel()),
        "I": [int(value) for value in indices.tolist()],
        "V": [float(value) for value in flattened[indices].tolist()],
    }


def sparse_feature_matrix_payload(tensor: torch.Tensor) -> list[dict]:
    return [sparse_feature_payload(row) for row in tensor]


def read_row_selection(path: str) -> set[int] | None:
    if not path:
        return None
    source = Path(path)
    if not source.exists():
        raise RuntimeError(f"row selection is missing: {source}")
    selected: set[int] = set()
    with source.open("r", encoding="utf-8") as stream:
        for line in stream:
            value = line.strip()
            if value:
                if value.startswith("{"):
                    selected.add(int(json.loads(value)["I"]))
                else:
                    selected.add(int(value))
    return selected


def read_row_identities(path: str) -> dict[int, str]:
    if not path:
        return {}
    source = Path(path)
    if not source.exists():
        return {}
    identities: dict[int, str] = {}
    with source.open("r", encoding="utf-8") as stream:
        for line in stream:
            value = line.strip()
            if not value.startswith("{"):
                continue
            payload = json.loads(value)
            identity = str(payload.get("K", ""))
            if identity:
                identities[int(payload["I"])] = identity
    return identities


def load_rows(
    path: Path,
    history_length: int,
    progress: ProgressReporter | None = None,
    selected_rows: set[int] | None = None,
) -> list[dict]:
    rows: list[dict] = []
    started = time.perf_counter()
    with path.open("r", encoding="utf-8") as stream:
        source_row_index = 0
        for line in stream:
            if not line.strip():
                continue
            if selected_rows is not None and source_row_index not in selected_rows:
                source_row_index += 1
                continue
            row = json.loads(line)
            source_row_index += 1
            if len(row.get("A", [])) < 1:
                continue
            row["P"] = normalized(row.get("P", []))
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
    by_episode: dict[str, list[dict]] = {}
    for row in rows:
        key = run_key(row) + f"|battle:{int(row.get('B', -1))}"
        by_episode.setdefault(key, []).append(row)
    for episode_rows in by_episode.values():
        episode_rows.sort(
            key=lambda row: (
                int(row.get("QD", row.get("F", 0))),
                int(row.get("F", 0)),
            )
        )
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
                "OK": 1,
                "DK": 1,
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
                "SL": [
                    1 if strategy == index % 5 else 0
                    for strategy in range(5)
                ],
                "SA": [1, 1, 1, 1, 1],
                "SK": 1,
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
                "TK": 1,
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
    sparse_encoding = isinstance(row.get("S"), dict)
    state = dense_feature_tensor(row["S"], storage_dtype)
    state_dimensions = int(state.numel())
    actions = feature_matrix(row["A"], storage_dtype, int(
        row["A"][0].get("D", 0)
        if isinstance(row["A"][0], dict)
        else len(row["A"][0])
    ))
    objects = feature_matrix(
        row.get("O", []), storage_dtype, state_dimensions
    )
    policy = torch.as_tensor(row["P"], dtype=torch.float32)
    next_payload = row.get("N", [])
    next_state = dense_feature_tensor(
        next_payload,
        torch.float32,
        state_dimensions if next_payload else None,
    )
    row["_state_tensor"] = state
    row["_action_tensor"] = actions
    row["_object_tensor"] = objects
    row["_policy_tensor"] = policy
    row["_next_state_tensor"] = next_state
    row["_dataset_sparse"] = sparse_encoding
    row["_state_nonzero"] = int(torch.count_nonzero(state))
    row["_action_nonzero"] = int(torch.count_nonzero(actions))
    row["_object_nonzero"] = int(torch.count_nonzero(objects))
    row["_object_count"] = int(objects.shape[0])
    row["_dense_feature_slots"] = int(
        state.numel() + actions.numel() + objects.numel() + next_state.numel()
    )
    row["_nonzero_feature_values"] = int(
        torch.count_nonzero(state)
        + torch.count_nonzero(actions)
        + torch.count_nonzero(objects)
        + torch.count_nonzero(next_state)
    )
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
    by_episode: dict[str, list[dict]],
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
    by_episode: dict[str, list[dict]] = {}
    for row in rows:
        key = run_key(row) + f"|battle:{int(row.get('B', -1))}"
        by_episode.setdefault(key, []).append(row)
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


class ShardedTeacherDataset(Dataset):
    def __init__(
        self,
        source_path: Path,
        root_owner: tempfile.TemporaryDirectory,
        shard_paths: list[Path],
        locations: list[tuple[int, int]],
        metadata: list[dict],
    ):
        self.source_path = source_path
        self.root_owner = root_owner
        self.shard_paths = shard_paths
        self.locations = locations
        self.rows = metadata
        self.cache: OrderedDict[int, list[dict]] = OrderedDict()

    @classmethod
    def build(
        cls,
        path: Path,
        history_length: int,
        shard_frames: int,
        progress: ProgressReporter | None = None,
        selected_rows: set[int] | None = None,
    ) -> "ShardedTeacherDataset":
        owner = tempfile.TemporaryDirectory(prefix="aura-teacher-shards-")
        root = Path(owner.name)
        shard_paths: list[Path] = []
        locations: list[tuple[int, int]] = []
        metadata: list[dict] = []
        shard: list[dict] = []
        histories: OrderedDict[str, deque] = OrderedDict()
        started = time.perf_counter()
        limit = max(256, min(4096, int(shard_frames)))

        def flush() -> None:
            if not shard:
                return
            shard_index = len(shard_paths)
            shard_path = root / f"shard-{shard_index:05d}.pt"
            torch.save(shard, shard_path)
            shard_paths.append(shard_path)
            shard.clear()

        try:
            with path.open("r", encoding="utf-8") as stream:
                source_row_index = 0
                for line in stream:
                    if not line.strip():
                        continue
                    if (
                        selected_rows is not None
                        and source_row_index not in selected_rows
                    ):
                        source_row_index += 1
                        continue
                    row = json.loads(line)
                    source_row_index += 1
                    if len(row.get("A", [])) < 1:
                        continue
                    row["P"] = normalized(row.get("P", []))
                    tensorize_row(row)
                    key = str(
                        row.get("Y")
                        or f"episode:{int(row.get('E', 0))}"
                    ) + f"|battle:{int(row.get('B', -1))}"
                    history = histories.pop(key, deque(maxlen=history_length))
                    histories[key] = history
                    if history:
                        row["_history_state_tensor"] = torch.stack(
                            [item[0] for item in history]
                        )
                        row["_history_action_tensor"] = torch.stack(
                            [item[1] for item in history]
                        )
                    else:
                        row["_history_state_tensor"] = torch.empty(
                            0,
                            row["_state_tensor"].shape[0],
                            dtype=torch.float16,
                        )
                        row["_history_action_tensor"] = torch.empty(
                            0,
                            row["_action_tensor"].shape[1],
                            dtype=torch.float16,
                        )
                    executed = max(
                        0,
                        min(row["_action_tensor"].shape[0] - 1, int(row["X"])),
                    )
                    history.append(
                        (
                            row["_state_tensor"],
                            row["_action_tensor"][executed],
                        )
                    )
                    while len(histories) > 512:
                        histories.popitem(last=False)
                    row["_bucket_cost"] = (
                        int(row["_action_tensor"].shape[0])
                        + int(row["_object_tensor"].shape[0])
                        + int(row["_history_state_tensor"].shape[0]) * 2
                    )
                    row["_tensorized_complete"] = True
                    row["_history"] = []
                    release_raw_arrays([row])
                    shard_index = len(shard_paths)
                    locations.append((shard_index, len(shard)))
                    metadata.append(
                        {
                            "I": int(row.get("I", len(metadata))),
                            "Y": row.get("Y"),
                            "E": int(row.get("E", 0)),
                            "C": row.get("C", "normal"),
                            "L": row.get("L", "general"),
                            "J": int(row.get("J", 0)),
                            "M": int(row.get("M", 0)),
                            "DK": int(row.get("DK", 0)),
                            "SK": int(row.get("SK", 0)),
                            "SL": list(row.get("SL", [])),
                            "SA": list(row.get("SA", [])),
                            "TK": int(row.get("TK", 0)),
                            "_sampling_repeats": int(
                                row.get("_sampling_repeats", 1)
                            ),
                            "_bucket_cost": int(row["_bucket_cost"]),
                            "_action_count": int(
                                row["_action_tensor"].shape[0]
                            ),
                            "_history_count": int(
                                row["_history_state_tensor"].shape[0]
                            ),
                            "_dataset_sparse": bool(row["_dataset_sparse"]),
                            "_state_nonzero": int(row["_state_nonzero"]),
                            "_action_nonzero": int(row["_action_nonzero"]),
                            "_object_nonzero": int(row["_object_nonzero"]),
                            "_object_count": int(row["_object_count"]),
                            "_dense_feature_slots": int(
                                row["_dense_feature_slots"]
                            ),
                            "_nonzero_feature_values": int(
                                row["_nonzero_feature_values"]
                            ),
                        }
                    )
                    shard.append(row)
                    if len(shard) >= limit:
                        flush()
                    if progress is not None and len(metadata) % 64 == 0:
                        elapsed = max(1.0e-6, time.perf_counter() - started)
                        progress.update(
                            emit=False,
                            CompletedFrames=len(metadata),
                            FramesPerSecond=len(metadata) / elapsed,
                            Message=f"正在分片张量化数据 {len(metadata):,} 帧",
                        )
            flush()
            histories.clear()
            gc.collect()
            return cls(path, owner, shard_paths, locations, metadata)
        except Exception:
            owner.cleanup()
            raise

    def __len__(self) -> int:
        return len(self.locations)

    def __getitem__(self, index: int) -> dict:
        shard_index, local_index = self.locations[index]
        rows = self.cache.pop(shard_index, None)
        if rows is None:
            try:
                rows = torch.load(
                    self.shard_paths[shard_index],
                    map_location="cpu",
                    weights_only=False,
                )
            except TypeError:
                rows = torch.load(
                    self.shard_paths[shard_index], map_location="cpu"
                )
        self.cache[shard_index] = rows
        # Each persistent DataLoader worker owns a dataset copy. Keeping one
        # shard per copy bounds Windows spawn-mode memory while sequential
        # access still reuses the active 512-frame shard.
        while len(self.cache) > 1:
            self.cache.popitem(last=False)
        return rows[local_index]

    def clear_cache(self) -> None:
        self.cache.clear()
        gc.collect()

    def close(self) -> None:
        self.clear_cache()
        if self.root_owner is not None:
            self.root_owner.cleanup()
            self.root_owner = None

    def __getstate__(self) -> dict:
        state = dict(self.__dict__)
        state["cache"] = OrderedDict()
        state["root_owner"] = None
        return state


class ShardedDatasetView(Dataset):
    def __init__(self, source: Dataset, indices: list[int]):
        self.source = source
        self.indices = indices
        metadata = dataset_metadata(source)
        self.rows = [metadata[index] for index in indices]

    def __len__(self) -> int:
        return len(self.indices)

    def __getitem__(self, index: int) -> dict:
        return self.source[self.indices[index]]


def dataset_metadata(rows) -> list[dict]:
    return getattr(rows, "rows", rows)


def base_sharded_dataset(rows) -> ShardedTeacherDataset | None:
    current = rows
    while isinstance(current, ShardedDatasetView):
        current = current.source
    return current if isinstance(current, ShardedTeacherDataset) else None


def dataset_locality_keys(rows) -> list[int] | None:
    """Return the backing shard for every row visible through dataset views."""
    visible_indices = list(range(len(rows)))
    current = rows
    while isinstance(current, ShardedDatasetView):
        visible_indices = [current.indices[index] for index in visible_indices]
        current = current.source
    if not isinstance(current, ShardedTeacherDataset):
        return None
    return [current.locations[index][0] for index in visible_indices]


def subset_for_row_ids(rows, selected_rows: set[int] | None):
    if selected_rows is None:
        return rows
    metadata = dataset_metadata(rows)
    indices = [
        index
        for index, row in enumerate(metadata)
        if int(row.get("I", -1)) in selected_rows
    ]
    if base_sharded_dataset(rows) is not None:
        return ShardedDatasetView(rows, indices)
    return [rows[index] for index in indices]


def audit_dataset(rows) -> dict:
    metadata = dataset_metadata(rows)
    frame_count = len(metadata)
    dense_slots = sum(int(row.get("_dense_feature_slots", 0)) for row in metadata)
    nonzero_values = sum(
        int(row.get("_nonzero_feature_values", 0)) for row in metadata
    )
    sparse_frames = sum(bool(row.get("_dataset_sparse")) for row in metadata)
    object_frames = sum(int(row.get("_object_count", 0)) > 0 for row in metadata)
    empty_object_frames = max(0, frame_count - object_frames)
    transition_frames = sum(int(row.get("M", 0)) > 0 for row in metadata)
    invalid_transition_frames = sum(
        int(row.get("DK", 0)) <= 0 for row in metadata
    )
    strategy_rows = [row for row in metadata if int(row.get("SK", 0)) > 0]
    strategy_label_frames = len(strategy_rows)
    strategy_label_counts = [0, 0, 0, 0, 0]
    strategy_applicable_counts = [0, 0, 0, 0, 0]
    for row in strategy_rows:
        labels = list(row.get("SL", []))
        applicable = list(row.get("SA", []))
        if len(labels) != len(strategy_label_counts) or len(applicable) != 5:
            continue
        for index, value in enumerate(labels):
            is_applicable = float(applicable[index]) >= 0.5
            if is_applicable:
                strategy_applicable_counts[index] += 1
            if is_applicable and float(value) >= 0.5:
                strategy_label_counts[index] += 1
    strategy_negative_counts = [
        max(0, applicable - positive)
        for applicable, positive in zip(
            strategy_applicable_counts, strategy_label_counts
        )
    ]
    strategy_quality_passed = all(
        applicable == 0 or (positive > 0 and negative > 0)
        for applicable, positive, negative in zip(
            strategy_applicable_counts,
            strategy_label_counts,
            strategy_negative_counts,
        )
    )
    terminal_known_frames = sum(int(row.get("TK", 0)) > 0 for row in metadata)
    warnings: list[str] = []
    if frame_count > 0 and object_frames == 0:
        warnings.append(
            "all loaded frames have zero public object tokens; "
            "state tokens remain usable but object-aware supervision is absent"
        )
    elif empty_object_frames > 0:
        warnings.append(
            f"{empty_object_frames} loaded frames have zero public object tokens"
        )
    if frame_count > 0 and object_frames / frame_count < 0.95:
        warnings.append("public object-token coverage is below the 95% quality floor")
    if not strategy_quality_passed:
        warnings.append(
            "one or more applicable strategy heads lack positive or negative supervision"
        )
    if frame_count > 0 and terminal_known_frames / frame_count < 0.95:
        warnings.append("terminal-known coverage is below the 95% quality floor")
    if invalid_transition_frames > 0:
        warnings.append(
            f"{invalid_transition_frames} frames have no valid transition/terminal contract"
        )
    if frame_count > 1 and transition_frames == 0:
        warnings.append("the loaded dataset contains no trainable dynamics transitions")
    if sparse_frames == frame_count and frame_count > 0:
        encoding = "aura.combat-transformer-dataset.sparse-index-value.v3"
    elif sparse_frames > 0:
        encoding = "mixed-sparse-and-legacy-dense"
        warnings.append("the loaded dataset mixes sparse-v2 and legacy dense rows")
    else:
        encoding = "legacy-dense-json"
    return {
        "encoding": encoding,
        "loaded_frames": frame_count,
        "dense_slots": dense_slots,
        "nonzero_values": nonzero_values,
        "density": nonzero_values / max(1, dense_slots),
        "object_frames": object_frames,
        "empty_object_frames": empty_object_frames,
        "object_coverage": object_frames / max(1, frame_count),
        "object_audit_passed": (
            frame_count == 0 or object_frames / max(1, frame_count) >= 0.95
        ),
        "transition_frames": transition_frames,
        "invalid_transition_frames": invalid_transition_frames,
        "strategy_label_frames": strategy_label_frames,
        "strategy_label_counts": strategy_label_counts,
        "strategy_applicable_counts": strategy_applicable_counts,
        "strategy_negative_counts": strategy_negative_counts,
        "strategy_quality_passed": strategy_quality_passed,
        "terminal_known_frames": terminal_known_frames,
        "warnings": warnings,
    }


class LengthBucketBatchSampler(Sampler[list[int]]):
    def __init__(
        self,
        rows: list[dict],
        batch_size: int,
        seed: int,
        locality_keys: list[int] | None = None,
    ):
        self.rows = rows
        self.batch_size = max(1, batch_size)
        self.seed = seed
        self.epoch = 0
        if locality_keys is not None and len(locality_keys) != len(rows):
            raise ValueError("dataset locality keys must match the row count")
        self.locality_keys = locality_keys

    def __len__(self) -> int:
        if self.locality_keys is None:
            samples = sum(
                int(row.get("_sampling_repeats", 1)) for row in self.rows
            )
            return math.ceil(samples / self.batch_size)
        samples_by_locality: dict[int, int] = {}
        for row, locality in zip(self.rows, self.locality_keys):
            samples_by_locality[locality] = (
                samples_by_locality.get(locality, 0)
                + int(row.get("_sampling_repeats", 1))
            )
        return sum(
            math.ceil(samples / self.batch_size)
            for samples in samples_by_locality.values()
        )

    def _batches(
        self,
        indices: list[int],
        randomizer: random.Random,
    ) -> list[list[int]]:
        indices.sort(key=lambda index: self.rows[index]["_bucket_cost"])
        bucket_size = self.batch_size * 8
        buckets = [
            indices[start : start + bucket_size]
            for start in range(0, len(indices), bucket_size)
        ]
        randomizer.shuffle(buckets)
        batches: list[list[int]] = []
        for bucket in buckets:
            randomizer.shuffle(bucket)
            batches.extend(
                bucket[start : start + self.batch_size]
                for start in range(0, len(bucket), self.batch_size)
            )
        return batches

    def __iter__(self):
        randomizer = random.Random(self.seed + self.epoch * 104729)
        self.epoch += 1
        indices = [
            index
            for index, row in enumerate(self.rows)
            for _ in range(int(row.get("_sampling_repeats", 1)))
        ]
        if self.locality_keys is None:
            yield from self._batches(indices, randomizer)
            return

        # A sharded dataset has a one-shard cache per process. Global length
        # bucketing makes adjacent samples jump between files and can reload a
        # 512-frame shard hundreds of times for one batch. Randomize shard
        # order between epochs, but consume each shard contiguously; retain
        # length bucketing and randomization inside that shard.
        locality_groups: dict[int, list[int]] = {}
        for index in indices:
            locality_groups.setdefault(self.locality_keys[index], []).append(index)
        groups = list(locality_groups.values())
        randomizer.shuffle(groups)
        for group in groups:
            yield from self._batches(group, randomizer)


def run_key(row: dict) -> str:
    return str(row.get("Y") or f"episode:{int(row['E'])}")


def stable_partition_score(key: str, seed: int) -> int:
    payload = f"{seed}|{key}".encode("utf-8")
    return int.from_bytes(hashlib.sha256(payload).digest()[:8], "big")


def validation_run_ids(
    rows: list[dict], seed: int, initial_ids: set[str] | None = None
) -> set[str]:
    episodes = sorted({run_key(row) for row in rows})
    if len(episodes) < 2:
        return set(episodes)
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
    required_frames = min(192, max(64, len(rows) // 5))
    frames_by_run: dict[str, int] = {}
    for row in rows:
        key = run_key(row)
        frames_by_run[key] = frames_by_run.get(key, 0) + 1
    selected_frames = sum(frames_by_run.get(key, 0) for key in validation_ids)
    remaining_ids = sorted(
        set(episodes).difference(validation_ids),
        key=lambda key: (stable_partition_score(key, seed), key),
    )
    for key in remaining_ids:
        if selected_frames >= required_frames or len(validation_ids) >= len(episodes) - 1:
            break
        validation_ids.add(key)
        selected_frames += frames_by_run.get(key, 0)
    if len(validation_ids) == len(episodes) and len(episodes) > 1:
        validation_ids.remove(
            max(validation_ids, key=lambda key: (stable_partition_score(key, seed), key))
        )
    return validation_ids


def split_rows(rows: list[dict], seed: int) -> tuple[list[dict], list[dict]]:
    if len({run_key(row) for row in rows}) < 2:
        raise RuntimeError(
            "at least two independent Journey runs are required for "
            "run-isolated training and validation"
        )
    validation_ids = validation_run_ids(rows, seed)
    training = [row for row in rows if run_key(row) not in validation_ids]
    validation = [row for row in rows if run_key(row) in validation_ids]
    if not training or not validation:
        raise RuntimeError(
            "run-isolated training/validation split produced an empty side"
        )
    return training, validation


def write_anchor_rows(path: Path, rows: list[dict]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    with temporary.open("w", encoding="utf-8") as stream:
        for row in rows:
            payload = {key: value for key, value in row.items() if not key.startswith("_")}
            payload["S"] = sparse_feature_payload(row["_state_tensor"])
            payload["A"] = sparse_feature_matrix_payload(row["_action_tensor"])
            payload["O"] = sparse_feature_matrix_payload(row["_object_tensor"])
            payload["P"] = row["_policy_tensor"].float().tolist()
            next_state = row["_next_state_tensor"]
            if next_state.numel() == 0:
                next_state = torch.zeros_like(row["_state_tensor"])
            payload["N"] = sparse_feature_payload(next_state)
            stream.write(json.dumps(payload, separators=(",", ":"), ensure_ascii=True))
            stream.write("\n")
    temporary.replace(path)


def training_and_anchor_rows(
    rows, args: argparse.Namespace
):
    if args.fixed_anchor and args.anchor and Path(args.anchor).exists():
        validation = load_rows(Path(args.anchor), args.history)
        tensorize_rows(validation)
        anchor_keys = {run_key(row) for row in validation}
        if base_sharded_dataset(rows) is not None:
            metadata = dataset_metadata(rows)
            training_indices = [
                index
                for index, row in enumerate(metadata)
                if run_key(row) not in anchor_keys
            ]
            if training_indices and validation:
                return ShardedDatasetView(rows, training_indices), validation, False
            raise RuntimeError(
                "incremental selection contains no run outside the fixed anchor"
            )
        else:
            training = [row for row in rows if run_key(row) not in anchor_keys]
            if training and validation:
                return training, validation, False
            raise RuntimeError(
                "incremental selection contains no run outside the fixed anchor"
            )
    if base_sharded_dataset(rows) is not None:
        return sharded_training_and_anchor_rows(rows, args)
    if not args.fixed_anchor or not args.anchor:
        training, validation = split_rows(rows, args.seed)
        return training, validation, False
    anchor_path = Path(args.anchor)
    training, validation = split_rows(rows, args.seed)
    write_anchor_rows(anchor_path, validation)
    return training, validation, True


def sharded_training_and_anchor_rows(
    rows, args: argparse.Namespace
) -> tuple[ShardedDatasetView, ShardedDatasetView, bool]:
    metadata = dataset_metadata(rows)
    anchor_created = False
    initial_ids: set[str] = set()
    anchor_path = Path(args.anchor) if args.anchor else None
    if args.fixed_anchor and anchor_path is not None and anchor_path.exists():
        with anchor_path.open("r", encoding="utf-8") as stream:
            for line in stream:
                if not line.strip():
                    continue
                payload = json.loads(line)
                initial_ids.add(run_key(payload))
    validation_ids = validation_run_ids(metadata, args.seed, initial_ids)
    training_indices = [
        index
        for index, row in enumerate(metadata)
        if run_key(row) not in validation_ids
    ]
    validation_indices = [
        index
        for index, row in enumerate(metadata)
        if run_key(row) in validation_ids
    ]
    if not training_indices or not validation_indices:
        raise RuntimeError(
            "at least two independent Journey runs are required for "
            "run-isolated sharded training and validation"
        )
    if args.fixed_anchor and anchor_path is not None:
        if not initial_ids:
            anchor_created = True
        if anchor_created or validation_ids.difference(initial_ids):
            base = base_sharded_dataset(rows)
            if base is None:
                raise RuntimeError("sharded anchor source is unavailable")
            write_anchor_from_source(
                base.source_path, anchor_path, validation_ids
            )
    return (
        ShardedDatasetView(rows, training_indices),
        ShardedDatasetView(rows, validation_indices),
        anchor_created,
    )


def write_anchor_from_source(
    source_path: Path, anchor_path: Path, validation_ids: set[str]
) -> None:
    anchor_path.parent.mkdir(parents=True, exist_ok=True)
    temporary = anchor_path.with_suffix(anchor_path.suffix + ".tmp")
    with source_path.open("r", encoding="utf-8") as source, temporary.open(
        "w", encoding="utf-8", newline="\n"
    ) as destination:
        for line in source:
            if not line.strip():
                continue
            payload = json.loads(line)
            if run_key(payload) in validation_ids:
                destination.write(line.rstrip("\r\n"))
                destination.write("\n")
    temporary.replace(anchor_path)


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
    phases = torch.full((batch,), -1, dtype=torch.long)
    strategy_labels = torch.zeros(batch, 5)
    strategy_mask = torch.zeros(batch, 5, dtype=torch.bool)
    executed_actions = torch.zeros(batch, dtype=torch.long)
    next_states = torch.zeros(batch, state_dimensions)
    transition_mask = torch.zeros(batch, dtype=torch.bool)
    outcomes = torch.zeros(batch, 4)
    terminals = torch.zeros(batch)
    terminal_mask = torch.zeros(batch, dtype=torch.bool)
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
        phases[owner] = int(row.get("G", -1))
        labels = list(row.get("SL", []))
        applicable = list(row.get("SA", []))
        if (
            int(row.get("SK", 0)) > 0
            and len(labels) == 5
            and len(applicable) == 5
        ):
            strategy_labels[owner] = torch.as_tensor(labels, dtype=torch.float32)
            strategy_mask[owner] = torch.as_tensor(applicable, dtype=torch.bool)
        executed_actions[owner] = max(0, min(action_count - 1, int(row["X"])))
        if (
            int(row.get("M", 0)) > 0
            and row["_next_state_tensor"].numel() == state_dimensions
        ):
            next_states[owner] = row["_next_state_tensor"]
            transition_mask[owner] = True
        outcomes[owner] = row["_outcome_tensor"]
        terminals[owner] = 1.0 if int(row.get("Z", 0)) > 0 else 0.0
        terminal_mask[owner] = int(row.get("TK", 0)) > 0
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
        "phases": phases,
        "strategy_labels": strategy_labels,
        "strategy_mask": strategy_mask,
        "executed_actions": executed_actions,
        "next_states": next_states,
        "transition_mask": transition_mask,
        "outcomes": outcomes,
        "terminals": terminals,
        "terminal_mask": terminal_mask,
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
        self.phase_head = nn.Linear(hidden, 5)
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
        phase = self.phase_head(cls_encoded)
        strategy = self.strategy_head(cls_encoded)
        executed = batch["executed_actions"].clamp(0, max(0, action_count - 1))
        executed_encoded = action_encoded[
            torch.arange(owners, device=states.device), executed
        ]
        transition_context = torch.cat((cls_encoded, executed_encoded), dim=-1)
        next_state = self.dynamics_head(transition_context)
        outcome = self.outcome_head(cls_encoded)
        terminal = self.terminal_head(transition_context).squeeze(-1)
        return policy, value, phase, strategy, next_state, outcome, terminal


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


def capacity_bucket(value: int) -> int:
    value = max(1, int(value))
    bucket = 1
    while bucket < value:
        bucket <<= 1
    return bucket


def runtime_cache_key(
    args: argparse.Namespace,
    device: torch.device,
    sample_raw: dict[str, torch.Tensor],
) -> str:
    device_name = (
        torch.cuda.get_device_name(device)
        if device.type == "cuda"
        else platform.processor() or platform.machine()
    )
    payload = "|".join(
        str(value)
        for value in (
            "transformer-runtime-auto-tune-v6-capacity-envelope",
            platform.system(),
            platform.machine(),
            platform.processor(),
            os.cpu_count(),
            torch.__version__,
            device.type,
            device_name,
            int(sample_raw["states"].shape[1]),
            capacity_bucket(int(sample_raw["actions"].shape[1])),
            int(sample_raw["actions"].shape[2]),
            capacity_bucket(int(sample_raw["object_tokens"].shape[1])),
            capacity_bucket(int(sample_raw["history_states"].shape[1])),
            args.hidden,
            args.layers,
            args.heads,
            args.ffn,
            args.history,
            args.batch_size,
            args.micro_batch_size,
            args.loader_workers,
            args.prefetch_batches,
            args.dataset_storage,
            args.dataset_shard_frames,
            args.resident_dataset_maximum_frames,
            int(args.mixed_precision),
            int(args.deterministic),
            int(args.pin_memory),
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
        "effective_batch_size": int(plan["effective_batch_size"]),
        "loader_workers": int(plan["loader_workers"]),
        "prefetch_batches": int(plan["prefetch_batches"]),
        "pinned_memory": bool(plan["pinned_memory"]),
        "precision": str(plan["precision"]),
        "measured_utc": time.time(),
    }
    payload["protocol"] = "transformer-runtime-auto-tune-v6-capacity-envelope"
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
    was_training = model.training
    with torch.random.fork_rng(devices=[]):
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
    model.train(was_training)
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
    was_training = model.training
    fork_devices = [
        device.index if device.index is not None else torch.cuda.current_device()
    ]
    with torch.random.fork_rng(devices=fork_devices):
        model.train()
        while candidate >= 1:
            optimizer = None
            sample = None
            total = None
            try:
                sample = move(slice_batch(raw, 0, candidate), device)
                optimizer = torch.optim.AdamW(
                    model.parameters(), lr=0.0, weight_decay=0.0
                )
                optimizer.zero_grad(set_to_none=True)
                with precision_context(device, precision):
                    total, _ = loss_for(model, sample)
                total.backward()
                # Adam moments are lazily allocated at the first step; include
                # them in the probe before choosing a conservative batch.
                optimizer.step()
                optimizer.zero_grad(set_to_none=True)
                torch.cuda.synchronize(device)
                model.train(was_training)
                return max(1, candidate // 2)
            except (torch.cuda.OutOfMemoryError, RuntimeError) as error:
                if not is_cuda_out_of_memory(error):
                    raise
                model.zero_grad(set_to_none=True)
                del optimizer, sample, total
                gc.collect()
                torch.cuda.empty_cache()
                candidate //= 2
    model.train(was_training)
    raise CudaTrainingOutOfMemory(
        "Transformer teacher cannot fit a single worst-shape frame in GPU memory"
    )


def resolve_runtime_plan(
    model: StrategyTransformer,
    sample_raw: dict[str, torch.Tensor],
    args: argparse.Namespace,
    device: torch.device,
    runtime: dict,
    allow_training_probe: bool = True,
) -> dict:
    started = time.perf_counter()
    key = runtime_cache_key(args, device, sample_raw)
    cached = load_runtime_cache(args.runtime_cache, key)
    precision = "float32"
    if device.type == "cuda" and args.mixed_precision:
        precision = "bfloat16" if torch.cuda.is_bf16_supported() else "float16"
    plan = {
        **runtime,
        "micro_batch_size": args.micro_batch_size or args.batch_size,
        "effective_batch_size": args.batch_size,
        "loader_workers": (
            0
            if args.dataset_storage == "resident" or args.loader_workers < 0
            else args.loader_workers
            if args.loader_workers > 0
            else (
                min(2, max(1, (os.cpu_count() or 1) // 4))
                if device.type == "cuda"
                and args.dataset_storage == "sharded"
                else 0
            )
        ),
        "prefetch_batches": max(1, min(8, args.prefetch_batches)),
        "pinned_memory": bool(args.pin_memory and device.type == "cuda"),
        "precision": precision,
        "auto_tuned": False,
        "cache_hit": cached is not None,
        "runtime_cache_key": key,
        "calibration_kind": "cache-hit" if cached is not None else "default",
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
            plan["effective_batch_size"] = int(
                cached.get("effective_batch_size", args.batch_size)
            )
            used_cache = True
        if args.loader_workers == 0:
            plan["loader_workers"] = int(
                cached.get("loader_workers", plan["loader_workers"])
            )
            used_cache = True
        plan["cache_hit"] = used_cache
        plan["auto_tuned"] = used_cache
        plan["calibration_kind"] = (
            "cache-hit" if used_cache else "explicit-options"
        )
    elif not args.self_test and allow_training_probe:
        if device.type == "cpu" and args.cpu_threads <= 0:
            plan["cpu_threads"] = benchmark_cpu_threads(model, sample_raw, runtime)
            plan["auto_tuned"] = True
        plan["calibration_kind"] = "training-probe"
    elif cached is None:
        plan["calibration_kind"] = "annotation-default"
        if device.type == "cuda" and args.micro_batch_size <= 0:
            plan["micro_batch_size"] = choose_gpu_micro_batch(
                model,
                sample_raw,
                device,
                precision,
                min(512, int(sample_raw["states"].shape[0])),
            )
            plan["auto_tuned"] = True
    plan["micro_batch_size"] = max(
        1, min(512, int(plan["micro_batch_size"]))
    )
    if device.type == "cuda" and args.micro_batch_size <= 0:
        plan["effective_batch_size"] = max(
            args.batch_size,
            min(512, max(
                int(plan["effective_batch_size"]),
                int(plan["micro_batch_size"]) * 2,
            )),
        )
    else:
        plan["effective_batch_size"] = args.batch_size
    if args.dataset_storage == "resident":
        # Windows DataLoader workers spawn and duplicate a resident tensor
        # corpus. Keep resident batching in the training process even when an
        # older UI setting or runtime-cache entry requested workers.
        plan["loader_workers"] = 0
    plan["prefetch_batches"] = (
        max(1, int(plan["prefetch_batches"]))
        if int(plan["loader_workers"]) > 0
        else 0
    )
    plan["calibration_seconds"] = time.perf_counter() - started
    if cached is None and not args.self_test and allow_training_probe:
        save_runtime_cache(args.runtime_cache, key, plan)
    return plan


def evaluation_and_anchor_rows(rows, args: argparse.Namespace):
    """Choose evaluation rows without creating a train/validation leakage path."""
    if args.fixed_anchor and args.anchor and Path(args.anchor).exists():
        validation = load_rows(Path(args.anchor), args.history)
        tensorize_rows(validation)
        if not validation:
            raise RuntimeError("fixed anchor contains no usable frames")
        return rows, validation, False

    metadata = dataset_metadata(rows)
    run_ids = {run_key(row) for row in metadata}
    anchor_created = False
    if len(run_ids) < 2:
        # Reusing an existing model does not train on these rows, so evaluating
        # the sole run is safe. Do not freeze it as the future anchor.
        return rows, rows, False
    validation_ids = validation_run_ids(metadata, args.seed)
    indices = [
        index
        for index, row in enumerate(metadata)
        if run_key(row) in validation_ids
    ]
    validation = (
        ShardedDatasetView(rows, indices)
        if base_sharded_dataset(rows) is not None
        else [rows[index] for index in indices]
    )
    if args.fixed_anchor and args.anchor:
        anchor_path = Path(args.anchor)
        if base_sharded_dataset(rows) is not None:
            base = base_sharded_dataset(rows)
            if base is None:
                raise RuntimeError("sharded anchor source is unavailable")
            write_anchor_from_source(base.source_path, anchor_path, validation_ids)
        else:
            write_anchor_rows(anchor_path, validation)
        anchor_created = True
    return rows, validation, anchor_created


def calibration_batch(rows, requested_size: int) -> dict[str, torch.Tensor]:
    """Build a bounded batch that contains the dataset's shape envelope."""
    metadata = dataset_metadata(rows)
    if not metadata:
        raise RuntimeError("Transformer calibration dataset is empty")
    maximum = min(len(metadata), max(1, requested_size))
    def action_count(row: dict) -> int:
        return int(
            row["_action_count"]
            if "_action_count" in row
            else row["_action_tensor"].shape[0]
        )

    def object_count(row: dict) -> int:
        return int(
            row["_object_count"]
            if "_object_count" in row
            else row["_object_tensor"].shape[0]
        )

    def history_count(row: dict) -> int:
        return int(
            row["_history_count"]
            if "_history_count" in row
            else row["_history_state_tensor"].shape[0]
        )

    shape_functions = (
        action_count,
        object_count,
        history_count,
        lambda row: int(row.get("_bucket_cost", 0)),
    )
    indices: list[int] = []
    for shape in shape_functions:
        index = max(range(len(metadata)), key=lambda item: (shape(metadata[item]), -item))
        if index not in indices:
            indices.append(index)
    for index in sorted(
        range(len(metadata)),
        key=lambda item: (
            -int(metadata[item].get("_bucket_cost", 0)),
            item,
        ),
    ):
        if len(indices) >= maximum:
            break
        if index not in indices:
            indices.append(index)
    return collate([rows[index] for index in indices[:maximum]])


def lower_cuda_micro_batch(
    plan: dict,
    args: argparse.Namespace,
    device: torch.device,
    phase: str,
) -> int:
    current = max(1, int(plan["micro_batch_size"]))
    if device.type != "cuda" or current <= 1:
        raise CudaTrainingOutOfMemory(
            f"CUDA ran out of memory during {phase} at micro-batch {current}"
        )
    reduced = max(1, current // 2)
    plan["micro_batch_size"] = reduced
    plan["effective_batch_size"] = max(
        reduced, min(512, int(plan["effective_batch_size"]))
    )
    plan["auto_tuned"] = True
    save_runtime_cache(
        args.runtime_cache,
        str(plan.get("runtime_cache_key", "")),
        plan,
    )
    gc.collect()
    torch.cuda.empty_cache()
    return reduced


def loss_for(
    model: StrategyTransformer,
    batch: dict[str, torch.Tensor],
    effective_counts: dict[str, int] | None = None,
) -> tuple[torch.Tensor, dict[str, float]]:
    (
        policy_logits,
        values,
        phase_logits,
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
    valid_phase = batch["phases"] >= 0
    if valid_phase.any():
        phase_loss = torch.nn.functional.cross_entropy(
            phase_logits[valid_phase], batch["phases"][valid_phase]
        )
    else:
        phase_loss = policy_loss.new_zeros(())
    valid_strategy = batch["strategy_mask"]
    if valid_strategy.any():
        strategy_losses = torch.nn.functional.binary_cross_entropy_with_logits(
            strategy_logits,
            batch["strategy_labels"],
            reduction="none",
        )
        strategy_loss = strategy_losses[valid_strategy].mean()
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
    terminal_mask = batch["terminal_mask"]
    terminal_loss = (
        torch.nn.functional.binary_cross_entropy_with_logits(
            terminal_logits[terminal_mask], batch["terminals"][terminal_mask]
        )
        if terminal_mask.any()
        else policy_loss.new_zeros(())
    )
    if effective_counts is None:
        policy_weight = 1.0
        sample_weight = 1.0
        strategy_weight = 1.0
        phase_weight = 1.0
        dynamics_weight = 1.0
        terminal_weight = 1.0
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
        phase_weight = int(valid_phase.sum()) / max(
            1, effective_counts["phases"]
        )
        dynamics_weight = int(transition_mask.sum()) / max(
            1, effective_counts["transitions"]
        )
        terminal_weight = int(terminal_mask.sum()) / max(
            1, effective_counts["terminals"]
        )
    total = (
        policy_loss * policy_weight
        + value_loss * 0.35 * sample_weight
        + phase_loss * 0.08 * phase_weight
        + strategy_loss * 0.12 * strategy_weight
        + dynamics_loss * 0.15 * dynamics_weight
        + outcome_loss * 0.20 * sample_weight
        + terminal_loss * 0.10 * terminal_weight
    )
    return total, {
        "policy": float(policy_loss.detach()),
        "value": float(value_loss.detach()),
        "phase": float(phase_loss.detach()),
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
        "phases": int((raw["phases"] >= 0).sum()),
        "strategies": int(raw["strategy_mask"].sum()),
        "transitions": int(raw["transition_mask"].sum()),
        "terminals": int(raw["terminal_mask"].sum()),
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
    phase_count = 0
    phase_correct = 0
    strategy_count = 0
    strategy_correct = 0
    dynamics_squared_error = 0.0
    dynamics_elements = 0
    outcome_absolute_error = 0.0
    outcome_elements = 0
    death_squared_error = 0.0
    terminal_count = 0
    terminal_correct = 0
    for raw in loader:
        batch = move(raw, device)
        with precision_context(device, precision):
            (
                policy_logits,
                values,
                phase_logits,
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
        valid_phase = batch["phases"] >= 0
        phase_count += int(valid_phase.sum())
        if valid_phase.any():
            phase_correct += int(
                (
                    phase_logits[valid_phase].argmax(dim=-1)
                    == batch["phases"][valid_phase]
                ).sum()
            )
        valid_strategy = batch["strategy_mask"]
        strategy_count += int(valid_strategy.sum())
        if valid_strategy.any():
            strategy_correct += int(
                (
                    (strategy_logits >= 0.0)[valid_strategy]
                    == (batch["strategy_labels"] >= 0.5)[valid_strategy]
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
        terminal_mask = batch["terminal_mask"]
        terminal_count += int(terminal_mask.sum())
        if terminal_mask.any():
            terminal_correct += int(
                (
                    (terminal_logits[terminal_mask] >= 0.0)
                    == (batch["terminals"][terminal_mask] >= 0.5)
                ).sum()
            )
        count += size
    return {
        "policy_ce": policy_cross_entropy / max(1, policy_count),
        "uniform_policy_ce": uniform_policy_cross_entropy / max(1, policy_count),
        "policy_accuracy": policy_correct / max(1, policy_count),
        "value_mae": value_error / max(1, count),
        "phase_accuracy": phase_correct / max(1, phase_count),
        "strategy_accuracy": strategy_correct / max(1, strategy_count),
        "dynamics_mse": dynamics_squared_error / max(1, dynamics_elements),
        "dynamics_frames": sum(
            int(row.get("M", 0)) > 0 for row in loader.dataset.rows
        ),
        "outcome_mae": outcome_absolute_error / max(1, outcome_elements),
        "death_brier": death_squared_error / max(1, count),
        "terminal_accuracy": terminal_correct / max(1, terminal_count),
    }


def evaluate_with_cuda_backoff(
    model: StrategyTransformer,
    validation_rows,
    args: argparse.Namespace,
    device: torch.device,
    plan: dict,
    loader: DataLoader | None = None,
) -> tuple[dict[str, float], DataLoader]:
    while True:
        expected_batch = max(1, int(plan["micro_batch_size"]))
        if loader is None or int(loader.batch_size or 0) != expected_batch:
            if loader is not None:
                del loader
                gc.collect()
            loader = DataLoader(
                validation_rows
                if isinstance(validation_rows, Dataset)
                else TeacherDataset(validation_rows),
                batch_size=expected_batch,
                shuffle=False,
                collate_fn=collate,
                **loader_options(
                    {**plan, "loader_workers": 0, "prefetch_batches": 0},
                    device,
                ),
            )
        try:
            return evaluate(model, loader, device, plan["precision"]), loader
        except (torch.cuda.OutOfMemoryError, RuntimeError) as error:
            if not is_cuda_out_of_memory(error):
                raise
            model.zero_grad(set_to_none=True)
            loader = None
            lower_cuda_micro_batch(plan, args, device, "validation")


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
    state_dict = dict(checkpoint["state_dict"])
    # v2 used the five-way "strategy" head as a mutually-exclusive journey
    # phase classifier. Preserve that learned representation in the new phase
    # head while allowing the independent multi-label strategy head to start
    # from the same conservative prior.
    if (
        "phase_head.weight" not in state_dict
        and "strategy_head.weight" in state_dict
    ):
        state_dict["phase_head.weight"] = state_dict["strategy_head.weight"].clone()
        state_dict["phase_head.bias"] = state_dict["strategy_head.bias"].clone()
    model.load_state_dict(state_dict, strict=True)
    teacher_generation = max(
        0, int(checkpoint.get("teacher_generation", 0))
    )
    # Compatible legacy/external weights may initialize a training run, but
    # they are not a stable warm teacher until an accepted generation proves
    # that they passed this pipeline's quality gate.
    return teacher_generation > 0, teacher_generation


def initialize_optimizer_state(
    optimizer: torch.optim.Optimizer,
    model: nn.Module,
    device: torch.device,
) -> None:
    """Allocate lazy Adam state before batches can partially mutate weights."""
    learning_rates = [float(group["lr"]) for group in optimizer.param_groups]
    try:
        for group in optimizer.param_groups:
            group["lr"] = 0.0
        for parameter in model.parameters():
            if parameter.requires_grad:
                parameter.grad = torch.zeros_like(parameter)
        optimizer.step()
        for state in optimizer.state.values():
            step = state.get("step")
            if isinstance(step, torch.Tensor):
                step.zero_()
            elif step is not None:
                state["step"] = 0
        optimizer.zero_grad(set_to_none=True)
        if device.type == "cuda":
            torch.cuda.synchronize(device)
    except (torch.cuda.OutOfMemoryError, RuntimeError) as error:
        optimizer.zero_grad(set_to_none=True)
        if is_cuda_out_of_memory(error):
            gc.collect()
            if device.type == "cuda":
                torch.cuda.empty_cache()
            raise CudaTrainingOutOfMemory(
                "CUDA cannot allocate the Transformer optimizer state"
            ) from error
        raise
    finally:
        for group, learning_rate in zip(optimizer.param_groups, learning_rates):
            group["lr"] = learning_rate


def composite_score(metrics: dict[str, float]) -> float:
    return (
        metrics["policy_ce"]
        + metrics["value_mae"] * 0.35
        + metrics["dynamics_mse"] * 0.15
        + metrics["outcome_mae"] * 0.20
        + metrics["death_brier"] * 0.20
    )


def prior_evaluation_snapshot(args: argparse.Namespace) -> dict | None:
    if args.training_enabled or not args.prior_report:
        return None
    try:
        payload = json.loads(Path(args.prior_report).read_text(encoding="utf-8"))
        metrics = {
            "policy_ce": float(payload["ValidationPolicyCrossEntropy"]),
            "uniform_policy_ce": float(
                payload["ValidationUniformPolicyCrossEntropy"]
            ),
            "policy_accuracy": float(payload["ValidationPolicyTop1Accuracy"]),
            "value_mae": float(payload["ValidationValueMae"]),
            "phase_accuracy": float(payload["ValidationPhaseAccuracy"]),
            "strategy_accuracy": float(payload["ValidationStrategyAccuracy"]),
            "dynamics_mse": float(payload["ValidationDynamicsMse"]),
            "dynamics_frames": int(payload.get("DynamicsValidationFrames", 0)),
            "outcome_mae": float(payload["ValidationOutcomeMae"]),
            "death_brier": float(payload["ValidationDeathBrier"]),
            "terminal_accuracy": float(payload["ValidationTerminalAccuracy"]),
        }
        if not all(math.isfinite(float(value)) for value in metrics.values()):
            return None
        return {
            "metrics": metrics,
            "validation_frames": int(payload.get("ValidationFrames", 0)),
            "anchor_frames": int(payload.get("AnchorValidationFrames", 0)),
            "anchor_created": bool(payload.get("AnchorCreated", False)),
            "teacher_generation": int(payload.get("TeacherGeneration", 0)),
        }
    except (OSError, ValueError, TypeError, KeyError):
        return None


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
    rows,
    args: argparse.Namespace,
    device: torch.device,
    runtime: dict,
    progress: ProgressReporter,
    calibration_rows=None,
) -> tuple[StrategyTransformer, dict[str, float], int, int, int, dict, float, bool, dict, dict]:
    prior_evaluation = prior_evaluation_snapshot(args)
    if args.training_enabled:
        training_rows, validation_rows, anchor_created = training_and_anchor_rows(
            rows, args
        )
    elif prior_evaluation is not None:
        # AnchorCreated means created by this invocation, not merely present
        # in the reused report.
        training_rows, validation_rows, anchor_created = rows, [], False
    else:
        training_rows, validation_rows, anchor_created = evaluation_and_anchor_rows(
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
    training_metadata = getattr(training_rows, "rows", training_rows)
    validation_metadata = getattr(validation_rows, "rows", validation_rows)
    calibration_source = calibration_rows if calibration_rows is not None else rows
    calibration_batch_size = min(
        len(calibration_source),
        max(args.batch_size, 512 if device.type == "cuda" else args.batch_size),
    )
    sample_raw = calibration_batch(calibration_source, calibration_batch_size)
    plan = resolve_runtime_plan(
        model,
        sample_raw,
        args,
        device,
        runtime,
        allow_training_probe=bool(args.training_enabled),
    )
    # Calibration may run dropout-bearing train-mode probes and may take a
    # different path on cache hits. Reset all RNGs so host-local tuning cannot
    # alter model initialization-to-training reproducibility.
    reseed(args.seed)
    worker_options = loader_options(plan, device)
    base_dataset = base_sharded_dataset(rows)
    if base_dataset is not None:
        base_dataset.clear_cache()
    validation_loader = None
    evaluation_started = time.perf_counter()
    if prior_evaluation is not None:
        progress.update(
            Stage="evaluating",
            TotalEpochs=0,
            TotalFrames=int(prior_evaluation["validation_frames"]),
            Message="正在复用稳定教师的固定锚点评估",
        )
        best_metrics = dict(prior_evaluation["metrics"])
        plan["evaluation_reused"] = True
    else:
        progress.update(
            Stage="evaluating",
            TotalEpochs=max(0, args.epochs) if args.training_enabled else 0,
            TotalFrames=len(validation_metadata),
            Message=(
                "正在评估固定锚点基线"
                if args.training_enabled
                else "正在评估复用的 Transformer 教师"
            ),
        )
        best_metrics, validation_loader = evaluate_with_cuda_backoff(
            model,
            validation_rows,
            args,
            device,
            plan,
            validation_loader,
        )
        plan["evaluation_reused"] = False
    baseline_metrics = dict(best_metrics)
    evaluation_seconds = time.perf_counter() - evaluation_started
    best_loss = composite_score(best_metrics)
    if not args.training_enabled:
        return (
            model,
            best_metrics,
            0,
            0,
            int(prior_evaluation["validation_frames"])
            if prior_evaluation is not None
            else len(validation_metadata),
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
                "teacher_generation": (
                    int(prior_evaluation["teacher_generation"])
                    if prior_evaluation is not None
                    else prior_generation
                ),
                "anchor_frames": (
                    int(prior_evaluation["anchor_frames"])
                    if prior_evaluation is not None
                    else len(validation_metadata)
                ),
                "anchor_created": anchor_created,
            },
        )
    training_dataset = (
        training_rows
        if isinstance(training_rows, Dataset)
        else TeacherDataset(training_rows)
    )
    training_loader = DataLoader(
        training_dataset,
        batch_sampler=LengthBucketBatchSampler(
            training_metadata,
            plan["effective_batch_size"],
            args.seed,
            dataset_locality_keys(training_rows),
        ),
        collate_fn=collate,
        **worker_options,
    )
    optimizer = torch.optim.AdamW(
        model.parameters(), lr=3.0e-4, weight_decay=1.0e-3
    )
    initialize_optimizer_state(optimizer, model, device)
    use_scaler = device.type == "cuda" and plan["precision"] == "float16"
    try:
        scaler = torch.amp.GradScaler("cuda", enabled=use_scaler)
    except (AttributeError, TypeError):
        scaler = torch.cuda.amp.GradScaler(enabled=use_scaler)
    best_state = copy.deepcopy(model.state_dict())
    update_accepted = False
    head_gate_passed = True
    stale = 0
    executed = 0
    processed_frames = 0
    training_started = time.perf_counter()
    total_frame_work = sum(
        int(row.get("_sampling_repeats", 1)) for row in training_metadata
    ) * max(0, args.epochs)
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
            batch_count = raw["states"].shape[0]
            effective_counts = {
                "samples": batch_count,
                "policies": int(raw["policy_supervision_mask"].sum()),
                "phases": int((raw["phases"] >= 0).sum()),
                "strategies": int(raw["strategy_mask"].sum()),
                "transitions": int(raw["transition_mask"].sum()),
                "terminals": int(raw["terminal_mask"].sum()),
            }
            python_rng = random.getstate()
            torch_rng = torch.random.get_rng_state()
            cuda_rng = (
                torch.cuda.get_rng_state_all() if device.type == "cuda" else None
            )
            while True:
                optimizer.zero_grad(set_to_none=True)
                micro_batch = max(1, int(plan["micro_batch_size"]))
                try:
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
                    break
                except (torch.cuda.OutOfMemoryError, RuntimeError) as error:
                    if not is_cuda_out_of_memory(error):
                        raise
                    optimizer.zero_grad(set_to_none=True)
                    batch = None
                    total = None
                    random.setstate(python_rng)
                    torch.random.set_rng_state(torch_rng)
                    if cuda_rng is not None:
                        torch.cuda.set_rng_state_all(cuda_rng)
                    lower_cuda_micro_batch(plan, args, device, "training")
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
        metrics, validation_loader = evaluate_with_cuda_backoff(
            model,
            validation_rows,
            args,
            device,
            plan,
            validation_loader,
        )
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
        len(training_metadata),
        len(validation_metadata),
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
            "anchor_frames": len(validation_metadata),
            "anchor_created": anchor_created,
        },
    )


@torch.no_grad()
def annotate_once(
    model: StrategyTransformer,
    rows,
    plan: dict,
    device: torch.device,
    path: Path,
    progress: ProgressReporter,
) -> tuple[float, float]:
    loader = DataLoader(
        rows if isinstance(rows, Dataset) else TeacherDataset(rows),
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
                logits, _, _, _, _, _, _ = model(batch)
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


def annotation_model_key(args: argparse.Namespace) -> str:
    if args.training_enabled or not args.resume_model:
        return ""
    source = Path(args.resume_model)
    if not source.exists():
        return ""
    digest = hashlib.sha256()
    with source.open("rb") as stream:
        while True:
            block = stream.read(1024 * 1024)
            if not block:
                break
            digest.update(block)
    return digest.hexdigest()


def read_annotation_cache(
    path: str,
    model_key: str,
    expected_counts: dict[str, int],
) -> dict[str, list[float]]:
    if not path or not model_key or not expected_counts or not Path(path).exists():
        return {}
    cached: dict[str, list[float]] = {}
    try:
        with sqlite3.connect(path, timeout=10.0) as connection:
            for identity, expected_count in expected_counts.items():
                row = connection.execute(
                    "SELECT probabilities_json FROM annotations "
                    "WHERE model_key=? AND frame_identity=?",
                    (model_key, identity),
                ).fetchone()
                if row is not None:
                    values = json.loads(str(row[0]))
                    if isinstance(values, list) and len(values) == expected_count:
                        cached[identity] = [float(value) for value in values]
    except (OSError, sqlite3.Error, ValueError, TypeError):
        return {}
    return cached


def write_annotation_cache(
    path: str,
    model_key: str,
    identities_by_row: dict[int, str],
    annotations_path: Path,
) -> None:
    if not path or not model_key or not identities_by_row:
        return
    values: list[tuple[str, str, str, float]] = []
    with annotations_path.open("r", encoding="utf-8") as stream:
        for line in stream:
            payload = json.loads(line)
            identity = identities_by_row.get(int(payload["I"]), "")
            if identity:
                values.append(
                    (
                        model_key,
                        identity,
                        json.dumps(payload["P"], separators=(",", ":")),
                        time.time(),
                    )
                )
    if not values:
        return
    destination = Path(path)
    destination.parent.mkdir(parents=True, exist_ok=True)
    with sqlite3.connect(destination, timeout=10.0) as connection:
        connection.execute(
            "CREATE TABLE IF NOT EXISTS annotations("
            "model_key TEXT NOT NULL, frame_identity TEXT NOT NULL, "
            "probabilities_json TEXT NOT NULL, updated_utc REAL NOT NULL, "
            "PRIMARY KEY(model_key, frame_identity))"
        )
        connection.executemany(
            "INSERT INTO annotations VALUES(?,?,?,?) "
            "ON CONFLICT(model_key, frame_identity) DO UPDATE SET "
            "probabilities_json=excluded.probabilities_json, "
            "updated_utc=excluded.updated_utc",
            values,
        )
        connection.commit()


def annotate(
    model: StrategyTransformer,
    rows,
    plan: dict,
    args: argparse.Namespace,
    device: torch.device,
    path: Path,
    progress: ProgressReporter,
) -> tuple[float, float]:
    temporary = path.with_suffix(path.suffix + ".tmp")
    plan["annotation_cache_hits"] = 0
    plan["annotation_cache_misses"] = len(rows)
    identities_by_row = read_row_identities(args.annotation_selection)
    metadata = dataset_metadata(rows)
    ordered = []
    for row in metadata:
        row_id = int(row.get("I", -1))
        action_count = int(
            row.get(
                "_action_count",
                row["_action_tensor"].shape[0]
                if "_action_tensor" in row
                else 0,
            )
        )
        ordered.append(
            (row_id, identities_by_row.get(row_id, ""), action_count)
        )
    model_key = annotation_model_key(args)
    cache = read_annotation_cache(
        args.annotation_cache,
        model_key,
        {
            identity: action_count
            for _, identity, action_count in ordered
            if identity and action_count > 0
        },
    )
    if ordered and all(
        identity and identity in cache
        for _, identity, _ in ordered
    ):
        started = time.perf_counter()
        progress.update(
            Stage="annotating",
            CompletedFrames=0,
            TotalFrames=len(rows),
            Message="正在复用 Transformer 蒸馏标注缓存",
        )
        with temporary.open("w", encoding="utf-8", newline="\n") as stream:
            for row_id, identity, _ in ordered:
                stream.write(
                    json.dumps(
                        {"I": row_id, "P": cache[identity]},
                        separators=(",", ":"),
                    )
                    + "\n"
                )
        temporary.replace(path)
        elapsed = max(1.0e-6, time.perf_counter() - started)
        plan["annotation_cache_hits"] = len(ordered)
        plan["annotation_cache_misses"] = 0
        return elapsed, len(ordered) / elapsed
    while True:
        try:
            result = annotate_once(
                model, rows, plan, device, temporary, progress
            )
            temporary.replace(path)
            try:
                write_annotation_cache(
                    args.annotation_cache,
                    model_key,
                    identities_by_row,
                    path,
                )
            except (OSError, sqlite3.Error, ValueError, TypeError):
                pass
            return result
        except (torch.cuda.OutOfMemoryError, RuntimeError) as error:
            try:
                temporary.unlink(missing_ok=True)
            except OSError:
                pass
            if not is_cuda_out_of_memory(error):
                raise
            model.zero_grad(set_to_none=True)
            lower_cuda_micro_batch(plan, args, device, "annotation")


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
        verify_reseed_compatibility()
        reseed(args.seed)
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
        representative_state = [0.0] * 1024
        representative_action = [0.0] * 1024
        for index in range(85):
            representative_state[(index * 11) % 1024] = (index + 1) / 85.0
        for index in range(24):
            representative_action[(index * 37) % 1024] = (index + 1) / 24.0
        dense_benchmark = json.dumps(
            {"S": representative_state, "A": [representative_action] * 4},
            separators=(",", ":"),
        )
        sparse_benchmark = json.dumps(
            {
                "S": sparse_feature_payload(torch.tensor(representative_state)),
                "A": [
                    sparse_feature_payload(torch.tensor(representative_action))
                    for _ in range(4)
                ],
            },
            separators=(",", ":"),
        )
        sparse_reduction = 1.0 - len(sparse_benchmark) / len(dense_benchmark)
        assert sparse_reduction > 0.70
        assert torch.equal(
            dense_feature_tensor(
                json.loads(sparse_benchmark)["S"], torch.float32
            ),
            torch.tensor(representative_state, dtype=torch.float32),
        )
        empty_object_row = copy.deepcopy(rows[0])
        empty_object_row["O"] = []
        tensorize_row(empty_object_row)
        assert empty_object_row["_object_tensor"].shape == (0, 32)
        empty_object_audit = audit_dataset([empty_object_row])
        assert not empty_object_audit["object_audit_passed"]
        assert empty_object_audit["warnings"]
        with tempfile.TemporaryDirectory(
            prefix="aura-teacher-sharded-self-test-"
        ) as sharded_root:
            sharded_input = Path(sharded_root) / "input.jsonl"
            with sharded_input.open("w", encoding="utf-8") as stream:
                for row in rows:
                    payload = {
                        key: value
                        for key, value in row.items()
                        if not key.startswith("_")
                    }
                    if int(payload["I"]) % 2 == 0:
                        payload["S"] = sparse_feature_payload(
                            torch.tensor(payload["S"])
                        )
                        payload["A"] = [
                            sparse_feature_payload(torch.tensor(action))
                            for action in payload["A"]
                        ]
                        payload["O"] = [
                            sparse_feature_payload(torch.tensor(token))
                            for token in payload["O"]
                        ]
                        payload["N"] = sparse_feature_payload(
                            torch.tensor(payload["N"])
                        )
                    stream.write(json.dumps(payload, separators=(",", ":")))
                    stream.write("\n")
            sharded = ShardedTeacherDataset.build(
                sharded_input, args.history, 16, progress
            )
            assert len(sharded) == len(rows)
            assert collate([sharded[0]])["states"].shape[0] == 1
            first_view = ShardedDatasetView(sharded, list(range(16)))
            nested_view = subset_for_row_ids(
                first_view, {int(row["I"]) for row in first_view.rows[::2]}
            )
            assert isinstance(nested_view, ShardedDatasetView)
            assert nested_view.source is first_view
            assert base_sharded_dataset(nested_view) is sharded
            nested_localities = dataset_locality_keys(nested_view)
            assert nested_localities == [
                sharded.locations[first_view.indices[index]][0]
                for index in nested_view.indices
            ]
            sampler_rows = [
                {
                    "_bucket_cost": index % 4,
                    "_sampling_repeats": 2 if index % 3 == 0 else 1,
                }
                for index in range(12)
            ]
            sampler_localities = [0] * 4 + [1] * 4 + [2] * 4
            locality_sampler = LengthBucketBatchSampler(
                sampler_rows,
                2,
                args.seed,
                sampler_localities,
            )
            locality_batches = list(locality_sampler)
            assert len(locality_batches) == len(locality_sampler)
            assert all(
                len({sampler_localities[index] for index in batch}) == 1
                for batch in locality_batches
            )
            sampled_indices = [
                index for batch in locality_batches for index in batch
            ]
            assert all(
                sampled_indices.count(index)
                == int(row["_sampling_repeats"])
                for index, row in enumerate(sampler_rows)
            )
            assert audit_dataset(sharded)["encoding"] == (
                "mixed-sparse-and-legacy-dense"
            )
            selected_sharded = ShardedTeacherDataset.build(
                sharded_input,
                args.history,
                512,
                progress,
                set(range(0, 32)),
            )
            try:
                assert len(selected_sharded) == 32
                assert {int(row["I"]) for row in selected_sharded.rows} == set(
                    range(0, 32)
                )
            finally:
                selected_sharded.close()
            prior_anchor = args.anchor
            prior_fixed_anchor = args.fixed_anchor
            prior_dataset_storage = args.dataset_storage
            prior_loader_workers = args.loader_workers
            try:
                args.anchor = str(Path(sharded_root) / "anchor.jsonl")
                args.fixed_anchor = 1
                args.dataset_storage = "sharded"
                args.loader_workers = 2
                (
                    _,
                    sharded_metrics,
                    sharded_executed,
                    sharded_training_count,
                    sharded_validation_count,
                    _,
                    _,
                    _,
                    _,
                    _,
                ) = train(sharded, args, device, runtime, progress)
                assert math.isfinite(sharded_metrics["policy_ce"])
                assert sharded_metrics["dynamics_frames"] > 0
                assert sharded_executed > 0
                assert sharded_training_count > 0
                assert sharded_validation_count > 0
                assert Path(args.anchor).exists()
            finally:
                args.anchor = prior_anchor
                args.fixed_anchor = prior_fixed_anchor
                args.dataset_storage = prior_dataset_storage
                args.loader_workers = prior_loader_workers
                sharded.close()
        tensorize_rows(rows)
        single_run_rows = rows[:8]
        try:
            split_rows(single_run_rows, args.seed)
            raise AssertionError("single-run training split must be rejected")
        except RuntimeError as error:
            assert "two independent Journey runs" in str(error)
        evaluated_rows, single_run_validation, single_anchor_created = (
            evaluation_and_anchor_rows(single_run_rows, args)
        )
        assert evaluated_rows is single_run_rows
        assert single_run_validation is single_run_rows
        assert not single_anchor_created
        sample_shape = collate(rows[:8])
        bucket_shape = dict(sample_shape)
        bucket_shape["actions"] = torch.zeros(
            sample_shape["actions"].shape[0],
            capacity_bucket(sample_shape["actions"].shape[1]),
            sample_shape["actions"].shape[2],
        )
        assert runtime_cache_key(args, device, sample_shape) == runtime_cache_key(
            args, device, bucket_shape
        )
        wider_shape = dict(sample_shape)
        wider_shape["actions"] = torch.zeros(
            sample_shape["actions"].shape[0],
            capacity_bucket(sample_shape["actions"].shape[1]) + 1,
            sample_shape["actions"].shape[2],
        )
        assert runtime_cache_key(args, device, sample_shape) != runtime_cache_key(
            args, device, wider_shape
        )
        previous_mixed_precision = args.mixed_precision
        try:
            baseline_precision_key = runtime_cache_key(args, device, sample_shape)
            args.mixed_precision = 0 if args.mixed_precision else 1
            assert baseline_precision_key != runtime_cache_key(
                args, device, sample_shape
            )
        finally:
            args.mixed_precision = previous_mixed_precision
        reseed(args.seed)
        expected_random = torch.rand(16)
        reseed(args.seed)
        _ = torch.rand(128)
        reseed(args.seed)
        assert torch.equal(expected_random, torch.rand(16))
        with tempfile.TemporaryDirectory(
            prefix="aura-runtime-cache-self-test-"
        ) as cache_root:
            previous_self_test = args.self_test
            previous_runtime_cache = args.runtime_cache
            previous_micro_batch = args.micro_batch_size
            previous_cpu_threads = args.cpu_threads
            previous_dataset_storage = args.dataset_storage
            previous_loader_workers = args.loader_workers
            try:
                args.self_test = False
                args.runtime_cache = str(Path(cache_root) / "runtime-v4.json")
                args.micro_batch_size = 0
                args.cpu_threads = 0
                args.dataset_storage = "resident"
                args.loader_workers = 2
                reseed(args.seed)
                probe_model = StrategyTransformer(
                    rows[0]["_state_tensor"].shape[0],
                    rows[0]["_action_tensor"].shape[1],
                    args.hidden,
                    args.layers,
                    args.heads,
                    args.ffn,
                    args.history,
                ).to(device)
                cpu_rng_before = torch.random.get_rng_state().clone()
                cuda_rng_before = (
                    [state.clone() for state in torch.cuda.get_rng_state_all()]
                    if device.type == "cuda"
                    else []
                )
                cold_plan = resolve_runtime_plan(
                    probe_model, sample_shape, args, device, runtime
                )
                assert not cold_plan["cache_hit"]
                assert cold_plan["loader_workers"] == 0
                assert torch.equal(cpu_rng_before, torch.random.get_rng_state())
                if device.type == "cuda":
                    assert all(
                        torch.equal(before, after)
                        for before, after in zip(
                            cuda_rng_before, torch.cuda.get_rng_state_all()
                        )
                    )
                hot_plan = resolve_runtime_plan(
                    probe_model, sample_shape, args, device, runtime
                )
                assert hot_plan["cache_hit"]
                assert hot_plan["loader_workers"] == 0
                assert hot_plan["micro_batch_size"] == cold_plan[
                    "micro_batch_size"
                ]
                assert hot_plan["effective_batch_size"] == cold_plan[
                    "effective_batch_size"
                ]
            finally:
                args.self_test = previous_self_test
                args.runtime_cache = previous_runtime_cache
                args.micro_batch_size = previous_micro_batch
                args.cpu_threads = previous_cpu_threads
                args.dataset_storage = previous_dataset_storage
                args.loader_workers = previous_loader_workers
                torch.set_num_threads(int(runtime["cpu_threads"]))
                reseed(args.seed)
        coverage_rows = [
            {"Y": f"coverage:{index // 20}", "E": index // 20}
            for index in range(400)
        ]
        coverage_ids = validation_run_ids(
            coverage_rows,
            args.seed,
            {"coverage:0"},
        )
        coverage_frames = sum(
            1 for row in coverage_rows if run_key(row) in coverage_ids
        )
        assert coverage_frames >= min(192, max(64, len(coverage_rows) // 5))
        assert len(coverage_ids) < 20
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
            legacy_checkpoint_path = Path(root) / "legacy.pt"
            checkpoint_payload = {
                "protocol": "aura.combat-transformer-world-model.v2",
                "state_dimensions": rows[0]["_state_tensor"].shape[0],
                "action_dimensions": rows[0]["_action_tensor"].shape[1],
                "hidden_dimensions": args.hidden,
                "layers": args.layers,
                "heads": args.heads,
                "feedforward_dimensions": args.ffn,
                "history_length": args.history,
                "state_dict": model.state_dict(),
            }
            torch.save(
                checkpoint_payload,
                legacy_checkpoint_path,
            )
            legacy_usable, legacy_generation = load_warm_start(
                model, str(legacy_checkpoint_path), args, device
            )
            assert not legacy_usable and legacy_generation == 0
            checkpoint_payload["teacher_generation"] = 1
            torch.save(checkpoint_payload, checkpoint_path)
            prior_resume = args.resume_model
            prior_training_enabled = args.training_enabled
            prior_epochs = args.epochs
            prior_anchor = args.anchor
            prior_fixed_anchor = args.fixed_anchor
            prior_report_path = args.prior_report
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
                    warm_plan,
                    _,
                    warm_started,
                    _,
                    warm_gate,
                ) = train(rows, args, device, runtime, progress)
                assert warm_started and warm_executed == 0
                assert not warm_gate["update_accepted"]
                assert warm_plan["calibration_kind"] == "annotation-default"
                assert warm_gate["anchor_created"]
                assert Path(args.anchor).exists()
                assert math.isfinite(warm_metrics["policy_ce"])
                reused_report_path = Path(root) / "prior-report.json"
                reused_report_path.write_text(
                    json.dumps(
                        {
                            "ValidationPolicyCrossEntropy": warm_metrics["policy_ce"],
                            "ValidationUniformPolicyCrossEntropy": warm_metrics["uniform_policy_ce"],
                            "ValidationPolicyTop1Accuracy": warm_metrics["policy_accuracy"],
                            "ValidationValueMae": warm_metrics["value_mae"],
                            "ValidationPhaseAccuracy": warm_metrics["phase_accuracy"],
                            "ValidationStrategyAccuracy": warm_metrics["strategy_accuracy"],
                            "ValidationDynamicsMse": warm_metrics["dynamics_mse"],
                            "DynamicsValidationFrames": warm_metrics["dynamics_frames"],
                            "ValidationOutcomeMae": warm_metrics["outcome_mae"],
                            "ValidationDeathBrier": warm_metrics["death_brier"],
                            "ValidationTerminalAccuracy": warm_metrics["terminal_accuracy"],
                            "ValidationFrames": len(rows),
                            "AnchorValidationFrames": len(rows),
                            "AnchorCreated": True,
                            "TeacherGeneration": 1,
                        }
                    ),
                    encoding="utf-8",
                )
                args.prior_report = str(reused_report_path)
                (
                    _,
                    reused_metrics,
                    _,
                    _,
                    _,
                    reused_plan,
                    _,
                    _,
                    _,
                    reused_gate,
                ) = train(rows, args, device, runtime, progress)
                assert reused_plan["evaluation_reused"]
                assert not reused_gate["anchor_created"]
                assert reused_metrics == warm_metrics
                args.prior_report = ""
                args.anchor = ""
                (
                    _,
                    single_metrics,
                    single_executed,
                    single_training_count,
                    single_validation_count,
                    _,
                    _,
                    single_warm_started,
                    _,
                    _,
                ) = train(
                    single_run_rows,
                    args,
                    device,
                    runtime,
                    progress,
                )
                assert single_warm_started and single_executed == 0
                assert single_training_count == 0
                assert single_validation_count == len(single_run_rows)
                assert math.isfinite(single_metrics["policy_ce"])
                annotation_cache_path = str(
                    Path(root) / "annotation-cache.sqlite"
                )
                annotation_output_path = Path(root) / "annotations.jsonl"
                annotation_output_path.write_text(
                    '{"I":7,"P":[0.25,0.75]}\n',
                    encoding="utf-8",
                )
                write_annotation_cache(
                    annotation_cache_path,
                    "model-key",
                    {7: "frame-key"},
                    annotation_output_path,
                )
                assert read_annotation_cache(
                    annotation_cache_path,
                    "model-key",
                    {"frame-key": 2},
                )["frame-key"] == [0.25, 0.75]
                assert not read_annotation_cache(
                    annotation_cache_path,
                    "model-key",
                    {"frame-key": 3},
                )
            finally:
                args.resume_model = prior_resume
                args.training_enabled = prior_training_enabled
                args.epochs = prior_epochs
                args.anchor = prior_anchor
                args.fixed_anchor = prior_fixed_anchor
                args.prior_report = prior_report_path
        if os.name == "nt":
            assert working_set_bytes() > 0
        assert plan["loader_workers"] == 0
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
                    "effectiveBatch": plan["effective_batch_size"],
                    "loaderWorkers": plan["loader_workers"],
                    "sparsePayloadReduction": sparse_reduction,
                    "throughput": throughput,
                }
            )
        )
        return 0
    required = (args.input, args.annotations, args.model, args.report)
    if any(not value for value in required):
        raise RuntimeError("input, annotations, model, and report paths are required")
    started = time.perf_counter()
    progress.update(Stage="loading", Message="正在读取 Transformer 数据集")
    loading_started = time.perf_counter()
    training_selection = read_row_selection(args.training_selection)
    annotation_selection = read_row_selection(args.annotation_selection)
    if args.training_enabled:
        loaded_selection = (
            None
            if training_selection is None
            else training_selection.union(annotation_selection or set())
        )
    else:
        loaded_selection = annotation_selection
    selected_frame_count = (
        max(0, int(args.corpus_frames))
        if loaded_selection is None
        else len(loaded_selection)
    )
    dataset_storage = args.dataset_storage
    if dataset_storage == "auto":
        dataset_storage = (
            "resident"
            if selected_frame_count
            <= max(256, int(args.resident_dataset_maximum_frames))
            else "sharded"
        )
    # Runtime cache keys and worker selection must describe the resolved
    # storage mode, not the user-facing "auto" token.
    args.dataset_storage = dataset_storage
    if dataset_storage == "sharded":
        rows = ShardedTeacherDataset.build(
            Path(args.input),
            args.history,
            args.dataset_shard_frames,
            progress,
            loaded_selection,
        )
    else:
        rows = load_rows(
            Path(args.input), args.history, progress, loaded_selection
        )
    loading_seconds = time.perf_counter() - loading_started
    if not rows:
        raise RuntimeError("Transformer teacher dataset contains no usable frames")
    preparation_started = time.perf_counter()
    progress.update(
        Stage="preparing",
        TotalFrames=len(rows),
        Message="正在张量化并建立序列历史",
    )
    if not isinstance(rows, ShardedTeacherDataset):
        tensorize_rows(rows)
    preparation_seconds = time.perf_counter() - preparation_started
    data_audit = audit_dataset(rows)
    training_rows = subset_for_row_ids(rows, training_selection)
    annotation_rows = subset_for_row_ids(rows, annotation_selection)
    if len(training_rows) == 0:
        raise RuntimeError("Transformer incremental training selection is empty")
    if len(annotation_rows) == 0:
        raise RuntimeError("Transformer annotation selection is empty")
    cuda_fallback_reason = ""
    attempted_cuda_peak = 0
    try:
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
            training_rows,
            args,
            device,
            runtime,
            progress,
            calibration_rows=rows,
        )
        annotation_seconds, annotation_throughput = annotate(
            model,
            annotation_rows,
            plan,
            args,
            device,
            Path(args.annotations),
            progress,
        )
    except (CudaTrainingOutOfMemory, torch.cuda.OutOfMemoryError, RuntimeError) as error:
        if (
            device.type != "cuda"
            or args.backend != "auto"
            or (
                not isinstance(error, CudaTrainingOutOfMemory)
                and not is_cuda_out_of_memory(error)
            )
        ):
            raise
        attempted_cuda_peak = int(torch.cuda.max_memory_allocated(device))
        cuda_fallback_reason = str(error)
        if "model" in locals():
            del model
        gc.collect()
        torch.cuda.empty_cache()
        device = torch.device("cpu")
        torch.set_num_threads(max(1, int(runtime["cpu_threads"])))
        reseed(args.seed)
        progress.update(
            Stage="calibrating",
            Message="CUDA 内存不足，正在以 CPU 安全回退重新训练",
        )
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
            training_rows,
            args,
            device,
            runtime,
            progress,
            calibration_rows=rows,
        )
        annotation_seconds, annotation_throughput = annotate(
            model,
            annotation_rows,
            plan,
            args,
            device,
            Path(args.annotations),
            progress,
        )
    progress.update(Stage="saving", Message="正在写入模型和教师报告")
    saving_started = time.perf_counter()
    checkpoint = {
        "protocol": "aura.combat-transformer-world-model.v4",
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
    if not args.training_enabled and warm_started and args.resume_model:
        shutil.copy2(args.resume_model, args.model)
    else:
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
        "Protocol": "aura.combat-transformer-world-model-report.v4",
        "Success": True,
        "EffectiveBackend": device.type,
        "DeviceName": device_name,
        "PythonVersion": platform.python_version(),
        "TorchVersion": torch.__version__,
        "NumpyVersion": __import__("numpy").__version__,
        "RuntimeAutoTuned": bool(plan["auto_tuned"]),
        "RuntimeAutoTuneCacheHit": bool(plan["cache_hit"]),
        "RuntimeCalibrationKind": str(plan["calibration_kind"]),
        "RuntimeCacheKey": str(plan["runtime_cache_key"]),
        "ReusedPriorEvaluation": bool(plan.get("evaluation_reused", False)),
        "CudaFallbackTriggered": bool(cuda_fallback_reason),
        "CudaFallbackReason": cuda_fallback_reason,
        "EffectiveCpuThreads": int(plan["cpu_threads"]),
        "EffectiveCpuInteropThreads": int(plan["cpu_interop_threads"]),
        "EffectiveBatchSize": int(plan["effective_batch_size"]),
        "EffectiveMicroBatchSize": int(plan["micro_batch_size"]),
        "EffectiveDataLoaderWorkers": int(plan["loader_workers"]),
        "EffectivePrefetchBatches": int(plan["prefetch_batches"]),
        "PinnedMemoryEnabled": bool(plan["pinned_memory"]),
        "NumericPrecision": str(plan["precision"]),
        "DeterministicTrainingEnabled": bool(runtime["deterministic"]),
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
        "ValidationPhaseAccuracy": metrics["phase_accuracy"],
        "ValidationStrategyAccuracy": metrics["strategy_accuracy"],
        "ValidationDynamicsMse": metrics["dynamics_mse"],
        "DynamicsTrainingFrames": sum(
            int(row.get("M", 0)) > 0 for row in dataset_metadata(training_rows)
        ),
        "DynamicsValidationFrames": metrics["dynamics_frames"],
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
        "ProcessCpuSeconds": progress.process_cpu_seconds(),
        "PeakWorkingSetBytes": max(
            progress.peak_working_set_bytes, working_set_bytes()
        ),
        "DatasetStorageMode": (
            "sharded-disk-v3-locality"
            if base_sharded_dataset(rows) is not None
            else "resident"
        ),
        "DatasetShardFrames": (
            max(256, min(4096, int(args.dataset_shard_frames)))
            if base_sharded_dataset(rows) is not None
            else 0
        ),
        "DatasetEncoding": data_audit["encoding"],
        "LoadedDatasetFrames": int(data_audit["loaded_frames"]),
        "IncrementalTrainingSelection": training_selection is not None,
        "IncrementalTrainingFrames": (
            len(training_rows) if args.training_enabled else 0
        ),
        "AnnotationSelectionFrames": len(annotation_rows),
        "AnnotationCacheHits": int(plan.get("annotation_cache_hits", 0)),
        "AnnotationCacheMisses": int(plan.get("annotation_cache_misses", 0)),
        "DenseFeatureSlots": int(data_audit["dense_slots"]),
        "NonZeroFeatureValues": int(data_audit["nonzero_values"]),
        "SparseFeatureDensity": float(data_audit["density"]),
        "ObjectTokenFrames": int(data_audit["object_frames"]),
        "EmptyObjectTokenFrames": int(data_audit["empty_object_frames"]),
        "ObjectTokenFrameCoverage": float(data_audit["object_coverage"]),
        "ObjectTokenAuditPassed": bool(data_audit["object_audit_passed"]),
        "ObjectTokenAuditAdvisoryOnly": False,
        "StrategyLabelFrames": int(data_audit["strategy_label_frames"]),
        "StrategyLabelCounts": {
            key: int(data_audit["strategy_label_counts"][index])
            for index, key in enumerate(
                ("survival", "finale", "bank", "transform", "growth")
            )
        },
        "StrategyApplicableFrames": int(data_audit["strategy_label_frames"]),
        "StrategyApplicableCounts": {
            key: int(data_audit["strategy_applicable_counts"][index])
            for index, key in enumerate(
                ("survival", "finale", "bank", "transform", "growth")
            )
        },
        "StrategyNegativeCounts": {
            key: int(data_audit["strategy_negative_counts"][index])
            for index, key in enumerate(
                ("survival", "finale", "bank", "transform", "growth")
            )
        },
        "StrategyQualityGatePassed": bool(data_audit["strategy_quality_passed"]),
        "InvalidTransitionFrames": int(data_audit["invalid_transition_frames"]),
        "TerminalKnownFrames": int(data_audit["terminal_known_frames"]),
        "DataQualityWarnings": list(data_audit["warnings"])
        + (
            ["CUDA auto backend fell back to CPU: " + cuda_fallback_reason]
            if cuda_fallback_reason
            else []
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
            max(
                attempted_cuda_peak,
                int(torch.cuda.max_memory_allocated(device))
                if device.type == "cuda"
                else 0,
            )
        ),
        "Message": (
            "Transformer teacher training completed."
            if not data_audit["warnings"]
            else "Transformer teacher training completed with data-quality "
            "warnings: " + "; ".join(data_audit["warnings"])
        ),
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
    base_dataset = base_sharded_dataset(rows)
    if base_dataset is not None:
        base_dataset.close()
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(f"Transformer teacher failed: {exc}", file=sys.stderr)
        raise
