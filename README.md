# ML-Warehouse

A Unity project showcasing a multi-agent warehouse robot simulation trained with Unity ML-Agents.

## 🚀 Overview

In this environment, autonomous robots learn to pick up cargo from a warehouse entrance, deliver it to designated shelves, and return to the exit. The project supports both single-agent and multi-agent setups.

The key features include:

- Procedural warehouse generation (`WarehouseGenerator`).
- Configurable robot agents (`WarehouseRobotAgent`) with physics-based movement, raycast sensors, and reward shaping.
- Training manager (`WarehouseTrainingManager`) that handles spawning, target assignment, and multi-agent coordination.
- Optional curiosity-driven training configuration.

## 🧩 Project Structure

```
Assets/
  Scripts/          # C# components for generators, agents, training manager, debug HUD, etc.
warehouse_training.yaml            # PPO training config
warehouse_training_curiosity.yaml  # PPO config with curiosity module
```

## 🛠️ Prerequisites

- Unity (tested with 2021.3 LTS or later).
- [Unity ML-Agents](https://github.com/Unity-Technologies/ml-agents) package installed in the project (via `Window > Package Manager`).
- Python 3.8+ environment to run the training CLI (`mlagents-learn`).

## 📁 Getting Started

1. **Open the Project**
   - Launch Unity and open the `ML-warehouse` project folder.
   - Load the example scene (e.g. `Assets/Scenes/WarehouseScene.unity`).

2. **Configure the Scene**
   - Place a `WarehouseGenerator` prefab in the scene and adjust width/depth/doors as desired.
   - Add one or more `WarehouseRobotAgent` prefabs or allow the `WarehouseTrainingManager` to spawn them automatically.
   - Set behavior name to `WarehouseRobot` in the `Behavior Parameters` component.

3. **Run Training**
   - Open a terminal (PowerShell/Command Prompt) and navigate to the project root, e.g.:
     ```powershell
     cd "c:\Users\ban60\Documents\research\Unity\ML-warehouse"
     ```
   - Create or activate a Python 3.8+ virtual environment and install ML-Agents if you haven't already:
     ```powershell
     python -m venv .venv
     .\.venv\Scripts\activate
     pip install mlagents
     ```
   - Optional: install TensorBoard for monitoring:
     ```powershell
     pip install tensorboard
     ```
   - Choose a configuration file (plain or curiosity-enhanced) and launch the trainer. When running the environment in the **Unity Editor** you can simply run:
     ```powershell
     mlagents-learn warehouse_training.yaml --run-id=warehouse_run_01
     # or with curiosity:
     mlagents-learn warehouse_training_curiosity.yaml --run-id=warehouse_curiosity_01
     ```
     The trainer will then wait for the Editor to enter Play mode.

   - If you want to train using a built executable (located under `App/`), specify it with `--env`:
     ```powershell
     mlagents-learn warehouse_training.yaml --run-id=warehouse_run_01 --env="App/ML-warehouse.exe"
     ```
     (omit `--env` when launching from Editor.)
   - When the terminal output shows **"Waiting for Unity environment"**, switch to the Unity Editor and press **Play**.
   - While training runs, you can launch TensorBoard to visualize metrics:
     ```powershell
     tensorboard --logdir results
     ```
   - Training can be stopped with Ctrl+C in the terminal; the `.nn` model will be saved under `results/<run-id>/`.

4. **Inference / Playback**
   - After training, set the `Behavior Parameters` to use the exported `.nn` model (e.g. `Assets/Models/myModel.nn`).
   - Disable `train` flags or run a build to observe the trained agents executing the task.


### 🏭 Running as a Stand‑alone App (Training or Inference)

If you prefer to train or evaluate outside the Editor (for better performance or automation), build a headless player:

1. Open **File > Build Settings** and add the scene(s) you want to use.
2. Choose a target platform (Windows/Mac/Linux) and enable **Development Build** if you want logs.
3. For headless training, enable **Run In Background** and **Batch Mode** (set via command line `-batchmode -nographics`).
4. Build the project; the output executable will be placed under the `App/` folder.
5. Launch it from a terminal. Example command:
   ```powershell
   .\App\MLWarehouse.exe -batchmode -nographics -logFile train.log
   ```
6. Start the Python trainer as usual with `mlagents-learn` and it will connect automatically.
7. You can also run inference by omitting `--train` and providing a model at startup or via configuration.

> ⚠️ Remember to set the behavior name to `WarehouseRobot` in the built player or use command-line overrides.

This setup is useful for headless cloud runs, continuous integration, or long experiments.

## 🧪 Training Configurations

- `warehouse_training.yaml`: PPO baseline hyperparameters.
- `warehouse_training_curiosity.yaml`: Adds curiosity reward signal for exploration.

Adjust parameters like `batch_size`, `learning_rate`, `max_steps`, and reward weights to experiment.

## 📊 Results

Training results are stored under the `results/` directory with subfolders per run (e.g. `warehouse_robot_01`). Use TensorBoard to visualize.

```bash
tensorboard --logdir results
```

## 📝 Notes & Tips

- The `WarehouseTrainingManager` automatically assigns unique shelves to agents when `avoidDuplicateShelves` is enabled.
- Use the debug HUD (`WarehouseDebugHUD`) to inspect observations, actions, and agent state during training.
- You can manually place shelf units and alter reward parameters at runtime for rapid prototyping.

## 🧠 Extending the Environment

- Add new sensors or observations by modifying `WarehouseObservations`.
- Customize agent actions to include picking up/dropping crates or interacting with doors.
- Implement new tasks, e.g., sorting, charging stations, or adversarial agents.

## 📄 License

This project is provided for research and educational purposes. Adjust license as appropriate.

---

_Feel free to fork and adapt for your own ML‑Agents experiments!_