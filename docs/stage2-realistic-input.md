# 現実的な混在省略入力への徹底対応（Stage 1–3）

実打鍵は「母音などの省略がモーラ単位でバラバラに混在」する。本書は (1) その混在を決定的にモデル化し測定可能にし、(2) 外来小書き音・打鍵癖・長音記号を索引で正しく扱えるようにした記録。プラン: `~/.claude/plans/jiggly-brewing-biscuit.md`。

## 設計の背骨：母音を MANDATORY / OPTIONAL に二分

各母音は OPTIONAL（子音モーラの末尾母音＋確信長音 おう/うう＝[ConsonantEncoder.IsLongVowel](../src/ShortcutIme.Core/ConsonantEncoder.cs) が落とす厳密に2種のみ）か MANDATORY（それ以外の母音単独モーラ）。保持率 `p` は OPTIONAL のみに適用。

効果：**`mixed(p=0) ≡ ConsonantEncoder`、`mixed(p=1) ≡ RomajiEncoder` が構成的に成立**（property test で担保）。混在は両端の真の補間。一様 Bernoulli は**合成的仮定**（実打鍵分布そのものではない）。

## Stage 1：混在エンコーダ＋スペクトラム測定（ランタイム改修ゼロ）

- [MoraKeystrokeWalker](../src/ShortcutIme.Core/MoraKeystrokeWalker.cs)：モーラ走査・促音重ね・長音判定の単一 primitive。`ConsonantEncoder`/`RomajiEncoder` は `keepOptionalVowel` 述語（`_=>false` / `_=>true`）を渡す薄ラッパに（既存テスト無改修で緑＝リファクタ正当性）。
- [MixedVowelEncoder](../src/ShortcutIme.Evaluation/MixedVowelEncoder.cs)：`hash(seed, reading, optionalVowelIndex) < keepRate` で決定的に保持判定（反復順非依存・完全再現）。`keepRate≤0/≥1` は短絡で両端に厳密一致。
- [InputFaithfulnessAuditor](../src/ShortcutIme.Evaluation/InputFaithfulnessAuditor.cs)：`IsFaithful`（異形の母音削除形か）＋ `IsReachable`（トライで gold へ到達＝真の真実源）。
- `EvaluationHarness` に keepRate 軸（`EvalInputMode` は端点の特殊点として委譲）。Eval ツールに `spectrum` サブコマンド。
- property test：両端構成一致・決定性・`mixed(p)⊆mixed(p')` 単調部分列・全混在入力の忠実性/到達性。

## Stage 2：外来小書き音 ＋ ゔ（致命バグ修正）

[KanaRomanization](../src/ShortcutIme.Core/KanaRomanization.cs) に `ForeignMora` 表（ふぁ=fa, てぃ=thi, とぅ=twu, うぃ=wi, でぃ=dhi, つぁ=tsa, しぇ=sye/she …）と `ゔ`=vu を追加。`MoraSplitter` は無改修（既に2文字モーラに結合済み）。ち=ti・つ=tu・ぢ=di（訓令式）との衝突を避けるため てぃ→thi・でぃ→dhi 等。
- 効果：ファイル(ふぁいる)が "fuiru" でなく正しく "fairu" 索引に。**旧「ゔ」不変条件違反2件が解消**。

## Stage 3：打鍵癖（を→o, ん→nn）＋ 長音「ー」（脱落/延長）

- **ー は癖でなく base**：[RomajiVariants.BuildSlots](../src/ShortcutIme.Core/RomajiVariants.cs) で文脈（直前母音）から `["", 直前母音]`＝脱落形/延長形。こーひー→kohi/koohii 両対応。
- **癖は augmentation 層**：`ExpandReadingWithHabits` が scheme 展開の後ろに を→o・ん→nn を予算内で足す。`Slot` レコードでアンカー識別（一貫方式・癖なし・ー脱落）を全異形と分離 → cap 超過時は `ExpandReading` へ退避し**アンカーを絶対失わない（非劣化）**。
- 本番トライ（Eval/App）を `ExpandReadingWithHabits` へ切替（索引時＝検索時改修なし・latency 無影響・こな誤マッチ無し）。

### 索引時の判断（advisor 助言）
ん→nn は索引時（検索時でなく）：matcher 純度・latency・精度（こな≠konna）のため。memory ゲート ~180MB。

### メモリ実測（DataGen, 全1.29M エントリ）
| トライ | 1語平均異形 | 常駐 | blob |
|---|---|---|---|
| 単一方式（訓令式） | 1.0 | ~95MB | — |
| 多方式（混在直積） | 1.71 | ~129MB | — |
| **多方式＋癖（本番）** | **2.94** | **~185MB** | 177MB（ロード ~750ms） |

ん→nn が遍在で駆動（cap=64 退避 35,977 語）。[[compact-dictionary-dsa]] の130MBから増加。将来 ん→nn を per-position でなく per-word global toggle 化すれば ×2^k→×2 に圧縮可能（現状は実打鍵の混在 nn を忠実に再現する per-position）。

### 監査（全エントリ・2方式＝258万検査）
子音⊆フル違反 **0**／一貫フル欠落 **0**／非劣化違反 **0**／operational 到達不能 **0**（を/ん 含む 1,341 サンプル）。

## スペクトラム測定（identity・n=100・seed=0・本番トライ）

両端は歴史的数値と一致（＝ベンチ中立）。中間が実打鍵の混在を初めて可視化。

### seed（32文・丁寧長文）
| keepRate | Kunrei top-1 | gold∈n | Hepburn top-1 | gold∈n |
|---|---|---|---|---|
| 0 (Consonant) | 47% | 75% | 47% | 78% |
| 0.25 | 59% | 75% | 59% | 81% |
| 0.50 | 66% | 78% | 62% | 81% |
| 0.75 | 66% | 81% | 66% | 81% |
| 1 (Full) | 72% | 81% | 72% | 81% |

### generated（400文・テンプレ・活用多）
| keepRate | Kunrei top-1 | gold∈n | Hepburn top-1 | gold∈n |
|---|---|---|---|---|
| 0 (Consonant) | 17% | 47% | 15% | 49% |
| 0.25 | 23% | 61% | 22% | 62% |
| 0.50 | 32% | 67% | 33% | 66% |
| 0.75 | 39% | 67% | 39% | 67% |
| 1 (Full) | 49% | 67% | 51% | 67% |

## 含意
- 混在対応は生成器（出題側）と照合器（解答側）の両方に入るため**ベンチ top-1 はほぼ中立**（seed ±1〜2文＝ノイズ、generated 端点は docs と一致）。真の成果は「その打ち方のユーザーが成功する」現実忠実性（外来音・癖・長音）＝ベンチに映らない。
- **generated の gold∈n-best が 67% で頭打ち**（seed 81%）＝**活用合成の穴**＝Stage 4（精度の主レバー）の標的。

## 再現
```powershell
mise exec -- dotnet test tests/ShortcutIme.Core.Tests/ShortcutIme.Core.Tests.csproj -c Release        # 137
mise exec -- dotnet test tests/ShortcutIme.Evaluation.Tests/ShortcutIme.Evaluation.Tests.csproj -c Release # 39
mise exec -- dotnet run -c Release --project tools/ShortcutIme.Eval -- data/dictionary_oss data/eval/seed.tsv 100 spectrum 0
mise exec -- dotnet run -c Release --project tools/ShortcutIme.Eval -- data/dictionary_oss data/eval/generated.tsv 100 spectrum 0
mise exec -- dotnet run -c Release --project tools/ShortcutIme.DataGen -- data/dictionary_oss 20000   # メモリ・監査
```
