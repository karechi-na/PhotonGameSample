
# PhotonGameSample プロジェクト概要 (2025-08 更新版)

Unity + Photon Fusion (Shared Mode) による 2 プレイヤー用サンプル。最新版では以下を含む:
- ハードリセット (全クライアント Runner Shutdown + bootstrap 再ロード)
- 安定 PlayerID (1/2) 強制マッピングと余剰 Join ガード
- ItemsScene 遅延再カウント (sceneLoaded コールバック)
- InGame 中 Restart クリック無視
- Duplicate Avatar 防止 / PlayerManager 辞書補完

> 最終更新: 2025-08

## 📚 目次 (簡略)
- 再スタート(ハードリセット) フロー概要
- 主なスクリプト強化点
- トラブルシュート早見表
- 既存アーキテクチャ解説 (原文抜粋)

---
<!-- 最新更新概要セクションは要求により削除されました -->
## 再スタート (Hard Reset) フロー概要
1. GameOver 後 両プレイヤーが再スタートクリック
2. GameSyncManager が HardReset RPC を全クライアントへ
3. 各クライアント: HardResetRoutine
   - (猶予) 1frame + 0.1s
   - 全 NetworkObject Despawn (authority 判定)
   - NetworkRunner.Shutdown()
   - ServiceRegistry.Clear & GameEvents.ClearAllHandlers()
   - bootstrap シーン Single Load
4. 新しい GameLauncher が Runner.StartGame()
5. マスター: ItemsScene (Additive) Load → sceneLoaded で ItemManager.CountExistingItems()
6. 安定 PlayerID=1/2 で再スポーン → Countdown → InGame

---
## 主なスクリプト強化点 (2025-08)
### NetworkGameManager
- HardResetRoutine: 全クライアントローカル実行
- ItemsScene 再カウント: OnRunnerSceneLoadDone + sceneLoaded 二段階
- assignedPlayerIds (PlayerRef→stableId 1/2) + 余剰 Join return
- Duplicate spawn 防止 (stableId 基準)

### GameController
- Restart クリック受付を GameOver/WaitingForRestart のみ許可
- 条件成立で GameSyncManager.NotifyHardReset 経由

### GameSyncManager
- HardReset RPC をトリガ (StateAuthority 発火)
- 進行同期: Countdown / GameState / EnableInput / ItemsReset

### ItemManager
- sceneLoaded で最終 CountExistingItems (Additive load 遅延対応)
- ResetAllItemsViaRPC: authority のみ Reset → 次フレーム再活性補正

---
## トラブルシュート早見表
| 症状 | 主因 | 確認ログ | 対応 |
|------|------|----------|------|
| Player1 消失 / Player2 残留 | 片側のみ HardReset | HardResetRoutine start/complete | 全クライアント実行仕様確認 |
| アイテム 0/0 復活しない | ItemsScene 未ロード時に Count | OnSceneLoadedCallback | 次回 HardReset / ログで再カウント確認 |
| Player3 出現 | 余剰 Join / Ghost | Rejecting/ignoring additional join | 将来: authority Disconnect 実装予定 |
| Duplicate Avatar | stableId 競合 | Duplicate spawn prevented | HardReset / 再接続待ち |

---
## ServiceRegistry (依存解決レイヤ / フェーズ1)
ゲーム内で相互参照が必要なマネージャ同士を「起動順や Find 系 API に依存せず」疎結合に接続するための軽量 DI (Service Locator) です。`[DefaultExecutionOrder(-800)]` により極めて早期に使用可能となり、ハードリセット時には `ServiceRegistry.Clear()` で完全初期化されます。

### 目的
- 起動順非決定 / 遅延スポーン(NetworkBehaviour) を安全に扱う
- `FindObjectsByType / Resources.FindObjectsOfTypeAll / GameObject.Find*` の常時ポーリング削減
- Hard Reset 後の古い参照残り (スタティック変数) を一括クリア

### 提供 API
```
ServiceRegistry.Register<T>(instance);
bool ServiceRegistry.TryGet<T>(out T value);
T ServiceRegistry.GetOrNull<T>();
ServiceRegistry.Clear();               // Hard Reset 中に呼び出し
event Action<Type, object> OnAnyRegistered; // 遅延解決用フック
```

### 代表的な使用パターン
1. 早期登録 (Awake / Start):
```
void Awake(){ ServiceRegistry.Register<PlayerManager>(this); }
```
2. 遅延取得 (存在すれば即使用 / 無ければ待機):
```
var pm = ServiceRegistry.GetOrNull<PlayerManager>();
if (pm == null) {
  ServiceRegistry.OnAnyRegistered += HandleRegistered;
}
void HandleRegistered(Type t, object inst){
  if (t == typeof(PlayerManager)) { /* attach and then */ ServiceRegistry.OnAnyRegistered -= HandleRegistered; }
}
```
3. Hard Reset 後再構築フロー:
```
// HardResetRoutine 内
ServiceRegistry.Clear();
// Bootstrap シーン再ロード→各 Awake/Start で再 Register
```

### イベント化による置き換え事例 (2025-08 適用 A)
| 旧 (Before) | 新 (After) | 効果 |
|-------------|-----------|------|
| ItemManager.Start() で `FindObjectsByType<PlayerAvatar>` を即時列挙 | PlayerManager 登録後に `OnPlayerRegistered` 経由で ItemCatcher を購読 | 起動タイミング競合と重複ハンドラ低減 |
| GameUIManager のローカルプレイヤー検出で都度 `FindObjectsByType<PlayerAvatar>` | PlayerManager.AllPlayers 優先 + Find はフォールバック | パフォーマンス / 安定性向上 |

### 設計方針
- ServiceRegistry は「インスタンスを保持のみ」: 生成責務は各コンポーネント
- OnAnyRegistered は軽量通知。重い初期化や再帰的 Register 呼び出しは避ける
- 取得が一度成功したらリスナーを必ず解除 (リーク防止)
- ネットワークオブジェクト (動的多数) には乱用しない (プレイヤー / ゲーム進行系の中核マネージャ限定)

### Hard Reset と整合性
Hard Reset では Runner.Shutdown → ServiceRegistry.Clear → GameEvents.ClearAllHandlers の順で副作用を除去。再ロードされた bootstrap シーンの Awake/Start で再登録が行われるため、古い参照 (Destroy 済みオブジェクト) が混入しません。

### Anti-Pattern (避けるべき)
| パターン | 問題 | 推奨代替 |
|----------|------|-----------|
| 毎フレーム TryGet ループ | 不要 CPU / GC | 一度取得→保持 / 遅延時はイベント待機 |
| Register 前提の強制 Null チェック無し使用 | Hard Reset レースで NRE | TryGet + フェールセーフログ |
| OnAnyRegistered 内で更に Register 連鎖 | 予期しない再入 / 順序難読化 | 責務分離し外側で組み立て |

### 今後の拡張余地
- Debug UI: 現在登録中サービス一覧ダンプ
- オプション: 競合登録 (複数) を許可するタグ付き拡張 (key=Type+string)
- プロファイル計測: Register / Resolve タイムスタンプ記録

---
## 既存アーキテクチャ (原文抜粋 + 差分)
下記以降は元 README の詳細解説 (イベント / 各クラス責務 / 拡張ガイド) を維持。更新差分は上部セクションを参照。

---

# PhotonGameSample プロジェクト概要

このプロジェクトは、UnityとPhoton Fusionを使用して構築されたマルチプレイヤーゲームのサンプルです。ゲームの基本的な流れ、主要なスクリプトの役割、およびイベントシステムについて解説します。

## 📚 目次
- [アーキテクチャの改善](#-アーキテクチャの改善)
- [ゲームの流れ](#1-ゲームの開始から終了までの流れ)
- [ソースコードの関係性](#2-各ソースコードの関係性)
- [イベントシステム](#3-イベントシステムの仕組み-gameeventscs)
- [各ソースコードの詳細](#4-各ソースコードの内容)
- [Prefabsディレクトリ](#prefabs-ディレクトリ概要)
- [責任分離の改善](#6-アーキテクチャ改善playeravatarの責任分離)
- [段階的改造ガイド](#7-段階的な改造拡張ポイント)

## ⚡ アーキテクチャの改善
このプロジェクトでは、コードの保守性と拡張性を向上させるため、ゲーム進行管理機能をPlayerAvatarから分離し、専用の`GameSyncManager`を導入しています。これにより、各クラスの責任が明確化され、より良いアーキテクチャを実現しています。

## 1. ゲームの開始から終了までの流れ

1.  **アプリケーション起動と接続:**
    *   `GameLauncher.cs`: アプリケーション起動時にPhoton Fusionの`NetworkRunner`を初期化し、Photon Cloudへの接続を開始します。`GameMode.Shared`でセッションを開始し、プレイヤーが参加できる状態にします。

2.  **プレイヤーの参加とスポーン:**
    *   `GameLauncher.cs` (`OnPlayerJoined`コールバック): 新しいプレイヤーがセッションに参加すると、このコールバックが呼び出されます。
    *   `GameController.cs` (`OnPlayerSpawned`): `NetworkGameManager`を通じてプレイヤーのアバターがネットワーク上にスポーンされると、`GameController`がその通知を受け取ります。スポーンされたプレイヤーは`PlayerManager`に登録されます。

3.  **ゲーム開始条件のチェック:**
    *   `GameController.cs` (`CheckPlayerCountAndUpdateGameState`): プレイヤーが参加するたびに、現在のプレイヤー数をチェックします。`MAX_PLAYERS`（デフォルト2人）に達すると、ゲームの状態が`WaitingForPlayers`から`InGame`に遷移し、ゲームが開始されます。
    *   プレイヤーの入力が有効化され、ゲームプレイが可能になります。

4.  **ゲームプレイ:**
    *   プレイヤーはアイテムを収集します。アイテムの収集状況は`ItemManager.cs`によって管理されます。
    *   プレイヤーのスコアは`PlayerAvatar.cs`内で管理され、変更されると`GameEvents.OnPlayerScoreChanged`イベントを通じて通知されます。

5.  **ゲーム終了条件と勝者決定:**
    *   `GameRuleProcessor.cs`: 全てのアイテムが収集されると、`ItemManager`から`OnAllItemsCollected`イベントが発火し、`GameRuleProcessor`がゲーム終了をトリガーします。
    *   `GameController.cs` (`EndGame`): `GameRuleProcessor`からの通知を受けて、`GameController`がゲームを終了状態に遷移させ、全プレイヤーの入力を無効化します。
    *   `GameRuleProcessor.cs` (`DetermineWinner`): スコア更新が完了した後、`PlayerManager`からプレイヤーのスコア情報を取得し、勝者を決定します。引き分けの場合も考慮されます。
    *   勝者決定の結果は`GameEvents.OnWinnerDetermined`イベントを通じて通知され、UIに表示されます。

6.  **ゲームのリスタート（オプション）:**
    *   ゲーム終了後、一定時間経過するとゲームがリスタートするロジックが`GameController.cs`に実装されています（`RestartGameAfterDelay`）。

## 2. 各ソースコードの関係性

主要なスクリプトとその関係性は以下の通りです。

*   **`GameLauncher.cs`**: Photon Fusionのセッション管理とプレイヤーの接続を担当します。`NetworkRunner`のライフサイクルイベントを処理し、`GameController`にプレイヤーの参加やスポーンを通知します。
*   **`GameController.cs`**: ゲーム全体の状態（待機中、ゲーム中、ゲーム終了）を管理する中心的なスクリプトです。`PlayerManager`, `ItemManager`, `NetworkGameManager`, `GameSyncManager`, `GameUIManager`, `GameRuleProcessor`といった他のマネージャーコンポーネントと連携し、ゲームの進行を制御します。イベント駆動で各マネージャーと通信します。
*   **`PlayerManager.cs`**: ゲームに参加しているプレイヤーのアバター（`PlayerAvatar`）を管理します。プレイヤーの登録、登録解除、スコア変更、プレイヤー数変更などのイベントを発火します。`GameRuleProcessor`に勝者決定のための情報を提供します。
*   **`ItemManager.cs`**: ゲーム内のアイテムの生成、収集、および収集状況を管理します。全てのアイテムが収集された際に`GameRuleProcessor`に通知します。
*   **`GameRuleProcessor.cs`**: ゲームの終了条件（全アイテム収集など）を判定し、ゲームの勝者を決定するロジックを担います。`PlayerManager`からスコア情報を取得し、`GameEvents`を通じて勝者決定を通知します。
*   **`GameUIManager.cs`**: ゲームのUI表示を管理します。ゲームの状態変化やスコア更新などのイベントを`GameEvents`から受け取り、UIを更新します。
*   **`NetworkGameManager.cs`**: Photon Fusionのネットワーク同期に関する処理をラップし、ネットワーク関連のイベントを`GameController`などの他のコンポーネントに通知する役割を担います。`NetworkRunner`のコールバックを直接処理するのではなく、より高レベルなイベントとして提供します。
*   **`GameSyncManager.cs`**: ゲーム進行の同期管理を専門に担当するNetworkBehaviourクラスです。カウントダウン、ゲーム状態変更、再開処理、アイテムリセットなどのRPC通信を通じて、クライアント間のゲーム進行を同期します。PlayerAvatarから分離されたことで、ゲーム進行管理がより明確に責任分離されています。
*   **`PlayerAvatar.cs` (Prefabs配下)**: 各プレイヤーのネットワークオブジェクトであり、プレイヤー個別の機能（移動、アニメーション、スコア、ニックネーム）に特化しています。ゲーム進行関連のRPCは`GameSyncManager`に移譲され、プレイヤー固有の機能のみを担当するよう簡素化されています。
*   **`Item.cs` (Prefabs配下)**: ゲーム内の収集可能なアイテムの挙動を定義します。プレイヤーがアイテムに触れた際の処理（収集）を管理します。

## 3. イベントシステムの仕組み (`GameEvents.cs`)

`GameEvents.cs`は、ゲーム内の様々なイベントを一元的に管理するための静的クラスです。Unityのイベントシステム（`Action`デリゲート）を利用して、各コンポーネント間の疎結合な通信を実現しています。

*   **イベントの定義:** `public static event Action<T> EventName;` の形式でイベントが定義されています。例えば、`OnGameStateChanged`はゲームの状態が変更されたときに発火します。
*   **イベントの発火:** `TriggerEventName(args);` の形式で、対応するイベントが発火されます。例えば、`GameEvents.TriggerGameStateChanged(newState);` を呼び出すことで、`OnGameStateChanged`イベントを購読している全てのリスナーに通知が送られます。
*   **イベントの購読:** 各コンポーネントは、`GameEvents.EventName += YourMethod;` の形式でイベントを購読し、イベント発生時に`YourMethod`が呼び出されるように設定します。これにより、イベントの発火元と購読元が直接依存することなく、柔軟なシステム構築が可能になります。

**主なイベント:**
*   `OnGameStateChanged`: ゲームの状態（待機中、カウントダウン中、ゲーム中、ゲーム終了）が変更された時。
*   `OnPlayerScoreChanged`: プレイヤーのスコアが変更された時。
*   `OnWinnerDetermined`: ゲームの勝者が決定された時。
*   `OnPlayerCountChanged`: ゲーム内のプレイヤー数が変更された時。
*   `OnPlayerRegistered`: 新しいプレイヤーが登録された時。
*   `OnGameEnd`: ゲームが終了した時。
*   `OnScoreUpdateCompleted`: スコアの更新が完了した時。
*   `OnCountdownUpdate`: ゲーム開始カウントダウンの更新時。

### 3.1 発火元と主な購読先一覧
| イベント | 発火メソッド / 典型的発火元 | 主な購読先 (代表) | 用途 / 備考 |
|----------|------------------------------|------------------|-------------|
| OnGameStateChanged | GameEvents.TriggerGameStateChanged() ← GameController / GameSyncManager | GameUIManager, PlayerManager(入力制御), ItemManager(必要に応じ) | 状態遷移通知 (Waiting→Countdown→InGame→GameOver) |
| OnPlayerScoreChanged | TriggerPlayerScoreChanged() ← PlayerAvatar / GameController | GameUIManager (スコアUI), GameRuleProcessor(終了条件判定) | スコアUI更新 / 終了判定補助 |
| OnWinnerDetermined | TriggerWinnerDetermined() ← GameRuleProcessor | GameUIManager (勝者表示), GameController(後続遷移) | 勝者表示と再開準備 |
| OnPlayerCountChanged | TriggerPlayerCountChanged() ← PlayerManager | GameController(開始条件), GameUIManager(表示) | 参加人数ステータス更新 |
| OnPlayerRegistered | TriggerPlayerRegistered() ← PlayerManager.RegisterPlayerAvatar | GameUIManager(UI生成), ItemManager(キャッチャ購読) | 新規プレイヤー初期化 |
| OnGameEnd | TriggerGameEnd() ← GameController.EndGame | GameUIManager, GameSyncManager(再開同期) | 終了フロー開始 |
| OnScoreUpdateCompleted | TriggerScoreUpdateCompleted() ← PlayerAvatar.Score変化後 | GameRuleProcessor(勝者決定待ち) | スコア反映完了合図 |
| OnCountdownUpdate | TriggerCountdownUpdate() ← GameSyncManager (RPC) | GameUIManager(カウント表示) | 開始カウントダウン同期 |
| OnGameRestartRequested | TriggerGameRestartRequested() ← UI/外部呼び出し | GameSyncManager(集約), GameController | 再開意図表明 (旧ロジック) |
| OnGameRestartExecution | TriggerGameRestartExecution() ← GameSyncManager | GameController(Reset手順), ItemManager | 旧ソフトリセット実行通知 (Hard Reset 後は限定使用) |
| OnPlayerClickedForRestart | TriggerPlayerClickedForRestart() ← GameUIManager / PlayerAvatar RPC | GameController(両者クリック集計) | Hard Reset 前提のクリック集約 |
| OnPlayerInputStateChanged | TriggerPlayerInputStateChanged() ← GameController / GameSyncManager | PlayerManager / 各 Avatar | 入力有効/無効制御 |
| OnItemsReset | TriggerItemsReset() ← GameSyncManager / GameController | ItemManager | アイテム状態リセット (権限側再活性) |
| OnItemsSceneReloaded | TriggerItemsSceneReloaded() ← シーンロード完了箇所 | ItemManager(再カウント) | Additive ItemsScene 遅延カウント |
| OnHardResetRequested | TriggerHardResetRequested() ← GameSyncManager RPC / NetworkGameManager | 全マネージャ(HardResetRoutine) | 全クライアント同時再初期化 |
| OnHardResetPreCleanup | TriggerHardResetPreCleanup() ← HardResetRoutine 直前 | 各マネージャ(購読解除/停止) | Runner.Shutdown 前の整理 |

補足:
- 発火元は「ゲーム進行(Controller)」「同期(RPC/SyncManager)」「エンティティ(PlayerAvatar)」の3層に分かれる。
- Hard Reset 後は OnGameRestartExecution 系の旧ソフトリセット用途は最小化し、HardResetRequested を基軸に統一。
- Score 関連は二重発火が起きても副作用を避ける idempotent 設計 (UI 上は上書きのみ)。

## 4. 各ソースコードの内容

### `GameController.cs`

*   **役割**: ゲームの全体的な進行と状態を管理します。他のマネージャー（`ItemManager`, `PlayerManager`など）と連携し、ゲームの開始、終了、プレイヤーの参加/離脱、スコア更新などを調整します。
*   **主要な機能**: 
    *   ゲーム状態の管理 (`CurrentGameState`プロパティ)。
    *   各マネージャーコンポーネントの参照とイベント購読/解除。
    *   プレイヤー数のチェックとゲーム状態の更新 (`CheckPlayerCountAndUpdateGameState`)。
    *   ゲーム終了処理 (`EndGame`)。
    *   プレイヤーの入力有効/無効化。
    *   勝者決定結果の受け取りとブロードキャスト。

### `GameEvents.cs`

*   **役割**: ゲーム全体で利用されるイベントを定義し、その発火メソッドを提供します。各コンポーネント間の疎結合な通信を促進します。
*   **主要な機能**: 
    *   ゲームの状態変化、プレイヤーのスコア変化、勝者決定など、様々なイベントの定義。
    *   各イベントを発火させるための静的メソッド (`Trigger...` メソッド)。

### `GameLauncher.cs`

*   **役割**: Photon Fusionのネットワークセッションを初期化し、プレイヤーの接続を管理します。`INetworkRunnerCallbacks`インターフェースを実装し、Photon Fusionからのネットワークイベントを処理します。
*   **主要な機能**: 
    *   `NetworkRunner`プレハブのインスタンス化と初期化。
    *   Photon Cloudへの接続とゲームセッションの開始 (`StartGame`)。
    *   プレイヤーの参加 (`OnPlayerJoined`)、離脱 (`OnPlayerLeft`)、接続失敗 (`OnConnectFailed`)などのネットワークイベントのハンドリング。
    *   `GameController`へのプレイヤー参加通知。

### `GameRuleProcessor.cs`

*   **役割**: ゲームの終了条件を判定し、ゲームの勝者を決定するロジックを実装します。主に`ItemManager`からの全アイテム収集イベントや、`GameController`からのゲーム終了要求を受けて動作します。
*   **主要な機能**: 
    *   ゲーム終了のトリガー (`TriggerGameEndByRule`)。
    *   スコア更新の完了を待機し、タイムアウト処理を行うコルーチン。
    *   `PlayerManager`からスコア情報を取得し、勝者（または引き分け）を決定する (`DetermineWinner`)。
    *   決定された勝者メッセージを`GameEvents`を通じてブロードキャスト。

### `GameUIManager.cs`

*   **役割**: ゲームのユーザーインターフェース（UI）の表示と更新を管理します。`GameEvents`からゲームの状態変化やスコア更新などの通知を受け取り、それに応じてUI要素を操作します。
*   **主要な機能**: 
    *   ゲームの状態表示（例: 「Waiting for Players...」、「Game Started!」）。
    *   プレイヤーのスコア表示の更新。
    *   勝者メッセージの表示。
    *   ゲーム終了時のUI要素の切り替え。

### `ItemManager.cs`

*   **役割**: ゲーム内に存在する収集可能なアイテムの管理を行います。アイテムの生成、プレイヤーによる収集、および収集状況の追跡を担当します。
*   **主要な機能**: 
    *   ゲーム開始時のアイテムの初期化と配置。
    *   プレイヤーがアイテムを収集した際の処理。
    *   収集されたアイテム数のカウントと、全アイテム収集時のイベント発火 (`OnAllItemsCollected`)。
    *   ゲーム状態のリセット時のアイテム状態のリセット。

### `NetworkGameManager.cs`

*   **役割**: Photon Fusionの`NetworkRunner`をラップし、ネットワーク関連のイベントを`GameController`などの他のコンポーネントに通知する役割を担います。`NetworkRunner`のコールバックを直接処理するのではなく、より高レベルなイベントとして提供します。
*   **主要な機能**: 
    *   プレイヤーの参加、離脱、スポーンに関するイベントの提供。
    *   ゲーム終了要求の処理。
    *   `NetworkRunner`インスタンスへのアクセス提供。

### `GameSyncManager.cs`

*   **役割**: ゲーム進行の同期管理を専門に担当するNetworkBehaviourクラスです。PlayerAvatarから分離されたゲーム進行関連のRPC機能を一元管理し、クライアント間でのゲーム状態同期を実現します。
*   **主要な機能**: 
    *   ゲーム再開クリックの同期（`NotifyRestartClick`）
    *   カウントダウン更新の同期（`NotifyCountdownUpdate`）
    *   ゲーム状態変更の同期（`NotifyGameStateChanged`）
    *   プレイヤー入力制御の同期（`NotifyEnableAllPlayersInput`）
    *   ゲーム再開処理の同期（`NotifyGameRestart`）
    *   アイテムリセットの同期（`NotifyItemsReset`）
*   **アーキテクチャ上の利点**: 
    *   PlayerAvatarがプレイヤー個別機能に集中できるよう責任を分離
    *   ゲーム進行管理の一元化により保守性が向上
    *   新しいゲーム進行機能の追加時、既存のPlayerAvatarに影響しない

### `PlayerManager.cs`

*   **役割**: ゲームに参加している全てのプレイヤーアバター（`PlayerAvatar`）の情報を一元的に管理します。プレイヤーの登録、登録解除、スコアの追跡、および勝者決定のための情報提供を行います。
*   **主要な機能**: 
    *   `PlayerAvatar`の登録と登録解除。
    *   各プレイヤーのスコアの管理と更新。
    *   プレイヤー数の追跡。
    *   最も高いスコアを持つプレイヤー（勝者）を特定するロジック (`DetermineWinner`)。
    *   プレイヤーの入力状態の有効/無効化。
*   **フォールバック機構**: `ContinuousPlayerCheck` によりネットワーク遅延で初期登録が漏れた `PlayerAvatar` を定期再スキャン。`pendingIdResolution` は `playerId==0` が後から確定したケースを再登録する救済リスト。`PruneNullEntries` は Destroy 済み参照を辞書から除去してゴーストを防止。安定後はこれらを DEBUG ビルド限定に縮小可能。



# Prefabs ディレクトリ概要

このディレクトリには、ゲーム内で使用される主要なプレハブとその関連スクリプトが含まれています。主にネットワーク同期されるオブジェクトや、ゲームプレイに直接関わる要素が定義されています。

## 1. 主要なプレハブとスクリプト

### `Item.prefab` と `Item.cs`

*   **役割**: ゲーム内でプレイヤーが収集するアイテムのプレハブと、その挙動を制御するスクリプトです。
*   **`Item.cs` の内容**: 
    *   アイテムが収集された際の処理（例: プレイヤーのスコア加算、アイテムの非アクティブ化）。
    *   プレイヤーがアイテムに触れたことを検出するトリガーロジック。
    *   ネットワークを介したアイテムの状態同期（収集済みかどうかなど）。

### `PlayerAvatar.prefab` と `PlayerAvatar.cs`

*   **役割**: ゲームに参加する各プレイヤーを表すアバターのプレハブと、その挙動を制御するスクリプトです。アーキテクチャ改善により、プレイヤー個別の機能に特化し、ゲーム進行管理は`GameSyncManager`に分離されています。
*   **`PlayerAvatar.cs` の内容**: 
    *   プレイヤーの移動、回転、ジャンプの制御
    *   プレイヤーのスコア、ニックネーム、IDなどの情報の管理とネットワーク同期
    *   アイテム取得とスコア更新のRPC処理
    *   勝者メッセージRPC（プレイヤー固有の通知機能）
    *   プレイヤーの入力処理（移動、ジャンプなど）
    *   `NetworkBehaviour`を継承し、Photon Fusionによるネットワーク同期を実現
*   **改善点**: 
    *   ゲーム進行関連のRPC（カウントダウン、状態変更、再開処理など）を`GameSyncManager`に移譲
    *   プレイヤー固有の責任に集中することで、コードの可読性と保守性が向上

### `NetworkRunner.prefab`

*   **役割**: Photon Fusionのネットワークセッションを管理するためのコアコンポーネントである`NetworkRunner`のプレハブです。`GameLauncher.cs`によってインスタンス化され、ゲームのネットワーク通信の基盤となります。
*   **内容**: 
    *   Photon Fusionのネットワークセッションの開始、参加、終了。
    *   ネットワークオブジェクトのスポーンとデスポーン。
    *   ネットワーク上のデータ同期とイベント処理。

## 2. Prefabsディレクトリ内のその他のファイル

*   **`.mat` ファイル**: 各プレハブに適用されるマテリアルファイルです。オブジェクトの見た目を定義します。
*   **`.meta` ファイル**: Unityが内部的に使用するメタデータファイルです。各アセットの設定情報などが含まれています。

このディレクトリのファイルは、ゲームの実行時に動的に生成またはロードされるオブジェクトのテンプレートとして機能し、マルチプレイヤー環境でのゲームプレイを支える重要な要素です。



### `ItemCatcher.cs`

*   **役割**: プレイヤーがアイテムを「キャッチ」したことを検出・処理するスクリプトです。主に`PlayerAvatar`にアタッチされ、アイテムとの衝突イベントを処理します。
*   **内容**: 
    *   `OnItemCaught`イベントを定義し、アイテムがキャッチされた際に通知します。
    *   `ItemCought`メソッドは、アイテムがキャッチされたときに呼び出され、関連するイベントを発火させます。

### `PlayerAvatarAnimationEventReceiver.cs`

*   **役割**: プレイヤーアバターのアニメーションイベントを受け取り、それに応じたサウンドエフェクト（足音、着地音など）を再生するスクリプトです。
*   **内容**: 
    *   アニメーションクリップに設定されたイベント（例: `OnFootstep`, `OnLand`）に対応するメソッドを実装します。
    *   指定されたオーディオクリップを再生します。

### `PlayerAvatarView.cs`

*   **役割**: プレイヤーアバターの視覚的な表現（モデル、マテリアルなど）を管理し、ネットワーク同期されたデータに基づいてアバターの外観を更新するスクリプトです。
*   **内容**: 
    *   プレイヤーのニックネームやスコアをUIに表示する処理。
    *   プレイヤーのモデルやマテリアルの切り替え。
    *   ネットワーク同期されたデータ（例: `PlayerAvatar.cs`からのデータ）を視覚的に反映。

### `PlayerModel.cs`

*   **役割**: プレイヤーの基本的なデータ構造と、そのデータを操作するためのロジックを定義するスクリプトです。主にプレイヤーの統計情報や状態を保持します。
*   **内容**: 
    *   プレイヤーID、ニックネーム、スコアなどのプロパティ。
    *   プレイヤーの状態（例: 生存、死亡）を管理するロジック。
    *   スコアの加算や減算などのデータ操作メソッド。

### `ServiceRefactorBaselineLogger.cs`
* **役割**: リファクタ（Find→イベント/ServiceRegistry化）前後のタイミング差異を可視化するための計測コンポーネント。Hard Reset / 再スタートサイクルの状態遷移・クリック時刻・カウントダウン値をログ化し非対称挙動やレースを早期検出。
* **購読イベント**: `OnGameStateChanged`, `OnPlayerClickedForRestart`, `OnCountdownUpdate`, `OnGameRestartExecution`, `OnGameEnd`。
* **出力例**: サイクル番号 / 各プレイヤーのクリック遅延(ms) / end→wait / wait→inGame の時間差。
* **実行順序**: `[DefaultExecutionOrder(-500)]` で早期購読し初期状態を逃さない。
* **削除タイミング**: 安定化後（差異ログが常に許容範囲 or Hard Reset バリア導入後）。
* **注意**: ログ量増大を避けるため本番ビルドでは無効化推奨。




## 5. ゲームイベントの流れと`GameEvents.cs`の発火順序

ゲーム内の主要なイベントは`GameEvents.cs`を通じて管理され、各コンポーネント間の疎結合な通信を実現しています。以下に、ゲーム開始からアイテム取得、スコア表示、勝敗判定の表示までのイベントの流れと、`GameEvents.cs`での発火および発火するクラスの順序を解説します。

### 5.1. ゲーム開始

1.  **`GameLauncher.cs`**: Photon Cloudへの接続が成功し、セッションが開始されると、`GameLauncher.cs`は`NetworkRunner`のコールバックを通じてプレイヤーの参加を検知します。
2.  **`GameController.cs`**: `GameLauncher.cs`からの通知を受け、`GameController.cs`はプレイヤー数をチェックします。`MAX_PLAYERS`に達すると、ゲームの状態を`WaitingForPlayers`から`CountdownToStart`に遷移させ、5秒のカウントダウンを開始します。
    *   **`GameEvents.TriggerGameStateChanged(GameState.CountdownToStart)`**: `GameController.cs`がゲーム状態の変更を`GameEvents`を通じて発火します。
    *   **`GameEvents.TriggerCountdownUpdate(remainingSeconds)`**: `GameController.cs`が1秒ごとにカウントダウンの残り時間を`GameEvents`を通じて発火します。これにより、`GameUIManager.cs`がカウントダウン表示を更新します。
3.  **ゲーム開始**: カウントダウンが完了すると、ゲームの状態が`InGame`に遷移し、全プレイヤーの操作が有効化されます。
    *   **`GameEvents.TriggerGameStateChanged(GameState.InGame)`**: `GameController.cs`がゲーム開始を`GameEvents`を通じて発火します。これにより、`GameUIManager.cs`などがゲーム開始UIを更新します。

### 5.2. アイテム取得とスコア表示

1.  **`Item.cs`**: プレイヤーがゲーム内のアイテムに触れると、`Item.cs`内のロジックがアイテムの収集を検知します。
2.  **`ItemCatcher.cs`**: `Item.cs`からの通知を受け、`ItemCatcher.cs`（`PlayerAvatar`にアタッチされている）がアイテムのキャッチを処理します。
3.  **`PlayerAvatar.cs`**: `ItemCatcher.cs`からの通知を受け、`PlayerAvatar.cs`は自身のスコアを更新します。このスコア更新はネットワーク同期されます。
    *   **`GameEvents.TriggerPlayerScoreChanged(playerId, newScore)`**: `PlayerAvatar.cs`が自身のスコア変更を`GameEvents`を通じて発火します。これにより、`GameUIManager.cs`などがプレイヤーのスコア表示をリアルタイムで更新します。
4.  **`ItemManager.cs`**: `Item.cs`が収集されると、`ItemManager.cs`は収集されたアイテム数を追跡します。

### 5.3. 勝敗判定の表示

1.  **`ItemManager.cs`**: 全てのアイテムが収集されると、`ItemManager.cs`は`OnAllItemsCollected`イベントを発火します。
2.  **`GameRuleProcessor.cs`**: `ItemManager.cs`からの`OnAllItemsCollected`イベントを受け取り、`GameRuleProcessor.cs`はゲーム終了のトリガーと判断します。
    *   **`GameEvents.TriggerGameEnd()`**: `GameRuleProcessor.cs`がゲーム終了を`GameEvents`を通じて発火します。これにより、`GameController.cs`がゲームを終了状態に遷移させ、プレイヤーの入力を無効化します。
3.  **`GameRuleProcessor.cs`**: ゲーム終了後、`GameRuleProcessor.cs`は`PlayerManager.cs`から最終的なスコア情報を取得し、勝者を決定します。
    *   **`GameEvents.TriggerWinnerDetermined(winnerId, winnerName, winnerScore)`**: `GameRuleProcessor.cs`が勝者決定の結果を`GameEvents`を通じて発火します。これにより、`GameUIManager.cs`などが勝者メッセージをUIに表示します。

### 5.4. 勝敗決定後 → リスタート (Hard Reset) まで

勝者表示後、再試合を開始するまでの詳細なイベント/処理フローは以下の通りです。

| フェーズ | 発火主体 / 条件 | 呼ばれるメソッド / イベント | 主な処理 | 次状態 |
|----------|-----------------|------------------------------|----------|--------|
| 勝者表示 | GameRuleProcessor 勝者確定 | GameEvents.TriggerWinnerDetermined / TriggerGameEnd | 入力無効化 (PlayerManager.SetAllPlayersInputEnabled(false)) / UI 勝者文言 | GameOver |
| 待機遷移 | GameController 内タイマー or 直接 | GameEvents.TriggerGameStateChanged(GameState.WaitingForRestart) | UI に再開クリック指示表示 / クリックフラグ初期化 | WaitingForRestart |
| クリック検知 | 各クライアント UI (GameUIManager) | GameEvents.TriggerPlayerClickedForRestart(playerId) | ローカル一度だけ送信 / UI を「相手待ち」に変更 | WaitingForRestart |
| 集約判定 | GameController (全員クリック済み確認) | (内部) 両クリック成立 → Hard Reset 要求 | Hard Reset RPC 発火 (GameSyncManager / NetworkGameManager) | HardResetRequested |
| Hard Reset 通知 | GameSyncManager RPC / NetworkGameManager | GameEvents.TriggerHardResetRequested() | 全クライアントで HardResetRoutine 開始 | HardResetRoutine 実行中 |
| クリーンアップ前フック | HardResetRoutine 冒頭 | GameEvents.TriggerHardResetPreCleanup() | 各マネージャ任意の購読解除/停止 | -- |
| Runner シャットダウン | HardResetRoutine | NetworkRunner.Shutdown() | ネットワーク状態破棄 / ServiceRegistry.Clear / GameEvents.ClearAllHandlers | -- |
| Bootstrap 再ロード | HardResetRoutine | SceneManager.LoadScene(Single) | 新シーン初期化 (GameLauncher 再生成) | WaitingForPlayers |
| 再セッション開始 | GameLauncher | Runner.StartGame(GameMode.Shared) | 新しい PlayerRef 割り当て → stableId 1/2 マッピング復元 | WaitingForPlayers / Countdown |
| アイテム再カウント | アイテム Additive シーンロード完了 | ItemManager.CountExistingItems() (sceneLoaded コールバック) | アイテム総数確定 / UI 初期化 | Countdown / InGame |
| カウントダウン再開 | GameController (プレイヤー揃う) | GameEvents.TriggerGameStateChanged(CountdownToStart) / TriggerCountdownUpdate | 以前と同じ開始シーケンス | Countdown |

補足:
- HardResetRoutine 先頭で 1frame + 0.1s の猶予を入れ、Restart クリック RPC や HardResetRequested イベント伝播の取りこぼしを緩和。
- Hard Reset 後は旧ソフトリセット (OnGameRestartExecution) は基本未使用で、完全再初期化に一本化。
- stableId (1/2) は PlayerRef の累積増加を UI / ロジックから隠蔽し、再戦回数に依存しない一貫 ID を提供。

このイベント駆動の仕組みにより、各コンポーネントは互いに直接依存することなく、柔軟かつ拡張性の高いゲームロジックを実現しています。

## 6. アーキテクチャ改善：PlayerAvatarの責任分離

### 6.1. 改善の背景
初期実装では`PlayerAvatar.cs`にプレイヤー個別の機能とゲーム進行管理の機能が混在しており、以下の問題がありました：
- 単一責任原則の違反（プレイヤー機能 + ゲーム進行管理）
- コードの可読性低下（400行超の肥大化）
- 保守性の低下（変更時の影響範囲が不明確）
- テスタビリティの低下（機能が密結合）

### 6.2. 解決策：GameSyncManagerの導入
`GameSyncManager`を新規作成し、以下のゲーム進行管理機能をPlayerAvatarから分離：

#### 分離された機能（6種類のRPCメソッド）
- `NotifyRestartClick` / `RPC_NotifyRestartClick` - 再開クリックの同期
- `NotifyCountdownUpdate` / `RPC_NotifyCountdownUpdate` - カウントダウンの同期  
- `NotifyGameStateChanged` / `RPC_NotifyGameStateChanged` - ゲーム状態変更の同期
- `NotifyEnableAllPlayersInput` / `RPC_NotifyEnableAllPlayersInput` - 入力制御の同期
- `NotifyGameRestart` / `RPC_NotifyGameRestart` - ゲーム再開処理の同期
- `NotifyItemsReset` / `RPC_NotifyItemsReset` - アイテムリセットの同期

#### PlayerAvatarに残された機能
- プレイヤーの移動・ジャンプ制御
- スコア管理（OnItemCaught、RPC_UpdateScore）
- プレイヤー固有の状態管理
- 勝者メッセージRPC（プレイヤー固有の機能）

### 6.3. 改善効果

| 改善項目 | 改善前 | 改善後 |
|----------|--------|--------|
| **責任の分離** | PlayerAvatarが複数責任 | 各クラスが単一責任 |
| **コード行数** | PlayerAvatar: 400行超 | PlayerAvatar: 300行程度<br>GameSyncManager: 120行程度 |
| **保守性** | 変更時の影響範囲が不明確 | 機能別に明確に分離 |
| **テスタビリティ** | 機能が密結合でテスト困難 | 独立してテスト可能 |
| **拡張性** | 新機能追加時に既存コード影響 | 適切なクラスに機能追加可能 |

### 6.4. 実装パターン
```csharp
// 改善前：PlayerAvatar内でゲーム進行RPC
public class PlayerAvatar : NetworkBehaviour 
{
    // プレイヤー機能 + ゲーム進行管理が混在
    public void NotifyRestartClick() { ... }
    public void NotifyCountdownUpdate() { ... }
    // ... 他のゲーム進行RPC
}

// 改善後：責任の明確な分離
public class PlayerAvatar : NetworkBehaviour 
{
    // プレイヤー個別機能のみに集中
    private void OnItemCaught() { ... }
    public void ResetScore() { ... }
}

public class GameSyncManager : NetworkBehaviour 
{
    // ゲーム進行同期管理に特化
    public void NotifyRestartClick() { ... }
    public void NotifyCountdownUpdate() { ... }
}
```

この改善により、より保守しやすく拡張可能なアーキテクチャを実現しています。

## 7. 段階的な改造・拡張ポイント

このゲームを段階的に改造・拡張する際の推奨ポイントを、難易度と必要な知識レベル別に整理しました。

### 7.1. 初級レベル（Unity基礎知識）

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

### 7.2. 中級レベル（ネットワーク基礎知識）

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

### 7.3. 上級レベル（高度なネットワーク知識）

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

### 7.4. 専門レベル（ゲーム開発全般知識）

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

### 7.6. 改造時の注意点
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

