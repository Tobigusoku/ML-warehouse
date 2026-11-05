# 24Agent・None / SubtaskSeparated + Scalar 比較

確認日: 2026-11-05。

## 目的・条件

- 同じ24Agent倉庫で、フェロモンなし（None）と
  `SubtaskSeparated + Scalar`（ST）の最終方策を比較する。
- None: `None_v3`、`wh_middle2Gate_none_24_v1_01`～`03`。
  ST: `STseparated_v5`、`wh_middle2Gate_ST_24_v1_01`～`03`。
  Configアセットの公開フィールドは名前・ID・`pheromoneMode` 以外同じ。
  倉庫Presetは両方 `Middle_2Gate_v2` で、学習記録のPreset内容ハッシュも一致。
- PPO 5,000万step、trainer seed 1～3。各Unityプロセスには6環境 x 各24Agent。
  `configuration.yaml` の `num_envs` はNoneの3runとSTのseed 2・3が2、
  STのseed 1は1（途中から再開）。この違いは比較上の制約とする。
- 各最終ONNXを1環境 x 24Agent、決定的推論、固定シミュレーション300秒 x
  trial seed 1～5で評価。6件の `test_experiment.json` は
  `AutoFromTrainingRecord`、実効24Agent、学習Config・環境Presetの内容ハッシュ一致を記録。
  全30試行は300秒の `simulation_duration_target` で終了した。
- 元データ: `results/wh_middle2Gate_none_24_v1_01`～`03`、
  `results/wh_middle2Gate_ST_24_v1_01`～`03` の `configuration.yaml`、
  `unity_experiment.json`、`test_experiment.json`、`logTest.csv`、
  `logTest_summary.csv`。`results/` はGit管理外。
  16体の参照結果は [3方式比較](pheromone_mode_front_dynamic_v1.md)。

## 観察された事実

Agent接触は同一ペアの継続接触を1件とする開始イベント数。
表のseed別の値は各5試行平均、全体値は各15試行平均。

| trainer seed | None完了 | ST完了 | ST−None | None接触 | ST接触 |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 555.6 | 454.6 | -101.0 | 317.2 | 361.4 |
| 2 | 546.4 | 536.2 | -10.2 | 339.0 | 354.8 |
| 3 | 244.4 | 506.2 | +261.8 | 182.6 | 323.8 |
| **15試行平均** | **448.8** | **499.0** | **+50.2** | **279.6** | **346.7** |

| 指標（300秒あたり） | None | ST |
| --- | ---: | ---: |
| 完了数 / Agent | 18.70 | 20.79 |
| Agent接触 / 完了 | 0.623 | 0.695 |
| Agent非接触完了率 | 63.6% | 61.3% |
| 壁接触 | 181.7 | 167.5 |
| 棚接触 | 642.4 | 660.4 |

STの平均完了数はNoneより50.2件（11.2%）多いが、trainer seed 1・2では
STのほうが少ない。seed 3ではNoneが244.4件（各試行132～298件）で、
STの506.2件（490～519件）との差が平均を大きく動かしている。
Agent接触はSTが全trainer seedで多く、15試行平均では67.1件（24.0%）多い。
接触/完了も0.623→0.695、Agent非接触完了率は63.6%→61.3%。

参考として16体のNone/STは平均完了371.7/385.5、
Agentあたり完了23.23/24.10。24体では総完了数が両方式とも増えたが、
Agentあたり完了数はNoneで18.70、STで20.79へ減った。
16体から24体への比較はそれぞれ別方策の学習結果であり、
同じONNXのAgent数変更による転移評価ではない。

## 解釈と限界

- この3 trainer seedでは、STが24体で安定してNoneより多く搬送したとは言えない。
  平均の+11.2%は、特にNoneのseed 3の低い結果に影響される。
  一方、Agent接触はSTの全seedで多く、接触指標での改善は観察されない。
- Noneではフェロモン観測9値と追加フェロモン報酬がともにゼロ、STでは両方有効。
  この比較だけで履歴情報共有と追加報酬の寄与は分離できない。
- ST seed 1の学習時 `num_envs` は他の5runと異なる。
  同じtrainer seed番号でも学習軌跡が同一になるわけではない。
  16体と24体の学習時並列Player数も異なるため、台数だけの因果効果は断定しない。
- 接触位置、実停止時間、ゲート滞留、判断時に読めたフェロモン値は未計測。
  接触増加を混雑悪化の直接証拠、完了数の差をフェロモン利用の証拠とはしない。

## 次の検証

1. Noneのseed 3の挙動と学習ログを調べ、低い完了数が再現するか追加seedで確認する。
2. 接触の位置・停止時間・ゲート滞留と、フェロモン再訪時の残存量を計測する。
3. `pheromoneRewardScale = 0` のSTを別Configで学習し、観測と追加報酬を切り分ける。
