# ShortcutIme — 子音ショートカット日本語入力アシスト

[![CI](https://github.com/P4suta/shortcut-ime-poc/actions/workflows/ci.yml/badge.svg)](https://github.com/P4suta/shortcut-ime-poc/actions/workflows/ci.yml)
[![CodeQL](https://github.com/P4suta/shortcut-ime-poc/actions/workflows/codeql.yml/badge.svg)](https://github.com/P4suta/shortcut-ime-poc/actions/workflows/codeql.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![WinUI 3](https://img.shields.io/badge/WinUI-3-0078D4)](https://learn.microsoft.com/windows/apps/winui/winui3/)

子音だけ（または子音＋任意の母音）で日本語を高速入力する**入力補助ツール**。
中国語ピンインの「簡拼（声母だけ入力 → 候補選択）」を日本語でやる試み。

```
kyy   → 共有 / 許容 …      （子音だけ）
kyou  → 今日 / 共有 …      （母音を足して絞り込み）
hnjt  → 本日               （文節ごとに補完して長文をつなぐ）
```

単発の単語より、**長文をテンポよく書く**ことに価値を置く（"本日はお忙しい中…" のような文）。

## 仕組み

- **入力 → ローマ字トライ走査**：各候補の読みをフルローマ字（きょう→`kyou`）でトライに格納し、
  検索時は**子音は必須・母音はオプション**で走査する。だから `ky` でも `kyou` でも、その中間でも引ける
  （中国語簡拼の `bj` / `beijing` と同じスペクトラム）。混んだ短いキーは母音を足して絞る。
- **ランキング**：Mozc 辞書の単語生起コスト（小さいほど高頻度）昇順。
- **学習（recency）**：一度選んだ語を次回から上位へ（JSON 永続化）。子音だけでは絞れない短い語
  （今日・愛など）を実使用で浮上させる。

設計の肝はエンコードではなく「ランキング＋段階的絞り込み」。詳細は
[docs/step0-measurement.md](docs/step0-measurement.md)（実データでの衝突率・絞り込み効果の計測）参照。

## アーキテクチャ

純粋ロジックを UI 非依存の `Core` に隔離し、WinUI はその上の薄い殻にしている。

```
src/
  ShortcutIme.Core/   かな→ローマ字、辞書ローダ、母音オプション・トライ、学習、ImeEngine（UI非依存）
  ShortcutIme.App/    WinUI 3：補完入力 UI（MVVM, CommunityToolkit.Mvvm）
tests/
  ShortcutIme.Core.Tests/   xUnit（33 ケース）
tools/
  ShortcutIme.Measure/      ステップ0 計測コンソール
data/
  dictionary_oss/           Mozc 辞書（取得物）
```

主な型（`Core`）：`KanaRomanization` → `RomajiEncoder` / `ConsonantEncoder`（`IReadingEncoder`）、
`MozcDictionaryReader`、`RomajiTrie`、`LearningStore`、`ImeEngine`（ファサード）。

## 前提・環境

- .NET 10 SDK（このリポジトリは [mise](https://mise.jdx.dev/) で管理：`mise install` → `mise exec -- dotnet …`）
- WinUI 3 実行には **Windows の Developer Mode** が必要（Settings → System → For developers）
- `winapp` CLI（`winget install Microsoft.WinAppCLI`）

## ビルド・テスト・実行

```bash
mise exec -- dotnet test                              # Core のユニットテスト
mise exec -- dotnet run --project tools/ShortcutIme.Measure -c Release -- data/dictionary_oss   # 計測
pwsh BuildAndRun.ps1 src/ShortcutIme.App/ShortcutIme.App.csproj                                  # アプリのビルド＆起動（winui スキル）
```

## 使い方（v1）

1. 入力欄に子音／ローマ字を打つ（例 `kyy`）。
2. 候補が出る → Enter で先頭確定、クリックで任意の候補を確定。
3. 確定語が「確定文」に連結される。次の文節を打って繰り返し、長文を作る。
4. 「コピー」で確定文をクリップボードへ → 他アプリに貼り付け。

または **Ctrl+Alt+Space** でどのアプリからでも本アプリを呼び出し（直前のアプリを記憶）→ 子音で文を作り → 「送信」で直前のアプリへ貼り付け注入。

## ロードマップ

- **v1**：Core ＋ 補完 UI（文節ごと補完で長文）＋ クリップボード連携 ＋ グローバルホットキー（Ctrl+Alt+Space）召喚と他アプリへの貼り付け注入。
  - 注入のクリップボード経路は自動検証済み。実他アプリへの貼り付けは `SetForegroundWindow` のフォーカス制約が出る場合があり、**手動確認を推奨**（出る場合は前面化処理の調整が必要）。
- **将来**：区切りなしで一気に打って文節を自動分割する連文節変換（Mozc 連接コスト lid/rid ＋ ビタビ）。TSF による本物の IME 化。

## 辞書・ライセンス（要確認）

- 辞書＝Mozc `dictionary_oss`（読み→候補＋コスト）。BSD-3-Clause ＋ 寛容 IPADIC ＋ 沖縄パブリックドメインで
  商用フレンドリー。配布前にコンポーネントごとのライセンスを必ず確認すること。
- 本リポジトリのコードと辞書データは別ライセンス。詳細は各上流を参照。
