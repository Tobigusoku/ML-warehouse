from __future__ import annotations

import argparse
import os
import re
import shutil
import subprocess
import sys
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


def find_model(run_dir: Path, behavior: str) -> Path:
    preferred = [
        run_dir / f"{behavior}.onnx",
        run_dir / f"{behavior}.nn",
    ]
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


def run_one(settings: dict[str, str], index: int) -> Path:
    prefix = settings.get("prefix", "r")
    digits = int(settings.get("digits", "2"))
    run_tag = f"{prefix}{index:0{digits}d}"

    config = rel_path(settings.get("config", "warehouse_training.yaml"))
    env = rel_path(settings.get("env", "App/ML-warehouse.exe"))
    results_dir = rel_path(settings.get("results", "results"))
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
    ]

    if as_bool(settings.get("no_graphics", "true"), True):
        cmd.append("--no-graphics")
    if as_bool(settings.get("force", "false"), False):
        cmd.append("--force")

    cmd += split_args(settings.get("extra_args", ""))

    print(f"\n=== training {run_tag} ===", flush=True)
    print(" ".join(f'"{x}"' if " " in x else x for x in cmd), flush=True)
    child_env = dict(os.environ)
    child_env["MLAGENTS_RUN_ID"] = run_tag
    subprocess.run(cmd, cwd=ROOT, env=child_env, check=True)

    run_dir = results_dir / run_tag
    model = find_model(run_dir, behavior)
    model = rename_model_to_run_id(model, run_dir, run_tag)

    models_dir.mkdir(parents=True, exist_ok=True)
    out = models_dir / f"{run_tag}{model.suffix.lower()}"
    shutil.copy2(model, out)
    print(f"copied: {model} -> {out}", flush=True)
    return out


def main() -> int:
    parser = argparse.ArgumentParser(description="Run ML-Agents training repeatedly.")
    parser.add_argument("settings", nargs="?", default=str(DEFAULT_SETTINGS))
    args = parser.parse_args()

    settings = read_settings(rel_path(args.settings))
    models_dir = rel_path(settings.get("models", "Assets/Models"))
    prefix = settings.get("prefix", "r")
    digits = int(settings.get("digits", "2"))
    runs = int(settings.get("runs", "1"))
    start = settings.get("start", "auto").lower()
    results_dir = rel_path(settings.get("results", "results"))

    index = next_index(models_dir, results_dir, prefix, digits) if start == "auto" else int(start)

    copied: list[Path] = []
    for i in range(index, index + runs):
        copied.append(run_one(settings, i))

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
