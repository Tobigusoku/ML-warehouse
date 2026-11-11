from __future__ import annotations

import argparse
import difflib
import filecmp
import json
import os
import re
import shutil
import subprocess
import sys
import time
from pathlib import Path


ROOT = Path(__file__).resolve().parent
DEFAULT_SETTINGS = ROOT / "tr.txt"


def read_settings(path: Path) -> dict[str, str]:
    data: dict[str, str] = {}
    if not path.exists():
        raise FileNotFoundError(f"settings file not found: {path}")

    for raw in path.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        if "=" not in line:
            raise ValueError(f"invalid setting line: {raw!r}")
        key, value = line.split("=", 1)
        data[key.strip().lower()] = value.strip()
    return data


def as_bool(value: str, default: bool = False) -> bool:
    if value == "":
        return default
    return value.lower() in {"1", "true", "yes", "y", "on"}


def rel_path(value: str) -> Path:
    p = Path(value)
    return p if p.is_absolute() else ROOT / p


def next_index(models_dir: Path, results_dir: Path, prefix: str, digits: int) -> int:
    pattern = re.compile(rf"^{re.escape(prefix)}(\d+)\.(onnx|nn)$", re.IGNORECASE)
    run_pattern = re.compile(rf"^{re.escape(prefix)}(\d+)$", re.IGNORECASE)
    highest = 0
    if models_dir.exists():
        for p in models_dir.iterdir():
            m = pattern.match(p.name)
            if m:
                highest = max(highest, int(m.group(1)))
    if results_dir.exists():
        for p in results_dir.iterdir():
            m = run_pattern.match(p.name)
            if m:
                highest = max(highest, int(m.group(1)))
    return highest + 1


def find_model(run_dir: Path, behavior: str, run_id: str | None = None) -> Path:
    preferred = [run_dir / f"{run_id}{ext}" for ext in (".onnx", ".nn")] if run_id else []
    preferred.extend([run_dir / f"{behavior}.onnx", run_dir / f"{behavior}.nn"])
    for p in preferred:
        if p.exists():
            return p

    candidates = [
        p for p in run_dir.rglob("*")
        if p.suffix.lower() in {".onnx", ".nn"} and p.is_file()
    ]
    if not candidates:
        raise FileNotFoundError(f"no .onnx/.nn model found under {run_dir}")
    return max(candidates, key=lambda p: p.stat().st_mtime)


def rename_model_to_run_id(model: Path, run_dir: Path, run_id: str) -> Path:
    """Make the selected final model inside results/<run-id> use the run-id name."""
    target = run_dir / f"{run_id}{model.suffix.lower()}"
    if model.resolve() == target.resolve():
        return target

    if target.exists():
        target.unlink()
    model.rename(target)
    return target


def split_args(value: str) -> list[str]:
    return [part for part in value.split() if part]


def read_trainer_seeds(settings: dict[str, str], runs: int) -> list[int]:
    raw = settings.get("trainer_seeds", "").strip()
    if not raw:
        raise ValueError(
            "trainer_seeds is required (example: trainer_seeds=1,2,3)"
        )

    parts = [part for part in re.split(r"[,\s]+", raw) if part]
    seeds = [int(part) for part in parts]
    if len(seeds) != runs:
        raise ValueError(
            f"trainer_seeds contains {len(seeds)} values, but runs={runs}"
        )
    if any(seed < 0 for seed in seeds):
        raise ValueError("trainer_seeds must contain non-negative integers")
    if len(set(seeds)) != len(seeds):
        raise ValueError("trainer_seeds must not contain duplicate values")
    return seeds


def check_catalog_ids(requested_ids: list[str]) -> None:
    """Catch settings typos before launching Unity; the Player still validates its own build."""
    catalog = ROOT / "Assets/Resources/WarehouseExperimentConfigCatalog.asset"
    if not catalog.exists():
        raise FileNotFoundError(f"experiment config catalog not found: {catalog}")
    references = set(re.findall(
        r"(?m)^  - \{fileID: 11400000, guid: ([0-9a-f]{32}), type: 2\}$",
        catalog.read_text(encoding="utf-8-sig"),
    ))
    available: dict[str, str] = {}
    for asset in (ROOT / "Assets/ExperimentConfigs").glob("*.asset"):
        meta = asset.with_suffix(".asset.meta")
        if not meta.exists():
            continue
        guid_match = re.search(r"(?m)^guid: ([0-9a-f]{32})$", meta.read_text(encoding="utf-8-sig"))
        if not guid_match or guid_match.group(1) not in references:
            continue
        id_match = re.search(r"(?m)^  configId: ([^\r\n]+)$", asset.read_text(encoding="utf-8-sig"))
        if id_match:
            available[id_match.group(1).strip()] = guid_match.group(1)
    for config_id in requested_ids:
        if config_id not in available:
            suggestion = difflib.get_close_matches(config_id, available, n=1)
            hint = f" Did you mean {suggestion[0]!r}?" if suggestion else ""
            raise ValueError(f"experiment_ids: {config_id!r} is not in the local catalog.{hint}")


def batch_run_id(prefix: str, config_id: str, seed: int, digits: int, separator: str) -> str:
    """Keep the exact Config ID for selection while adding a separator to the run name."""
    return f"{(prefix + config_id).rstrip('_-')}{separator}{seed:0{digits}d}"


def copy_legacy_provenance(env: Path, run_dir: Path, run_id: str) -> None:
    """Recover provenance written beside an older build instead of into results_dir."""
    target = run_dir / "unity_experiment.json"
    if target.exists():
        return

    legacy = env.parent / "results" / run_id / "unity_experiment.json"
    if not legacy.exists():
        print(f"warning: Unity provenance not found: {target}", file=sys.stderr)
        return

    run_dir.mkdir(parents=True, exist_ok=True)
    shutil.copy2(legacy, target)
    print(f"recovered provenance: {legacy} -> {target}", flush=True)


def verify_provenance(path: Path, run_tag: str, trainer_seed: int, config_id: str) -> bool:
    if not path.exists():
        return False
    try:
        record = json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, json.JSONDecodeError):
        return False  # Another Player may still be writing the record.
    expected = {"runId": run_tag, "trainerSeed": str(trainer_seed), "presetId": config_id}
    for key, value in expected.items():
        if record.get(key) != value:
            raise ValueError(f"{path}: {key} is {record.get(key)!r}, expected {value!r}")
    if not record.get("usePreset"):
        raise ValueError(f"{path}: Unity did not use an experiment config")
    return True


def recover_finished_run(settings: dict[str, str], run_tag: str, trainer_seed: int,
                         config_id: str, *, dry_run: bool = False) -> Path:
    """Use a completed trainer output without starting another training process."""
    run_dir = rel_path(settings.get("results", "results")) / run_tag
    provenance = run_dir / "unity_experiment.json"
    if not verify_provenance(provenance, run_tag, trainer_seed, config_id):
        raise FileNotFoundError(f"existing run has no readable Unity provenance: {provenance}")
    behavior = settings.get("behavior", "WarehouseRobot")
    configuration = run_dir / "configuration.yaml"
    status = run_dir / "run_logs/training_status.json"
    if not configuration.exists() or not status.exists():
        raise FileNotFoundError(f"existing run has no trainer completion record: {run_dir}")
    max_steps_match = re.search(r"(?m)^\s+max_steps:\s*(\d+)\s*$",
                                configuration.read_text(encoding="utf-8-sig"))
    if max_steps_match is None:
        raise ValueError(f"cannot verify max_steps in {configuration}")
    training_status = json.loads(status.read_text(encoding="utf-8-sig"))
    final_steps = training_status.get(behavior, {}).get("final_checkpoint", {}).get("steps", 0)
    if final_steps < int(max_steps_match.group(1)):
        raise ValueError(f"existing run is incomplete: {run_tag} ({final_steps} steps)")
    model = find_model(run_dir, behavior, run_tag)
    models_dir = rel_path(settings.get("models", "Assets/Models"))
    out = models_dir / f"{run_tag}{model.suffix.lower()}"
    if out.exists() and not filecmp.cmp(model, out, shallow=False):
        raise FileExistsError(f"copied model differs from the completed run: {out}")
    if dry_run:
        return out
    model = rename_model_to_run_id(model, run_dir, run_tag)
    if not out.exists():
        models_dir.mkdir(parents=True, exist_ok=True)
        shutil.copy2(model, out)
        print(f"recovered: {model} -> {out}", flush=True)
    else:
        print(f"already copied: {out}", flush=True)
    return out


def run_one(
    settings: dict[str, str], index: int, trainer_seed: int,
    *, run_id: str | None = None, config_id: str | None = None, dry_run: bool = False,
) -> Path:
    prefix = settings.get("prefix", "r")
    digits = int(settings.get("digits", "2"))
    run_tag = run_id or f"{prefix}{index:0{digits}d}"

    config = rel_path(settings.get("config", "warehouse_training.yaml"))
    env = rel_path(settings.get("env", "App/ML-warehouse.exe"))
    results_dir = rel_path(settings.get("results", "results")).resolve()
    models_dir = rel_path(settings.get("models", "Assets/Models"))
    behavior = settings.get("behavior", "WarehouseRobot")

    cmd = [
        settings.get("mlagents", "mlagents-learn"),
        str(config),
        "--run-id",
        run_tag,
        "--env",
        str(env),
        "--results-dir",
        str(results_dir),
        "--seed",
        str(trainer_seed),
    ]

    if as_bool(settings.get("no_graphics", "true"), True):
        cmd.append("--no-graphics")
    if as_bool(settings.get("force", "false"), False):
        cmd.append("--force")

    extra_args = split_args(settings.get("extra_args", ""))
    if any(arg == "--seed" or arg.startswith("--seed=") for arg in extra_args):
        raise ValueError("set trainer seeds with trainer_seeds, not extra_args")
    cmd += extra_args

    print(f"\n=== training {run_tag} (trainer seed {trainer_seed}, config {config_id or 'from Player'}) ===", flush=True)
    print(" ".join(f'"{x}"' if " " in x else x for x in cmd), flush=True)
    if dry_run:
        return models_dir / f"{run_tag}.onnx"
    child_env = dict(os.environ)
    child_env["MLAGENTS_RUN_ID"] = run_tag
    child_env["MLAGENTS_TRAINER_SEED"] = str(trainer_seed)
    child_env["WAREHOUSE_RESULTS_DIR"] = str(results_dir)
    if config_id is not None:
        child_env["WAREHOUSE_EXPERIMENT_CONFIG_ID"] = config_id
        provenance = results_dir / run_tag / "unity_experiment.json"
        verified = False
        process = subprocess.Popen(cmd, cwd=ROOT, env=child_env)
        try:
            while process.poll() is None:
                if not verified:
                    try:
                        verified = verify_provenance(provenance, run_tag, trainer_seed, config_id)
                    except ValueError:
                        process.terminate()
                        process.wait()
                        raise
                time.sleep(1)
            if process.returncode != 0:
                raise subprocess.CalledProcessError(process.returncode, cmd)
        except KeyboardInterrupt:
            process.terminate()
            process.wait()
            raise
    else:
        subprocess.run(cmd, cwd=ROOT, env=child_env, check=True)

    run_dir = results_dir / run_tag
    copy_legacy_provenance(env, run_dir, run_tag)
    if config_id is not None and not verify_provenance(
        run_dir / "unity_experiment.json", run_tag, trainer_seed, config_id
    ):
        raise FileNotFoundError(f"Unity provenance missing or unreadable for {run_tag}; model was not copied")
    model = find_model(run_dir, behavior, run_tag)
    model = rename_model_to_run_id(model, run_dir, run_tag)

    models_dir.mkdir(parents=True, exist_ok=True)
    out = models_dir / f"{run_tag}{model.suffix.lower()}"
    shutil.copy2(model, out)
    print(f"copied: {model} -> {out}", flush=True)
    return out


def main() -> int:
    parser = argparse.ArgumentParser(description="Run ML-Agents training repeatedly.")
    parser.add_argument("settings", nargs="?", default=str(DEFAULT_SETTINGS))
    parser.add_argument("--dry-run", action="store_true", help="Print planned runs without starting training")
    parser.add_argument("--resume-batch", action="store_true",
                        help="Reuse verified completed batch runs and train only missing runs")
    args = parser.parse_args()

    settings = read_settings(rel_path(args.settings))
    models_dir = rel_path(settings.get("models", "Assets/Models"))
    prefix = settings.get("prefix", "r")
    digits = int(settings.get("digits", "2"))
    runs = int(settings.get("runs", "1"))
    trainer_seeds = read_trainer_seeds(settings, runs)
    raw_ids = settings.get("experiment_ids", "").strip()
    if raw_ids:
        ids = [part for part in re.split(r"[,\s]+", raw_ids) if part]
        if len(set(ids)) != len(ids):
            raise ValueError("experiment_ids must not contain duplicates")
        if any(not re.fullmatch(r"[A-Za-z0-9_-]+", item) for item in ids):
            raise ValueError("experiment_ids must contain only letters, digits, _ and -")
        check_catalog_ids(ids)
        if as_bool(settings.get("force", "false")):
            raise ValueError("force=true is not allowed for experiment_ids batches")
        reserved = {"--force", "--run-id", "--env", "--results-dir"}
        if any(arg.split("=", 1)[0] in reserved for arg in split_args(settings.get("extra_args", ""))):
            raise ValueError("extra_args must not override batch run ID, Player, results directory, or force")
        if not rel_path(settings.get("env", "App/ML-warehouse.exe")).exists():
            raise FileNotFoundError("Player build is missing; build it after adding the config catalog")
        prefix = settings.get("prefix", "wh_")
        separator = settings.get("number_separator", "_")
        if separator not in {"_", "-"}:
            raise ValueError("number_separator must be _ or -")
        plans = [(batch_run_id(prefix, config_id, seed, digits, separator), config_id, seed)
                 for config_id in ids for seed in trainer_seeds]
        if len({run_id for run_id, _, _ in plans}) != len(plans):
            raise ValueError("batch generates duplicate run IDs")
        results_dir = rel_path(settings.get("results", "results"))
        completed: dict[str, Path] = {}
        for run_id, config_id, seed in plans:
            if (results_dir / run_id).exists():
                if not args.resume_batch:
                    raise FileExistsError(f"run ID already exists: {run_id}; use --resume-batch to reuse it")
                completed[run_id] = recover_finished_run(settings, run_id, seed, config_id, dry_run=True)
            elif any((models_dir / f"{run_id}{ext}").exists() for ext in (".onnx", ".nn")):
                raise FileExistsError(f"model exists without its results folder: {run_id}")
        copied = []
        for run_id, config_id, seed in plans:
            if run_id in completed:
                print(f"\n=== completed {run_id}: reuse verified model ===", flush=True)
                copied.append(completed[run_id] if args.dry_run else
                              recover_finished_run(settings, run_id, seed, config_id))
            else:
                copied.append(run_one(settings, 0, seed, run_id=run_id, config_id=config_id,
                                      dry_run=args.dry_run))
        print("\nDry run complete." if args.dry_run else "\nDone.")
        for path in copied:
            print(path)
        return 0
    if args.resume_batch:
        raise ValueError("--resume-batch requires experiment_ids")
    start = settings.get("start", "auto").lower()
    results_dir = rel_path(settings.get("results", "results"))

    index = next_index(models_dir, results_dir, prefix, digits) if start == "auto" else int(start)

    if args.dry_run:
        for offset, i in enumerate(range(index, index + runs)):
            run_one(settings, i, trainer_seeds[offset], dry_run=True)
        print("\nDry run complete.")
        return 0

    copied: list[Path] = []
    for offset, i in enumerate(range(index, index + runs)):
        copied.append(run_one(settings, i, trainer_seeds[offset]))

    print("\nDone.")
    for p in copied:
        print(p)
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except subprocess.CalledProcessError as e:
        print(f"training failed with exit code {e.returncode}", file=sys.stderr)
        raise SystemExit(e.returncode)
