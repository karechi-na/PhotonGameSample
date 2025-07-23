# PlayerAvatar分離案

## 現状の問題
PlayerAvatarクラスに以下のゲーム進行管理RPCが混在している：

### プレイヤー基本機能（残すべき）
- 移動、ジャンプ
- スコア管理（OnItemCaught、RPC_UpdateScore）
- プレイヤー固有の状態管理

### ゲーム進行管理機能（分離すべき）
- NotifyRestartClick / RPC_NotifyRestartClick
- NotifyCountdownUpdate / RPC_NotifyCountdownUpdate  
- NotifyGameStateChanged / RPC_NotifyGameStateChanged
- NotifyEnableAllPlayersInput / RPC_NotifyEnableAllPlayersInput
- NotifyGameRestart / RPC_NotifyGameRestart
- NotifyItemsReset / RPC_NotifyItemsReset

## 分離案

### 1. GameSyncManager（新規作成済み）
- ゲーム進行関連のRPC機能を担当
- StateAuthorityを持つ単一インスタンスで全体のゲーム進行を管理

### 2. PlayerAvatar（簡素化）
- プレイヤー個別の機能のみに集中
- 移動、スコア、アイテム取得
- 勝者メッセージRPCなど、プレイヤー固有の通知機能

### 3. GameController（修正）
- GameSyncManagerを使用してゲーム進行を管理
- PlayerAvatarを直接操作せず、GameSyncManager経由で通信

## 実装手順

### Phase 1: GameSyncManagerの基盤作成（完了）
✅ GameSyncManagerクラス作成
✅ 基本的なRPCメソッドの実装

### Phase 2: GameControllerの修正
- GetMasterPlayerAvatar() → GetGameSyncManager()に変更
- PlayerAvatar経由のRPC呼び出しをGameSyncManager経由に変更

### Phase 3: PlayerAvatarの簡素化
- ゲーム進行関連RPCメソッドを削除
- プレイヤー固有機能のみ残存

### Phase 4: GameUIManagerの修正
- PlayerAvatar.NotifyRestartClick() → GameSyncManager.NotifyRestartClick()に変更

## メリット

### 1. 責任の分離
- PlayerAvatar: プレイヤー個別機能
- GameSyncManager: ゲーム全体の進行管理
- GameController: ローカルゲーム状態とネットワーク管理の橋渡し

### 2. 保守性の向上
- 各クラスの責任が明確
- ゲーム進行ロジックの変更がプレイヤー機能に影響しない

### 3. テスタビリティ
- ゲーム進行とプレイヤー機能を独立してテスト可能

### 4. スケーラビリティ
- 新しいゲーム進行機能追加時、PlayerAvatarを変更する必要がない
