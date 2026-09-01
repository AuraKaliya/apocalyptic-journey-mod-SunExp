#!/usr/bin/env python3
"""Train the bounded linear residual used by Aura Combat AI.

Human/policy disagreements provide preference labels. Automatic policy samples
remain outcome trajectories for reporting and future value learning; they are
never converted into invented counterfactual preference labels.
"""

from __future__ import annotations

import argparse
import json
import math
import random
import sys
from collections import defaultdict
from datetime import datetime, timezone
from pathlib import Path


LEARNED_FEATURES = {
    "usefulDefend", "wastedDefend", "effectiveHeal", "overheal",
    "effectiveDraw", "overdraw", "effectiveDamage", "overkill", "lethal",
    "energyScarcity", "freeKnownValue", "semanticConfidence",
    "utilitySurvival", "utilityLethal", "utilityTempo", "utilityResource",
    "utilityDeckEconomy", "utilityScaling", "utilitySynergy",
    "utilityContinuation", "utilityRisk", "utilityUncertainty",
    "utilityCoordination", "categoryAttack", "categoryDefend",
    "categorySupport", "categorySkill", "categoryOther",
}

CURRENT_SAMPLE_PROTOCOL = "aura.combat-ai.sample.v9"
PREVIOUS_SAMPLE_PROTOCOL = "aura.combat-ai.sample.v8"
CURRENT_SELECTION_PROTOCOL = "aura.combat-ai.selection.v2"
PREVIOUS_SELECTION_PROTOCOL = "aura.combat-ai.selection.v1"
CURRENT_FEATURE_SCHEMA = 11


def finite(value: object) -> float:
    try:
        number = float(value)
    except (TypeError, ValueError):
        return 0.0
    return number if math.isfinite(number) else 0.0


def upgrade_sample_in_place(sample: dict) -> bool:
    selection = sample.get("Selection") or {}
    if (
        sample.get("ModelProtocol") == CURRENT_SAMPLE_PROTOCOL
        and int(sample.get("FeatureSchemaVersion", 0))
        == CURRENT_FEATURE_SCHEMA
        and selection.get("Protocol") == CURRENT_SELECTION_PROTOCOL
    ):
        return True
    if not (
        sample.get("ModelProtocol") == PREVIOUS_SAMPLE_PROTOCOL
        and int(sample.get("FeatureSchemaVersion", 0))
        == CURRENT_FEATURE_SCHEMA
        and selection.get("Protocol") == PREVIOUS_SELECTION_PROTOCOL
    ):
        return False

    executed_by = str(selection.get("ExecutedBy") or "").lower()
    known = executed_by in {"human", "emergency-baseline"}
    selection["Protocol"] = CURRENT_SELECTION_PROTOCOL
    selection["AuthorityKnown"] = known
    selection["DecisionAuthority"] = (
        executed_by if known else "legacy-policy-unknown"
    )
    selection["DecisionPurpose"] = "legacy-execution"
    sample["Selection"] = selection
    sample["ModelProtocol"] = CURRENT_SAMPLE_PROTOCOL
    return True


def load_samples(path: Path) -> list[dict]:
    samples: list[dict] = []
    with path.open("r", encoding="utf-8-sig") as stream:
        for line_number, line in enumerate(stream, 1):
            if not line.strip():
                continue
            try:
                sample = json.loads(line)
            except json.JSONDecodeError as exc:
                raise ValueError(f"{path}:{line_number}: invalid JSON: {exc}") from exc
            if (
                upgrade_sample_in_place(sample)
                and sample.get("CompletionState") == "Completed"
                and (sample.get("Selection") or {}).get("Protocol")
                == CURRENT_SELECTION_PROTOCOL
                and (sample.get("Selection") or {}).get(
                    "ExecutedCandidateId"
                )
            ):
                samples.append(sample)
    return samples


def candidate_map(sample: dict) -> dict[str, dict]:
    return {
        str(candidate.get("CandidateId", "")): candidate
        for candidate in sample.get("Candidates", [])
        if candidate.get("Legal") and candidate.get("CandidateId")
    }


def selection_trace(sample: dict) -> dict[str, object]:
    selection = sample["Selection"]
    executed_by = str(selection.get("ExecutedBy") or "policy").lower()
    executed_id = str(selection["ExecutedCandidateId"])
    policy_id = str(
        selection.get("PolicyPreselectedCandidateId")
        or (executed_id if executed_by == "policy" else "")
    )
    agreement = bool(
        executed_by == "human"
        and executed_id
        and policy_id
        and executed_id == policy_id
    )
    visible_to_human = bool(selection.get("PolicyVisibleToHuman", False))
    return {
        "executedBy": executed_by,
        "executedCandidateId": executed_id,
        "policyPreselectedCandidateId": policy_id,
        "humanPolicyAgreement": agreement,
        "policyVisibleToHuman": visible_to_human,
        "hasSelectionV2": selection.get("Protocol")
        == CURRENT_SELECTION_PROTOCOL,
    }


def features(candidate: dict, sample: dict | None = None) -> dict[str, float]:
    result: dict[str, float] = {}
    for key, value in (candidate.get("Features") or {}).items():
        if key in LEARNED_FEATURES:
            result[str(key)] = finite(value)
    utility = candidate.get("Utility") or {}
    for suffix in (
        "Survival", "Lethal", "Tempo", "Resource", "DeckEconomy",
        "Scaling", "Synergy", "Continuation", "Risk", "Uncertainty",
        "Coordination",
    ):
        result.setdefault("utility" + suffix, finite(utility.get(suffix)))

    semantics = candidate.get("Semantics") or {}
    raw = candidate.get("Features") or {}
    damage = (
        finite(semantics.get("Damage", raw.get("damage")))
        * max(1.0, finite(semantics.get("HitCount", raw.get("hitCount", 1))))
        + finite(semantics.get("TrueDamage", raw.get("trueDamage")))
        + finite(semantics.get("DamageOverTime", raw.get("damageOverTime")))
    )
    defend = finite(semantics.get("Defend", raw.get("defend")))
    state = (sample or {}).get("StateFeatures") or {}
    required_defend = max(
        0.0,
        finite(state.get("expectedBlockableDamage"))
        - finite(state.get("playerDefend")),
    )
    useful_defend = min(defend, required_defend)
    result.setdefault("effectiveDamage", damage)
    result.setdefault("usefulDefend", useful_defend)
    result.setdefault("wastedDefend", max(0.0, defend - useful_defend))
    category = action_category(candidate)
    for name in ("attack", "defend", "support", "skill", "other"):
        result.setdefault(
            "category" + name.title(),
            1.0 if category == name else 0.0,
        )
    recognized = damage + defend + sum(
        max(0.0, finite(semantics.get(key)))
        for key in (
            "Heal", "Draw", "EnergyGain", "Buff", "Debuff", "Cleanse",
            "CostReduction", "CardGeneration", "PersistentValue", "Scaling",
        )
    )
    result.setdefault("semanticConfidence", 1.0 if recognized > 0.0 else 0.0)
    return result


def action_category(candidate: dict | None) -> str:
    if not candidate:
        return "unknown"
    kind = str(candidate.get("ActionKind", "")).lower()
    if kind == "endturn":
        return "end-turn"
    semantics = candidate.get("Semantics") or {}
    if (
        finite(semantics.get("Damage"))
        + finite(semantics.get("TrueDamage"))
        + finite(semantics.get("DamageOverTime"))
        > 0.0
    ):
        return "attack"
    if finite(semantics.get("Defend")) > 0.0:
        return "defend"
    if any(
        finite(semantics.get(key)) > 0.0
        for key in (
            "Heal",
            "Draw",
            "EnergyGain",
            "Scaling",
            "DeckValue",
            "Buff",
            "Debuff",
            "Cleanse",
            "CostReduction",
            "CardGeneration",
            "PersistentValue",
        )
    ):
        return "support"
    if kind == "useskill":
        return "skill"
    return "other"


def discounted_returns(samples: list[dict], gamma: float) -> dict[int, float]:
    grouped: dict[str, list[tuple[int, dict]]] = defaultdict(list)
    for index, sample in enumerate(samples):
        grouped[str(sample.get("BattleSessionId", ""))].append((index, sample))
    returns: dict[int, float] = {}
    for values in grouped.values():
        running = 0.0
        for index, sample in reversed(values):
            running = finite(sample.get("Reward")) + gamma * running
            returns[index] = running
    return returns


def make_pairs(samples: list[dict], gamma: float) -> list[dict]:
    pairs: list[dict] = []
    for sample in samples:
        candidates = candidate_map(sample)
        selection = selection_trace(sample)
        chosen_id = str(selection["executedCandidateId"])
        chosen = candidates.get(chosen_id)
        if chosen is None:
            continue
        if selection["executedBy"] != "human":
            continue
        recommended_id = str(selection["policyPreselectedCandidateId"])
        if not recommended_id or recommended_id == chosen_id:
            continue
        recommended = candidates.get(recommended_id)
        if recommended is None:
            continue
        pairs.append(
            {
                "positive": features(chosen, sample),
                "negative": features(recommended, sample),
                "weight": 0.5
                if selection["policyVisibleToHuman"]
                else 2.0,
                "session": str(sample.get("BattleSessionId", "")),
            }
        )
    return pairs


def dataset_report(samples: list[dict], pairs: list[dict], gamma: float) -> dict:
    returns = discounted_returns(samples, gamma)
    actor_counts: dict[str, int] = defaultdict(int)
    executed_distribution: dict[str, dict[str, int]] = defaultdict(
        lambda: defaultdict(int)
    )
    policy_distribution: dict[str, int] = defaultdict(int)
    override_transitions: dict[str, int] = defaultdict(int)
    human_agreements = 0
    human_disagreements = 0
    visible_human_samples = 0
    hidden_human_samples = 0
    visible_human_agreements = 0
    hidden_human_agreements = 0
    selection_v2_count = 0
    policy_rewards: list[float] = []
    policy_returns: list[float] = []

    for index, sample in enumerate(samples):
        candidates = candidate_map(sample)
        selection = selection_trace(sample)
        actor = str(selection["executedBy"])
        actor_counts[actor] += 1
        if selection["hasSelectionV2"]:
            selection_v2_count += 1

        executed = candidates.get(str(selection["executedCandidateId"]))
        proposed = candidates.get(str(selection["policyPreselectedCandidateId"]))
        executed_category = action_category(executed)
        proposed_category = action_category(proposed)
        executed_distribution[actor][executed_category] += 1
        policy_distribution[proposed_category] += 1

        if actor == "human":
            if selection["policyVisibleToHuman"]:
                visible_human_samples += 1
            else:
                hidden_human_samples += 1
            if selection["humanPolicyAgreement"]:
                human_agreements += 1
                if selection["policyVisibleToHuman"]:
                    visible_human_agreements += 1
                else:
                    hidden_human_agreements += 1
            elif proposed is not None and executed is not None:
                human_disagreements += 1
                override_transitions[
                    proposed_category + "->" + executed_category
                ] += 1
        elif actor == "policy":
            policy_rewards.append(finite(sample.get("Reward")))
            policy_returns.append(returns.get(index, 0.0))

    human_total = human_agreements + human_disagreements
    return {
        "ReportProtocol": "aura.combat-ai.training-report.v1",
        "SampleProtocol": CURRENT_SAMPLE_PROTOCOL,
        "SelectionProtocol": CURRENT_SELECTION_PROTOCOL,
        "GeneratedUtc": datetime.now(timezone.utc).isoformat(),
        "SampleCount": len(samples),
        "SelectionCount": selection_v2_count,
        "ActorCounts": dict(sorted(actor_counts.items())),
        "HumanPolicyAgreementCount": human_agreements,
        "HumanPolicyDisagreementCount": human_disagreements,
        "HumanPolicyAgreementRate": (
            human_agreements / human_total if human_total else 0.0
        ),
        "HumanPolicyVisibleCount": visible_human_samples,
        "HumanPolicyHiddenCount": hidden_human_samples,
        "VisibleHumanAgreementRate": (
            visible_human_agreements / visible_human_samples
            if visible_human_samples
            else 0.0
        ),
        "HiddenHumanAgreementRate": (
            hidden_human_agreements / hidden_human_samples
            if hidden_human_samples
            else 0.0
        ),
        "EligiblePreferencePairCount": len(pairs),
        "ExecutedActionDistribution": {
            actor: dict(sorted(values.items()))
            for actor, values in sorted(executed_distribution.items())
        },
        "PolicyPreselectionDistribution": dict(
            sorted(policy_distribution.items())
        ),
        "HumanOverrideTransitions": dict(sorted(override_transitions.items())),
        "PolicyTrajectory": {
            "SampleCount": len(policy_rewards),
            "MeanImmediateReward": (
                sum(policy_rewards) / len(policy_rewards)
                if policy_rewards
                else 0.0
            ),
            "MeanDiscountedReturn": (
                sum(policy_returns) / len(policy_returns)
                if policy_returns
                else 0.0
            ),
            "PositiveRewardCount": sum(
                1 for value in policy_rewards if value > 0.0
            ),
            "NegativeRewardCount": sum(
                1 for value in policy_rewards if value < 0.0
            ),
            "ZeroRewardCount": sum(
                1 for value in policy_rewards if value == 0.0
            ),
            "UsedAsPreferenceLabels": False,
        },
    }


def write_json(path: Path, value: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps(value, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )


def statistics(
    pairs: list[dict],
) -> tuple[dict[str, float], dict[str, float], dict[str, float], dict[str, float], dict[str, float]]:
    values: dict[str, list[float]] = defaultdict(list)
    for pair in pairs:
        for side in ("positive", "negative"):
            for key, value in pair[side].items():
                values[key].append(value)
    means: dict[str, float] = {}
    scales: dict[str, float] = {}
    minimums: dict[str, float] = {}
    maximums: dict[str, float] = {}
    counts: dict[str, float] = {}
    for key, items in values.items():
        mean = sum(items) / len(items)
        variance = sum((item - mean) ** 2 for item in items) / len(items)
        means[key] = mean
        scales[key] = max(1e-6, math.sqrt(variance))
        minimums[key] = min(items)
        maximums[key] = max(items)
        counts[key] = float(len(items))
    return means, scales, minimums, maximums, counts


def vector_difference(
    pair: dict, means: dict[str, float], scales: dict[str, float]
) -> dict[str, float]:
    result: dict[str, float] = {}
    keys = set(pair["positive"]) | set(pair["negative"])
    for key in keys:
        scale = scales.get(key, 1.0)
        positive = (pair["positive"].get(key, 0.0) - means.get(key, 0.0)) / scale
        negative = (pair["negative"].get(key, 0.0) - means.get(key, 0.0)) / scale
        difference = positive - negative
        if abs(difference) > 1e-12:
            result[key] = difference
    return result


def dot(weights: dict[str, float], values: dict[str, float]) -> float:
    return sum(weights.get(key, 0.0) * value for key, value in values.items())


def accuracy(weights: dict[str, float], vectors: list[tuple[dict, float]]) -> float:
    if not vectors:
        return 0.0
    correct = sum(weight for vector, weight in vectors if dot(weights, vector) > 0.0)
    total = sum(weight for _, weight in vectors)
    return correct / total if total > 0.0 else 0.0


def train(
    pairs: list[dict],
    epochs: int,
    learning_rate: float,
    l2: float,
    seed: int,
) -> tuple[dict, dict, dict, dict, dict, dict, dict]:
    means, scales, minimums, maximums, counts = statistics(pairs)
    sessions = sorted({pair["session"] for pair in pairs})
    test_sessions = {
        session
        for index, session in enumerate(sessions)
        if len(sessions) > 1 and index % 5 == 0
    }
    train_vectors: list[tuple[dict, float]] = []
    test_vectors: list[tuple[dict, float]] = []
    for pair in pairs:
        value = (vector_difference(pair, means, scales), pair["weight"])
        (test_vectors if pair["session"] in test_sessions else train_vectors).append(value)
    if not train_vectors:
        train_vectors, test_vectors = test_vectors, []

    rng = random.Random(seed)
    weights: dict[str, float] = {key: 0.0 for key in means}
    for epoch in range(max(1, epochs)):
        rng.shuffle(train_vectors)
        rate = learning_rate / math.sqrt(1.0 + epoch * 0.05)
        for vector, sample_weight in train_vectors:
            score = max(-30.0, min(30.0, dot(weights, vector)))
            gradient_factor = sample_weight / (1.0 + math.exp(score))
            for key, value in vector.items():
                updated = weights.get(key, 0.0) + rate * (
                    gradient_factor * value - l2 * weights.get(key, 0.0)
                )
                weights[key] = max(-2.0, min(2.0, updated))

    weights = {key: value for key, value in weights.items() if abs(value) >= 1e-6}
    metrics = {
        "pairCount": float(len(pairs)),
        "trainingPairCount": float(len(train_vectors)),
        "testPairCount": float(len(test_vectors)),
        "trainingAccuracy": accuracy(weights, train_vectors),
        "testAccuracy": accuracy(weights, test_vectors),
    }
    return weights, means, scales, minimums, maximums, counts, metrics


def self_test() -> int:
    samples = [
        {
            "ModelProtocol": CURRENT_SAMPLE_PROTOCOL,
            "FeatureSchemaVersion": CURRENT_FEATURE_SCHEMA,
            "CompletionState": "Completed",
            "BattleSessionId": index,
            "Selection": {
                "Protocol": CURRENT_SELECTION_PROTOCOL,
                "ExecutedBy": "human",
                "ExecutedCandidateId": "attack",
                "PolicyPreselectedCandidateId": "shield",
                "PolicyWasExecuted": False,
                "HumanPolicyAgreement": False,
                "PolicyVisibleToHuman": index == 0,
            },
            "Candidates": [
                {
                    "CandidateId": "attack",
                    "Legal": True,
                    "Features": {"damage": 8, "defend": 0},
                },
                {
                    "CandidateId": "shield",
                    "Legal": True,
                    "Features": {"damage": 0, "defend": 6},
                },
            ],
        }
        for index in range(10)
    ]
    samples.append(
        {
            "ModelProtocol": CURRENT_SAMPLE_PROTOCOL,
            "FeatureSchemaVersion": CURRENT_FEATURE_SCHEMA,
            "CompletionState": "Completed",
            "BattleSessionId": 99,
            "Selection": {
                "Protocol": CURRENT_SELECTION_PROTOCOL,
                "ExecutedBy": "policy",
                "ExecutedCandidateId": "shield",
                "PolicyPreselectedCandidateId": "shield",
                "PolicyWasExecuted": True,
                "HumanPolicyAgreement": False,
                "PolicyVisibleToHuman": False,
            },
            "Reward": -100,
            "Candidates": [
                {
                    "CandidateId": "attack",
                    "Legal": True,
                    "ActionKind": "PlayCard",
                    "Semantics": {"Damage": 8},
                    "Features": {"damage": 8, "defend": 0},
                },
                {
                    "CandidateId": "shield",
                    "Legal": True,
                    "ActionKind": "PlayCard",
                    "Semantics": {"Defend": 6},
                    "Features": {"damage": 0, "defend": 6},
                },
            ],
        }
    )
    pairs = make_pairs(samples, gamma=0.97)
    if len(pairs) != 10:
        raise AssertionError("policy trajectories must not create preference pairs")
    if pairs[0]["weight"] != 0.5 or any(
        pair["weight"] != 2.0 for pair in pairs[1:]
    ):
        raise AssertionError("visible human advice must receive the lower preference weight")
    report = dataset_report(samples, pairs, 0.97)
    if (
        report["PolicyTrajectory"]["SampleCount"] != 1
        or report["HumanPolicyDisagreementCount"] != 10
    ):
        raise AssertionError("training report failed to separate data sources")
    weights, _, _, _, _, _, metrics = train(pairs, 100, 0.05, 0.001, 7)
    if weights.get("effectiveDamage", 0.0) <= weights.get("wastedDefend", 0.0):
        raise AssertionError("trainer failed to prefer demonstrated damage")
    print(json.dumps(metrics, ensure_ascii=False))
    return 0


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--report", type=Path)
    parser.add_argument("--report-only", action="store_true")
    parser.add_argument("--gamma", type=float, default=0.97)
    parser.add_argument("--epochs", type=int, default=250)
    parser.add_argument("--learning-rate", type=float, default=0.04)
    parser.add_argument("--l2", type=float, default=0.001)
    parser.add_argument("--seed", type=int, default=20260723)
    parser.add_argument(
        "--profile",
        choices=("balanced", "aggressive", "defensive"),
        default="balanced",
    )
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        return self_test()
    if args.input is None:
        parser.error("--input is required")

    samples = load_samples(args.input)
    samples = [
        sample for sample in samples
        if str(sample.get("DecisionProfile") or "balanced").lower()
        == args.profile
    ]
    pairs = make_pairs(samples, args.gamma)
    report_path = args.report or args.input.with_name(
        "auto-battle-training-report.json"
    )
    report = dataset_report(samples, pairs, args.gamma)
    write_json(report_path, report)
    print(json.dumps(report, ensure_ascii=False))
    print(report_path)
    if args.report_only:
        return 0
    if not pairs:
        raise ValueError(
            "no training pairs; collect completed v6 human samples where "
            "the player overrides the policy preselection"
        )
    weights, means, scales, minimums, maximums, counts, metrics = train(
        pairs, args.epochs, args.learning_rate, args.l2, args.seed
    )
    metrics.update(
        {
            "humanSampleCount": float(
                report["ActorCounts"].get("human", 0)
            ),
            "policyTrajectoryCount": float(
                report["ActorCounts"].get("policy", 0)
            ),
            "humanPolicyAgreementRate": float(
                report["HumanPolicyAgreementRate"]
            ),
            "visibleHumanSampleCount": float(
                report["HumanPolicyVisibleCount"]
            ),
            "hiddenHumanSampleCount": float(
                report["HumanPolicyHiddenCount"]
            ),
        }
    )
    output = args.output or args.input.with_name(
        "auto-battle-model-candidate-" + args.profile + ".json"
    )
    category_counts: dict[str, float] = defaultdict(float)
    for pair in pairs:
        for side in ("positive", "negative"):
            for category in (
                "categoryAttack", "categoryDefend", "categorySupport",
                "categorySkill", "categoryOther",
            ):
                if pair[side].get(category, 0.0) > 0.5:
                    category_counts[category] += 1.0
    model = {
        "ModelProtocol": "aura.decision-residual.linear.v1",
        "ModelId": "aura-combat-linear-" + datetime.now(timezone.utc).strftime("%Y%m%d%H%M%S"),
        "ProtocolVersion": 1,
        "FeatureSchemaVersion": CURRENT_FEATURE_SCHEMA,
        "ApplicabilityProtocolVersion": 1,
        "DecisionProfile": args.profile,
        "Bias": 0.0,
        "MaximumCorrection": 2.0,
        "Weights": weights,
        "Means": {key: means[key] for key in weights},
        "Scales": {key: scales[key] for key in weights},
        "FeatureMinimums": {key: minimums[key] for key in weights},
        "FeatureMaximums": {key: maximums[key] for key in weights},
        "FeatureObservationCounts": {key: counts[key] for key in weights},
        "CategoryObservationCounts": dict(category_counts),
        "MinimumCategoryObservations": 5.0,
        "Metrics": metrics,
        "CreatedUtc": datetime.now(timezone.utc).isoformat(),
    }
    write_json(output, model)
    print(f"trained {len(weights)} weights from {len(pairs)} pairs")
    print(json.dumps(metrics, ensure_ascii=False))
    print(output)
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(f"training failed: {exc}", file=sys.stderr)
        raise SystemExit(1)
