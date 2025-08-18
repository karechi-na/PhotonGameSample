# PhotonGameSample プロジェクト概要

Unity + Photon Fusion (Shared Mode) による2プレイヤー用マルチプレイヤーゲームのサンプルプロジェクトです。

## 📚 目次

- [プロジェクト概要](#プロジェクト概要)
- [ゲームの流れ](#ゲームの流れ)
- [アーキテクチャ設計](#アーキテクチャ設計)
- [主要コンポーネント](#主要コンポーネント)
- [イベントシステム](#イベントシステム)
- [ネットワーク機能](#ネットワーク機能)
- [ゲーム再開システム](#ゲーム再開システム)
- [ServiceRegistry（依存解決システム）](#serviceregistry依存解決システム)
- [開発・拡張ガイド](#開発拡張ガイド)

## プロジェクト概要

このプロジェクトは、UnityとPhoton Fusionを使用したマルチプレイヤーゲームの実装例です。2人のプレイヤーがアイテムを収集し、スコアを競うゲームとなっています。

### 主な特徴

- **Photon Fusion Shared Mode**: リアルタイムマルチプレイヤー通信
- **安定したプレイヤーID管理**: プレイヤー1/2の固定ID割り当て
- **ハードリセット機能**: 完全なゲーム状態リセット
- **イベント駆動アーキテクチャ**: 疎結合な設計
- **ServiceRegistry**: 依存関係の効率的な管理

### 技術スタック

- Unity 2022.3 LTS以上
- Photon Fusion
- C# (.NET Standard 2.1)

## ゲームの流れ

### 1. ゲーム開始フロー

1. **アプリケーション起動**
   - `GameLauncher`がPhoton Fusionの初期化を実行
   - NetworkRunnerを生成し、Shared Modeでセッション開始

2. **プレイヤー参加**
   - プレイヤーがセッションに参加
   - `NetworkGameManager`が安定したプレイヤーID（1または2）を割り当て
   - プレイヤーアバターをネットワーク上にスポーン

3. **ゲーム準備**
   - 2人のプレイヤーが揃うとカウントダウン開始
   - `GameSyncManager`がカウントダウンを全クライアントに同期

4. **ゲームプレイ**
   - プレイヤーがアイテムを収集してスコアを獲得
   - リアルタイムでスコアが同期・表示

5. **ゲーム終了**
   - 全アイテム収集でゲーム終了
   - 勝者判定とメッセージ表示
   - 再開待ち状態に移行

### 2. ゲーム再開フロー

1. **再開クリック**
   - 両プレイヤーが再開ボタンをクリック
   - `GameSyncManager`がハードリセットRPCを送信

2. **ハードリセット実行**
   - 全NetworkObjectをDespawn
   - NetworkRunner.Shutdown()実行
   - ServiceRegistryとGameEventsをクリア
   - ブートストラップシーンを再ロード

3. **ゲーム再初期化**
   - 新しいGameLauncherがRunner.StartGame()実行
   - アイテムシーンの再ロードと再カウント
   - プレイヤーの再スポーンとゲーム開始

## アーキテクチャ設計

### 設計原則

1. **責任分離**: 各クラスが明確な責任を持つ
2. **疎結合**: イベントシステムによる間接的な連携
3. **ネットワーク対応**: Fusion RPCによる状態同期
4. **拡張性**: 新機能追加が容易な構造

### コンポーネント構成

```
GameController (ゲーム状態管理)
├── NetworkGameManager (ネットワーク管理)
├── PlayerManager (プレイヤー管理)
├── ItemManager (アイテム管理)
├── GameUIManager (UI管理)
├── GameRuleProcessor (ルール処理)
└── GameSyncManager (同期管理)
```

## 主要コンポーネント

### GameController
ゲーム全体の状態管理を担当する中核コンポーネント。

**主な責務:**
- ゲーム状態の管理（WaitingForPlayers, Countdown, InGame, GameOver）
- プレイヤー数の監視とゲーム開始判定
- ゲーム終了処理とリスタート制御

**重要なメソッド:**
- `CheckPlayerCountAndUpdateGameState()`: プレイヤー数に基づく状態更新
- `StartGameCountdown()`: ゲーム開始カウントダウン
- `EndGame()`: ゲーム終了処理
- `RestartGame()`: ゲーム再開処理

### NetworkGameManager
ネットワーク関連の処理を一元管理。

**主な責務:**
- プレイヤーの接続・切断処理
- プレイヤーアバターのスポーン管理
- 安定したプレイヤーID（1/2）の割り当て
- シーン管理（メインシーン + アイテムシーン）

**重要な機能:**
- `assignedPlayerIds`: PlayerRefから安定IDへのマッピング
- `SpawnPlayerAfterDelay()`: 重複防止付きプレイヤースポーン
- `HardResetRoutine()`: 完全なゲーム状態リセット

### PlayerManager
プレイヤーアバターの登録・管理を担当。

**主な責務:**
- プレイヤーアバターの辞書管理
- スコア変更の監視と通知
- 勝者判定ロジック
- プレイヤー位置のリセット

### ItemManager
ゲーム内アイテムの管理システム。

**主な責務:**
- 静的アイテムのカウントと管理
- アイテム収集の処理
- ゲーム再開時のアイテムリセット
- 全アイテム収集の検出

### GameSyncManager
クライアント間のゲーム状態同期を担当。

**主な責務:**
- カウントダウンの同期
- ゲーム状態変更の同期
- プレイヤー入力状態の同期
- ハードリセットの実行

**主要RPC:**
- `RPC_NotifyCountdownUpdate()`: カウントダウン同期
- `RPC_NotifyGameStateChanged()`: ゲーム状態同期
- `RPC_NotifyHardReset()`: ハードリセット実行

### PlayerAvatar
個々のプレイヤーキャラクターの制御。

**主な責務:**
- プレイヤーの移動とアニメーション
- アイテム収集処理
- スコア管理とネットワーク同期
- 入力制御

**ネットワーク同期プロパティ:**
- `NickName`: プレイヤー名
- `playerId`: 安定したプレイヤーID
- `Score`: スコア（OnChangedRender付き）

## イベントシステム

### GameEvents
ゲーム全体で使用される静的イベントクラス。

**主要イベント:**
- `OnGameStateChanged`: ゲーム状態変更
- `OnPlayerScoreChanged`: プレイヤースコア変更
- `OnWinnerDetermined`: 勝者決定
- `OnPlayerCountChanged`: プレイヤー数変更
- `OnGameRestartRequested`: ゲーム再開要求
- `OnItemsReset`: アイテムリセット

**使用例:**
```csharp
// イベント購読
GameEvents.OnPlayerScoreChanged += OnPlayerScoreChanged;

// イベント発火
GameEvents.TriggerPlayerScoreChanged(playerId, newScore);

// イベント解除
GameEvents.OnPlayerScoreChanged -= OnPlayerScoreChanged;
```

### イベント駆動の利点

1. **疎結合**: コンポーネント間の直接参照を削減
2. **拡張性**: 新しいリスナーの追加が容易
3. **デバッグ性**: イベントフローの追跡が可能
4. **再利用性**: 汎用的なイベントシステム

## ネットワーク機能

### Photon Fusion統合

**Shared Mode使用:**
- 全クライアントが同等の権限を持つ
- StateAuthorityによる状態管理
- 自動的なホストマイグレーション

**主要なRPCパターン:**
```csharp
[Rpc(RpcSources.All, RpcTargets.StateAuthority)]
private void RPC_RequestAction(int parameter);

[Rpc(RpcSources.StateAuthority, RpcTargets.All)]
private void RPC_NotifyAction(int parameter);
```

### ネットワーク同期戦略

1. **スコア同期**: `[Networked, OnChangedRender]`による自動同期
2. **ゲーム状態**: RPCによる明示的な同期
3. **アイテム状態**: StateAuthorityクライアントが管理
4. **プレイヤー入力**: 各クライアントが自身の入力を管理

## ゲーム再開システム

### ハードリセット機能

従来のオブジェクトリセットではなく、完全なランタイム再初期化を実行。

**実行フロー:**
1. 両プレイヤーの再開クリック検出
2. `GameSyncManager.NotifyHardReset()`実行
3. 各クライアントで`HardResetRoutine()`実行
4. NetworkRunner.Shutdown()とシーン再ロード
5. 新しいセッションの開始

**利点:**
- 完全な状態クリア
- メモリリークの防止
- 予期しない状態残りの回避

### リセット処理の詳細

```csharp
private IEnumerator HardResetRoutine()
{
    // 猶予時間
    yield return new WaitForEndOfFrame();
    yield return new WaitForSeconds(0.1f);
    
    // NetworkObjectの削除
    DespawnAllNetworkObjects();
    
    // NetworkRunner停止
    if (networkRunner != null)
    {
        await networkRunner.Shutdown();
    }
    
    // 状態クリア
    ServiceRegistry.Clear();
    GameEvents.ClearAllHandlers();
    
    // シーン再ロード
    SceneManager.LoadScene(bootstrapSceneName, LoadSceneMode.Single);
}
```

## ServiceRegistry（依存解決システム）

### 概要

ゲーム内コンポーネント間の依存関係を効率的に管理するサービスロケーターパターンの実装。

### 主な機能

**登録・取得API:**
```csharp
ServiceRegistry.Register<T>(instance);
bool ServiceRegistry.TryGet<T>(out T value);
T ServiceRegistry.GetOrNull<T>();
ServiceRegistry.Clear();
```

**遅延解決:**
```csharp
ServiceRegistry.OnAnyRegistered += (type, instance) => {
    if (type == typeof(PlayerManager)) {
        // 遅延初期化処理
    }
};
```

### 使用パターン

1. **早期登録** (Awake/Start):
```csharp
void Awake() {
    ServiceRegistry.Register<PlayerManager>(this);
}
```

2. **安全な取得**:
```csharp
var playerManager = ServiceRegistry.GetOrNull<PlayerManager>();
if (playerManager != null) {
    // 使用処理
}
```

3. **遅延初期化**:
```csharp
void Start() {
    var itemManager = ServiceRegistry.GetOrNull<ItemManager>();
    if (itemManager == null) {
        ServiceRegistry.OnAnyRegistered += HandleLateRegistration;
    }
}
```

### 設計原則

- **軽量**: 最小限のオーバーヘッド
- **型安全**: ジェネリクスによる型チェック
- **ライフサイクル管理**: ハードリセット時の完全クリア
- **デバッグ対応**: 登録状況の追跡可能

## 開発・拡張ガイド

### 新機能追加の手順

1. **新しいアイテムタイプの追加**
```csharp
// Item.csを継承
public class SpecialItem : Item
{
    public override void OnCollected(PlayerAvatar player)
    {
        // 特殊効果の実装
        base.OnCollected(player);
    }
}
```

2. **新しいゲーム状態の追加**
```csharp
// GameState.csに列挙値追加
public enum GameState
{
    WaitingForPlayers,
    Countdown,
    InGame,
    GameOver,
    WaitingForRestart,
    NewGameMode // 新しい状態
}
```

3. **新しいイベントの追加**
```csharp
// GameEvents.csにイベント追加
public static event Action<CustomData> OnCustomEvent;

public static void TriggerCustomEvent(CustomData data)
{
    OnCustomEvent?.Invoke(data);
}
```

### デバッグとトラブルシューティング

**よくある問題と対処法:**

| 症状 | 原因 | 対処法 |
|------|------|--------|
| プレイヤーが消失 | ハードリセットの不完全実行 | 全クライアントでのリセット確認 |
| アイテムが0/0表示 | アイテムシーン未ロード時のカウント | シーンロード完了後の再カウント |
| 重複プレイヤー出現 | 安定ID競合 | ハードリセットまたは再接続 |
| スコア同期エラー | StateAuthority不整合 | RPCフローの確認 |

**デバッグログの活用:**
```csharp
Debug.Log($"GameController: Player count changed to {playerCount}");
Debug.Log($"NetworkGameManager: Spawning player at position {spawnPosition}");
```

### パフォーマンス最適化

1. **イベントリスナーの適切な解除**
2. **不要なFind系メソッドの削減**
3. **ServiceRegistryの効率的な活用**
4. **ネットワーク同期頻度の調整**

### 拡張可能な設計

このプロジェクトは以下の拡張を想定した設計となっています：

- **プレイヤー数の増加**: 2人以上への対応
- **新しいゲームモード**: チーム戦、タイムアタックなど
- **アイテムシステムの拡張**: 特殊効果、レアリティなど
- **UI/UXの改善**: より豊富な視覚効果とフィードバック

このサンプルプロジェクトを基に、より複雑で魅力的なマルチプレイヤーゲームを開発することができます。




＿＿＿＿

##  段階的な改造・拡張ポイント

このゲームを段階的に改造・拡張する際の推奨ポイントを、難易度と必要な知識レベル別に整理しました。

### 1. 初級レベル（Unity基礎知識）

#### A. UIとビジュアル改善
- **プレイヤーアバターの見た目変更**
  - `PlayerAvatar.prefab`のモデルやマテリアルを変更
  - `PlayerAvatarView.cs`でプレイヤー別の色分けやスキン追加
  - 対象ファイル: `Prefabs/PlayerAvatar.prefab`, `PlayerAvatarView.cs`

- **アイテムの種類拡張**
  - `Item.prefab`を複製して異なるスコアを持つアイテム作成
  - `Item.cs`でアイテム種別プロパティ追加
  - 対象ファイル: `Prefabs/Item.prefab`, `Item.cs`, `ItemManager.cs`

- **UI表示の改善**
  - `GameUIManager.cs`でスコア表示、タイマー表示の改善
  - ゲーム状態に応じたUI要素の追加
  - 対象ファイル: `GameUIManager.cs`

#### B. ゲームルールの簡単な変更
- **勝利条件の変更**
  - `GameRuleProcessor.cs`で時間制限やスコア閾値による勝利条件追加
  - 対象ファイル: `GameRuleProcessor.cs`

- **プレイヤー数の変更**
  - `GameController.cs`の`MAX_PLAYERS`定数変更
  - 対象ファイル: `GameController.cs`

### 2. 中級レベル（ネットワーク基礎知識）

#### A. 新しいゲーム機能追加
- **パワーアップアイテム実装**
  - 移動速度アップ、ジャンプ力アップなどの一時的効果
  - `PlayerAvatar.cs`に状態効果システム追加
  - `GameSyncManager.cs`でパワーアップ状態同期
  - 対象ファイル: `PlayerAvatar.cs`, `GameSyncManager.cs`, `Item.cs`

- **エリア制限・障害物追加**
  - マップにコライダーで境界設定
  - `PlayerAvatar.cs`の移動制限ロジック追加
  - 対象ファイル: `PlayerAvatar.cs`, シーン設定

- **チーム戦モード実装**
  - `PlayerModel.cs`にチーム情報追加
  - `GameRuleProcessor.cs`でチーム別勝利判定
  - `PlayerManager.cs`でチーム管理機能
  - 対象ファイル: `PlayerModel.cs`, `GameRuleProcessor.cs`, `PlayerManager.cs`

#### B. ゲーム進行システム拡張
- **ラウンド制ゲーム実装**
  - `GameController.cs`にラウンド管理機能追加
  - `GameSyncManager.cs`でラウンド状態同期RPC追加
  - 対象ファイル: `GameController.cs`, `GameSyncManager.cs`, `GameEvents.cs`

- **観戦モード実装**
  - `GameLauncher.cs`で観戦者とプレイヤーの区別
  - `NetworkGameManager.cs`で観戦者用の接続処理
  - 対象ファイル: `GameLauncher.cs`, `NetworkGameManager.cs`

### 3. 上級レベル（高度なネットワーク知識）

#### A. パフォーマンス最適化
- **ネットワーク通信最適化**
  - `PlayerAvatar.cs`のNetworkProperty使用量最適化
  - `GameSyncManager.cs`のRPC送信頻度制御
  - Fusion Tickの最適化
  - 対象ファイル: `PlayerAvatar.cs`, `GameSyncManager.cs`

- **サーバー権限管理強化**
- **バトルロワイヤルモード**
  - マップの段階的縮小システム
  - 生存者管理とエリミネーション
  - `GameRuleProcessor.cs`で複雑な勝利条件
  - クールダウン管理とネットワーク同期

### 4. 専門レベル（ゲーム開発全般知識）

#### A. AIシステム統合
- **NPCプレイヤー実装**
  - `PlayerAvatar.cs`のAI制御版作成
  - `GameController.cs`でAIプレイヤー管理
  - NavMeshを使用した移動AI

- **マッチメイキングシステム**
  - Photon CloudのマッチメイキングAPI活用
  - `GameLauncher.cs`でスキルベースマッチング
  - レーティングシステム実装

- **プレイヤー統計保存**
  - 外部データベース（Firebase等）との統合
  - ゲームプレイデータ収集システム
  - `GameEvents.cs`拡張でイベント解析
### 7.5. 改造時の推奨手順

   - 影響を受けるクラスの洗い出し
   - ネットワーク同期が必要な要素の特定
   - ローカル機能実装 → ネットワーク同期実装の順序
   - `PlayerAvatar.cs`へのRPC追加（プレイヤー固有機能）

3. **テストフェーズ**
   - シングルプレイヤーでの機能テスト
   - マルチプレイヤーでの同期テスト
   - エッジケース（接続切断、再接続）のテスト

### 6. 改造時の注意点
- **アーキテクチャの維持**: 責任分離の原則を維持し、適切なクラスに機能追加
- **ネットワーク負荷**: RPC頻度とデータサイズの最適化
- **後方互換性**: 既存のセーブデータやネットワークプロトコルとの互換性
- **デバッグ**: `GameEvents.cs`のイベントログ活用でデバッグ効率化
# C#イベントとActionの基礎・Unityでの使い方（初心者向け解説）

このプロジェクトでは「イベント駆動型」の設計を多用しています。C#のイベント・デリゲート・Actionの基礎と、Unityでの実践的な使い方を簡単にまとめます。

### 1. C#のイベント・デリゲートとは？

- **デリゲート**は「関数の型」。関数を変数のように渡したり、リストにして複数呼び出したりできます。
- **Action**は「戻り値なし」のデリゲート型。例：`Action<int>` は「int型を1つ受け取る関数」のリスト。
- **event**キーワードは「外部から +=, -= で購読/解除できるが、発火はクラス内部だけ」の制約をつけたもの。

#### 例：
```csharp
public event Action<int> OnScoreChanged;

// 登録（購読）
OnScoreChanged += MyScoreHandler;

// 解除
OnScoreChanged -= MyScoreHandler;

// 発火（呼び出し）
if (OnScoreChanged != null) OnScoreChanged(100);
// または null条件演算子で
OnScoreChanged?.Invoke(100);
```

### 2. Unityでのイベント活用パターン

- **ゲーム進行通知**：`GameEvents.OnGameStateChanged += ...` で状態変化をUIや他マネージャに伝える
- **プレイヤー登録通知**：`PlayerManager.OnPlayerRegistered += ...` で新規プレイヤー出現時にUIやアイテム管理を更新
- **UI更新**：`OnScoreChanged` でスコア表示を自動更新

#### Unityでよく使う書き方
```csharp
// 1. イベント定義
public event Action OnSomethingHappened;
public event Action<int, string> OnDataChanged;

// 2. イベント購読（StartやAwakeで）
OnSomethingHappened?.Invoke();
OnDataChanged?.Invoke(42, "Alice");

// 4. イベント解除（OnDestroyや終了時）
myManager.OnSomethingHappened -= HandleSomething;
```

### 3. よくあるミスと注意点

- **解除忘れ**：イベント購読したら必ずOnDestroy等で解除（メモリリーク・多重発火防止）
- **nullチェック**：`?.Invoke()` で購読者がいない時も安全
- **+=の重複**：同じハンドラを何度も+=すると複数回呼ばれる→`-=`してから`+=`が安全
- `GameEvents.OnPlayerScoreChanged += UpdatePlayerScoreUI;` … スコアが変わったらUIを自動更新
- `ServiceRegistry.OnAnyRegistered += HandleServiceRegistered;` … 遅延生成されたマネージャを検知して依存を解決

### 5. まとめ
イベントは「何かが起きたら通知する」仕組み。Action/デリゲート/イベントを使うことで、
・クラス同士が直接参照しなくても連携できる
・後から購読/解除が柔軟にできる
・ゲーム進行やUI更新の自動化が簡単になる

Unity+C#のイベントは「疎結合・拡張性・保守性」を高める基本テクニックです。

