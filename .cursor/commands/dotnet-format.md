# dotnet-format

リポジトリ内の .NET コードに `dotnet format` を適用し、EditorConfig に基づくコードフォーマットを統一する。

## 前提

- .NET SDK（`dotnet format` サブコマンド利用可能）がインストール済みであること。
- `dotnet/.editorconfig` が書式ルールの正本。

## 実行してほしいこと

リポジトリルートで実行する。

1. **インデント・空白**（`--folder` は MSBuild のワークスペース読み込みが不要で速い）:

   ```powershell
   dotnet format whitespace dotnet --folder --exclude "**/bin" "**/obj"
   ```

2. **不要な using の削除と配置の整理**:

   ```powershell
   dotnet format style dotnet/Melore.slnx --diagnostics IDE0005 IDE0065 --severity info
   ```

3. **チェックモード**（変更がないことを検証し、差分があれば exit 1）: 上の 2 コマンドに `--verify-no-changes` を付ける。
4. フォーマット後、変更があればユーザーへ要約を報告する。
5. 明示的に依頼された場合を除き、フォーマット結果のコミットは行わない。

編集後のフックは、編集した 1 ファイルの whitespace だけを処理する。using の整理はこのコマンドで行う。

## ユーザー向け短文（チャットへの貼り付け用）

リポジトリ内の .NET コードに `dotnet format` を適用し、EditorConfig に基づくフォーマットを統一してください。
