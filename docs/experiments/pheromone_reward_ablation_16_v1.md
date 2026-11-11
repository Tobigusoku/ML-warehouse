# 16Agent・毎ステップ分泌におけるフェロモン報酬の有無

## 2026-11-09: 報酬なし3モデルの推論結果

### 目的と条件

`STseparated_noRw_v1` の推論結果を、既存の報酬あり `STseparated_v4` と比較する。
両Configは `Middle_2Gate_v2`、16Agent、前方19方向観測、Scalarのサブタスク別マップを使う。
Configアセットのフィールド差はID・名前と `pheromoneRewardScale`（0 と 0.0002）。

両条件ともtrainer seed 1～3、推論seed 1～5、決定的推論、固定300シミュレーション秒。
各 `test_experiment.json` で学習時Configとの照合は一致した。
ただし学習の `configuration.yaml` は `num_envs` が報酬なし2、報酬ありv4は1。
この差があるため、報酬だけの厳密なアブレーションとは言えない。

### 観測された事実

各値は推論seed 5件の平均。Agent接触はペアイベント数。

| trainer seed | 報酬なし 完了数 | 報酬ありv4 完了数 | 報酬なし Agent接触 | 報酬ありv4 Agent接触 |
| --- | ---: | ---: | ---: | ---: |
| 1 | 386.6 | 408.0 | 173.6 | 119.4 |
| 2 | 374.6 | 372.2 | 162.8 | 184.2 |
| 3 | 376.4 | 376.4 | 166.2 | 164.2 |
| 3 seed平均 | 379.2 | 385.5 | 167.5 | 155.9 |

平均棚衝突は報酬なし392.9、報酬ありv4は451.5。報酬なしの15試行中1試行は
壁時計の安全タイムアウトで298.86シミュレーション秒で終了し、残り14試行は300秒。
報酬ありv4の15試行はすべて300秒で終了した。

元データ:

- 報酬なし: `results/wh_middle2Gate_ST_16_noRw_v1_01/` ～ `_03/` の
  `logTest.csv`、`logTest_summary.csv`、`test_experiment.json`、`unity_experiment.json`、`configuration.yaml`
- 報酬あり: `results/wh_middle2Gate_ST_16_v4_01/` ～ `_03/` の同名ファイル

### 解釈と次の検証

報酬なしでも平均約379件の搬送ができ、報酬ありv4との差はseed 1で大きく、
seed 2・3では小さい。3 seed平均の差6.3件からフェロモン報酬の有効性や不要性は断定できない。
学習時の並列Player数も違うため、差を報酬の因果効果とみなさない。

次は同じ新Player・同じ `num_envs=2`・同じseed集合で
`STseparated_successRoute_noRw_v1` と `STseparated_successRoute_v1` を学習・推論する。
前者と既存の報酬なし条件は分泌時期の比較に、成功経路方式の2条件は報酬有無の比較に使う。
成功経路の報酬ありと旧v4の直接比較には `num_envs` の違いを明記する。
