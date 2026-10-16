# 16Agent・SubtaskSeparated 学習と推論 (v1)

記録日: 2026-10-15

## 命名と条件

- 学習時の `tr.txt` に `wh_middle2Gate_ST_six_v1_` というprefixを指定したが、`six` は誤記。
  **実際は各環境16Agent**で学習した。元のrun IDは学習記録との対応を守るため改名しない。
- 対象runは `wh_middle2Gate_ST_six_v1_01`～`03`。trainer seedは順に1、2、3。
- 学習記録では6環境、各16Agent、合計96Agent。使用Configは `STseparated_v1`、
  Environment Presetは `Middle_2Gate_v1`。フェロモンは `SubtaskSeparated`、`Scalar`、`LegacyScalar9`。
- Configアセットの `agentCount`、各runの `unity_experiment.json`、テスト時の
  `test_experiment.json` の実効Agent数がいずれも16で一致した。
- 元データは `results/wh_middle2Gate_ST_six_v1_{01,02,03}/`。`results/` はGit管理外のため、
  主要数値を以下にも記録する。

## 推論結果

各最終ONNXを、学習記録からConfigを自動選択し、決定的推論、1環境 x 16Agent、
trial seed 1、固定シミュレーション時間300秒 x 各1試行でテストした。

| 旧run末尾 | 完了タスク | tasks/分 | 壁衝突 | 棚衝突 | Agent衝突 | 記録衝突合計 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| `_01` | 350 | 70.0 | 485 | 422 | 606 | 1,513 |
| `_02` | 356 | 71.2 | 494 | 370 | 600 | 1,464 |
| `_03` | 316 | 63.2 | 445 | 305 | 578 | 1,328 |

- 3モデルとも300秒試行を完了し、Config照合に成功した。達成数と衝突数は各runの
  `logTest.csv` と `test_experiment.json` から確認した。
- Agent衝突は両Agentで計上され得るため、上表は物理的な接触イベント数ではない。
- 16Agent条件は既存の6Agent条件とAgent密度もマップ分割方式も異なる。上表だけから
  フェロモン方式の優劣や混雑回避の効果は判断できない。各モデルの推論はまだ1試行。

## 次の扱い

- この3runを「6Agentで学習した結果」として引用しない。旧run IDと実効16Agentを併記する。
- 6Agentの `SubtaskSeparated` を調べるときは、既存Configを上書きせず、
  `agentCount = 6` の新Config・新run IDを用意してPlayerを再ビルドし、再学習する。
- 接触判定・安全停止・報酬を変える比較では、旧16Agent結果を旧ルール下の参考値として扱う。
