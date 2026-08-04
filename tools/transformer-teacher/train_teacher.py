#!/usr/bin/env python3
"""Train an offline sequence Transformer and emit soft policy annotations."""

from __future__ import annotations

import argparse
import copy
import json
import math
import os
import platform
import random
import sys
import time
from pathlib import Path

try:
    import torch
    from torch import nn
    from torch.utils.data import DataLoader, Dataset
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
    parser.add_argument("--hidden", type=int, default=64)
    parser.add_argument("--layers", type=int, default=2)
    parser.add_argument("--heads", type=int, default=4)
    parser.add_argument("--history", type=int, default=12)
    parser.add_argument("--cpu-threads", type=int, default=0)
    parser.add_argument("--seed", type=int, default=1701)
    parser.add_argument("--self-test", action="store_true")
    return parser.parse_args()


def choose_device(backend: str) -> torch.device:
    if backend == "cuda":
        if not torch.cuda.is_available():
            raise RuntimeError("CUDA backend was requested but torch.cuda.is_available() is false")
        return torch.device("cuda")
    if backend == "auto" and torch.cuda.is_available():
        return torch.device("cuda")
    return torch.device("cpu")


def configure_runtime(args: argparse.Namespace) -> torch.device:
    random.seed(args.seed)
    torch.manual_seed(args.seed)
    if torch.cuda.is_available():
        torch.cuda.manual_seed_all(args.seed)
    threads = args.cpu_threads or max(1, min(16, os.cpu_count() or 1))
    torch.set_num_threads(threads)
    try:
        torch.set_num_interop_threads(max(1, min(4, threads)))
    except RuntimeError:
        pass
    return choose_device(args.backend)


def normalized(values: list[float]) -> list[float]:
    finite = [max(0.0, float(value)) for value in values]
    total = sum(finite)
    if total <= 0.0:
        return [1.0 / max(1, len(finite)) for _ in finite]
    return [value / total for value in finite]


def load_rows(path: Path, history_length: int) -> list[dict]:
    rows: list[dict] = []
    with path.open("r", encoding="utf-8") as stream:
        for line in stream:
            if not line.strip():
                continue
            row = json.loads(line)
            if len(row.get("A", [])) < 2:
                continue
            row["P"] = normalized(row.get("P", []))
            row["_history"] = []
            rows.append(row)
    by_episode: dict[int, list[dict]] = {}
    for row in rows:
        by_episode.setdefault(int(row["E"]), []).append(row)
    for episode_rows in by_episode.values():
        episode_rows.sort(key=lambda row: (int(row["T"]), int(row["Q"]), int(row["F"])))
        for index, row in enumerate(episode_rows):
            row["_history"] = episode_rows[max(0, index - history_length) : index]
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
                "A": actions,
                "P": policy,
                "X": chosen,
                "V": math.tanh(state[0] + actions[chosen][0]),
                "G": index % 5,
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


class TeacherDataset(Dataset):
    def __init__(self, rows: list[dict]):
        self.rows = rows

    def __len__(self) -> int:
        return len(self.rows)

    def __getitem__(self, index: int) -> dict:
        return self.rows[index]


def split_rows(rows: list[dict], seed: int) -> tuple[list[dict], list[dict]]:
    episodes = sorted({int(row["E"]) for row in rows})
    random.Random(seed).shuffle(episodes)
    validation_count = max(1, int(round(len(episodes) * 0.2)))
    validation_ids = set(episodes[-validation_count:])
    training = [row for row in rows if int(row["E"]) not in validation_ids]
    validation = [row for row in rows if int(row["E"]) in validation_ids]
    if not training:
        training = rows[:-1]
        validation = rows[-1:]
    return training, validation


def collate(rows: list[dict]) -> dict[str, torch.Tensor]:
    batch = len(rows)
    state_dimensions = len(rows[0]["S"])
    action_dimensions = len(rows[0]["A"][0])
    maximum_actions = max(len(row["A"]) for row in rows)
    maximum_history = max(len(row["_history"]) for row in rows)
    states = torch.zeros(batch, state_dimensions)
    actions = torch.zeros(batch, maximum_actions, action_dimensions)
    action_mask = torch.zeros(batch, maximum_actions, dtype=torch.bool)
    policies = torch.zeros(batch, maximum_actions)
    history_states = torch.zeros(batch, maximum_history, state_dimensions)
    history_actions = torch.zeros(batch, maximum_history, action_dimensions)
    history_mask = torch.zeros(batch, maximum_history, dtype=torch.bool)
    values = torch.zeros(batch)
    strategies = torch.full((batch,), -1, dtype=torch.long)
    row_ids = torch.zeros(batch, dtype=torch.long)
    for owner, row in enumerate(rows):
        states[owner] = torch.tensor(row["S"], dtype=torch.float32)
        action_count = len(row["A"])
        actions[owner, :action_count] = torch.tensor(row["A"], dtype=torch.float32)
        action_mask[owner, :action_count] = True
        policies[owner, :action_count] = torch.tensor(row["P"], dtype=torch.float32)
        history = row["_history"]
        offset = maximum_history - len(history)
        for history_index, prior in enumerate(history):
            slot = offset + history_index
            history_states[owner, slot] = torch.tensor(prior["S"], dtype=torch.float32)
            executed = max(0, min(len(prior["A"]) - 1, int(prior["X"])))
            history_actions[owner, slot] = torch.tensor(
                prior["A"][executed], dtype=torch.float32
            )
            history_mask[owner, slot] = True
        values[owner] = float(row["V"])
        strategies[owner] = int(row.get("G", -1))
        row_ids[owner] = int(row["I"])
    return {
        "states": states,
        "actions": actions,
        "action_mask": action_mask,
        "policies": policies,
        "history_states": history_states,
        "history_actions": history_actions,
        "history_mask": history_mask,
        "values": values,
        "strategies": strategies,
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
        history_length: int,
    ):
        super().__init__()
        self.hidden = hidden
        self.history_length = history_length
        self.cls = nn.Parameter(torch.zeros(1, 1, hidden))
        self.state_projection = nn.Linear(state_dimensions, hidden)
        self.action_projection = nn.Linear(action_dimensions, hidden)
        self.history_projection = nn.Linear(state_dimensions + action_dimensions, hidden)
        self.type_embedding = nn.Embedding(4, hidden)
        self.position_embedding = nn.Embedding(history_length + 2, hidden)
        layer = nn.TransformerEncoderLayer(
            d_model=hidden,
            nhead=heads,
            dim_feedforward=hidden * 2,
            dropout=0.10,
            activation="gelu",
            batch_first=True,
            norm_first=False,
        )
        self.encoder = nn.TransformerEncoder(layer, layers, norm=nn.LayerNorm(hidden))
        self.policy_head = nn.Linear(hidden, 1)
        self.value_head = nn.Sequential(nn.Linear(hidden, hidden), nn.GELU(), nn.Linear(hidden, 1))
        self.strategy_head = nn.Linear(hidden, 5)
        nn.init.normal_(self.cls, std=0.02)

    def forward(self, batch: dict[str, torch.Tensor]) -> tuple[torch.Tensor, ...]:
        states = batch["states"]
        actions = batch["actions"]
        history_states = batch["history_states"]
        history_actions = batch["history_actions"]
        history_mask = batch["history_mask"]
        action_mask = batch["action_mask"]
        owners = states.shape[0]
        history_count = history_states.shape[1]
        action_count = actions.shape[1]
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
        action_tokens = self.action_projection(actions) + self.type_embedding.weight[3]
        tokens = torch.cat((cls, history, state_token, action_tokens), dim=1)
        padding = torch.cat(
            (
                torch.zeros(owners, 1, dtype=torch.bool, device=states.device),
                ~history_mask,
                torch.zeros(owners, 1, dtype=torch.bool, device=states.device),
                ~action_mask,
            ),
            dim=1,
        )
        encoded = self.encoder(tokens, src_key_padding_mask=padding)
        cls_encoded = encoded[:, 0]
        action_start = 1 + history_count + 1
        action_encoded = encoded[:, action_start : action_start + action_count]
        policy = self.policy_head(action_encoded).squeeze(-1)
        policy = policy.masked_fill(~action_mask, -1.0e9)
        value = torch.tanh(self.value_head(cls_encoded).squeeze(-1))
        strategy = self.strategy_head(cls_encoded)
        return policy, value, strategy


def move(batch: dict[str, torch.Tensor], device: torch.device) -> dict[str, torch.Tensor]:
    return {key: value.to(device, non_blocking=device.type == "cuda") for key, value in batch.items()}


def loss_for(
    model: StrategyTransformer, batch: dict[str, torch.Tensor]
) -> tuple[torch.Tensor, dict[str, float]]:
    policy_logits, values, strategy_logits = model(batch)
    log_policy = torch.log_softmax(policy_logits, dim=-1)
    policy_loss = -(batch["policies"] * log_policy).sum(dim=-1).mean()
    value_loss = torch.nn.functional.mse_loss(values, batch["values"])
    valid_strategy = batch["strategies"] >= 0
    if valid_strategy.any():
        strategy_loss = torch.nn.functional.cross_entropy(
            strategy_logits[valid_strategy], batch["strategies"][valid_strategy]
        )
    else:
        strategy_loss = policy_loss.new_zeros(())
    total = policy_loss + value_loss * 0.35 + strategy_loss * 0.20
    return total, {
        "policy": float(policy_loss.detach()),
        "value": float(value_loss.detach()),
        "strategy": float(strategy_loss.detach()),
    }


@torch.no_grad()
def evaluate(
    model: StrategyTransformer, loader: DataLoader, device: torch.device
) -> dict[str, float]:
    model.eval()
    count = 0
    policy_cross_entropy = 0.0
    uniform_policy_cross_entropy = 0.0
    policy_correct = 0
    value_error = 0.0
    strategy_count = 0
    strategy_correct = 0
    for raw in loader:
        batch = move(raw, device)
        policy_logits, values, strategy_logits = model(batch)
        log_policy = torch.log_softmax(policy_logits, dim=-1)
        size = batch["states"].shape[0]
        policy_cross_entropy += float(
            (-(batch["policies"] * log_policy).sum(dim=-1)).sum()
        )
        policy_correct += int(
            (policy_logits.argmax(dim=-1) == batch["policies"].argmax(dim=-1)).sum()
        )
        uniform_policy_cross_entropy += float(
            batch["action_mask"].sum(dim=-1).float().log().sum()
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
        count += size
    return {
        "policy_ce": policy_cross_entropy / max(1, count),
        "uniform_policy_ce": uniform_policy_cross_entropy / max(1, count),
        "policy_accuracy": policy_correct / max(1, count),
        "value_mae": value_error / max(1, count),
        "strategy_accuracy": strategy_correct / max(1, strategy_count),
    }


def train(
    rows: list[dict], args: argparse.Namespace, device: torch.device
) -> tuple[StrategyTransformer, dict[str, float], int, int, int]:
    training_rows, validation_rows = split_rows(rows, args.seed)
    generator = torch.Generator().manual_seed(args.seed)
    training_loader = DataLoader(
        TeacherDataset(training_rows),
        batch_size=args.batch_size,
        shuffle=True,
        collate_fn=collate,
        generator=generator,
    )
    validation_loader = DataLoader(
        TeacherDataset(validation_rows),
        batch_size=args.batch_size,
        shuffle=False,
        collate_fn=collate,
    )
    model = StrategyTransformer(
        len(rows[0]["S"]),
        len(rows[0]["A"][0]),
        args.hidden,
        args.layers,
        args.heads,
        args.history,
    ).to(device)
    optimizer = torch.optim.AdamW(model.parameters(), lr=3.0e-4, weight_decay=1.0e-3)
    best_state = copy.deepcopy(model.state_dict())
    best_metrics = evaluate(model, validation_loader, device)
    best_loss = best_metrics["policy_ce"] + best_metrics["value_mae"] * 0.35
    stale = 0
    executed = 0
    for epoch in range(1, args.epochs + 1):
        model.train()
        for raw in training_loader:
            batch = move(raw, device)
            optimizer.zero_grad(set_to_none=True)
            total, _ = loss_for(model, batch)
            total.backward()
            torch.nn.utils.clip_grad_norm_(model.parameters(), 1.0)
            optimizer.step()
        executed = epoch
        metrics = evaluate(model, validation_loader, device)
        score = metrics["policy_ce"] + metrics["value_mae"] * 0.35
        print(
            f"epoch={epoch}/{args.epochs} policyCE={metrics['policy_ce']:.6f} "
            f"top1={metrics['policy_accuracy']:.4f} valueMAE={metrics['value_mae']:.6f}",
            flush=True,
        )
        if score < best_loss - 1.0e-5:
            best_loss = score
            best_metrics = metrics
            best_state = copy.deepcopy(model.state_dict())
            stale = 0
        else:
            stale += 1
        if epoch >= 4 and stale >= 4:
            break
    model.load_state_dict(best_state)
    return model, best_metrics, executed, len(training_rows), len(validation_rows)


@torch.no_grad()
def annotate(
    model: StrategyTransformer,
    rows: list[dict],
    batch_size: int,
    device: torch.device,
    path: Path,
) -> None:
    loader = DataLoader(
        TeacherDataset(rows), batch_size=batch_size, shuffle=False, collate_fn=collate
    )
    model.eval()
    with path.open("w", encoding="utf-8", newline="\n") as stream:
        for raw in loader:
            batch = move(raw, device)
            logits, _, _ = model(batch)
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


def write_report(path: Path, payload: dict) -> None:
    path.write_text(json.dumps(payload, indent=2, ensure_ascii=True), encoding="utf-8")


def main() -> int:
    args = arguments()
    device = configure_runtime(args)
    if args.self_test:
        args.epochs = min(args.epochs, 2)
        args.batch_size = min(args.batch_size, 16)
        rows = synthetic_rows()
        model, metrics, executed, training_count, validation_count = train(
            rows, args, device
        )
        assert math.isfinite(metrics["policy_ce"])
        assert executed > 0 and training_count > 0 and validation_count > 0
        print(
            json.dumps(
                {
                    "success": True,
                    "device": str(device),
                    "torch": torch.__version__,
                    "policyCE": metrics["policy_ce"],
                    "uniformPolicyCE": metrics["uniform_policy_ce"],
                }
            )
        )
        return 0
    required = (args.input, args.annotations, args.model, args.report)
    if any(not value for value in required):
        raise RuntimeError("input, annotations, model, and report paths are required")
    started = time.perf_counter()
    rows = load_rows(Path(args.input), args.history)
    if not rows:
        raise RuntimeError("Transformer teacher dataset contains no usable frames")
    model, metrics, executed, training_count, validation_count = train(
        rows, args, device
    )
    annotate(model, rows, args.batch_size, device, Path(args.annotations))
    checkpoint = {
        "protocol": "aura.combat-transformer-teacher.v1",
        "state_dimensions": len(rows[0]["S"]),
        "action_dimensions": len(rows[0]["A"][0]),
        "hidden_dimensions": args.hidden,
        "layers": args.layers,
        "heads": args.heads,
        "history_length": args.history,
        "state_dict": model.state_dict(),
    }
    torch.save(checkpoint, args.model)
    device_name = (
        torch.cuda.get_device_name(device) if device.type == "cuda" else platform.processor()
    )
    report = {
        "Protocol": "aura.combat-transformer-teacher-report.v1",
        "Success": True,
        "EffectiveBackend": device.type,
        "DeviceName": device_name,
        "PythonVersion": platform.python_version(),
        "TorchVersion": torch.__version__,
        "TrainingFrames": training_count,
        "ValidationFrames": validation_count,
        "EpochsExecuted": executed,
        "ValidationPolicyCrossEntropy": metrics["policy_ce"],
        "ValidationUniformPolicyCrossEntropy": metrics["uniform_policy_ce"],
        "ValidationPolicyTop1Accuracy": metrics["policy_accuracy"],
        "ValidationValueMae": metrics["value_mae"],
        "ValidationStrategyAccuracy": metrics["strategy_accuracy"],
        "ElapsedSeconds": time.perf_counter() - started,
        "Message": "Transformer teacher training completed.",
    }
    write_report(Path(args.report), report)
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(f"Transformer teacher failed: {exc}", file=sys.stderr)
        raise
