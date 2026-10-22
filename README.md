# Hamidashi: Imperfect Parking

A visual-only Cities: Skylines II mod that gives parked cars a little human sloppiness, or total chaos if you want. It never touches the simulation.

Source: https://github.com/takapo-cs2mods/Hamidashi
Paradox Mods: https://mods.paradoxplaza.com/mods/164345/Windows
Forum: https://forum.paradoxplaza.com/forum/threads/im-making-a-mod-to-make-parking-lots-look-more-realistic-thoughts.1946424/

[English](#english) / [日本語](#日本語)

## English

**Parked cars, outside the lines.**

Vanilla parking lots look like they were parked by robots: perfectly centered, perfectly straight, every nose right on the line. Hamidashi gives your parked cars a little human sloppiness. Or a lot, if you want.

**The name**
Hamidashi (はみ出し) is Japanese for "sticking out" or "going over the line", the word you'd use for a car parked over the white lines. That's exactly what this mod does.

**Features**
- Random offsets and angles for parked cars, scaled to vehicle size (motorbikes stay tidy, trucks get sloppier)
- Front clearance: cars stop a little short of the end of the space instead of nose-to-the-line
- Backed-in parking (two-wheelers excluded by default, can be turned off)
- Bad parkers: crooked, on the line, two-spot hogs, two wheels on the sidewalk. They never overlap their neighbors.
- Chaos for the higher levels: tilted, on their side, upside down, stacked two high
- Street parking and parking lots are detected separately and can be switched on/off
- 7 presets plus sliders for everything (moving a slider switches to Custom)
- Applied automatically when a city loads, and to newly parked cars (can be turned off)
- Apply / Shuffle / Reset to vanilla buttons. Shuffle until you like the layout, handy for screenshots.
- No icons or panels added to your screen. Settings are in the Options menu only.
- Works with existing saves

**Presets**
- **L1: Vanilla Bureaucracy:** Perfect grid. Clean, and slightly dystopian.
- **L2: Peaceful Town:** A little off-center, a little crooked. Just real.
- **L3: Parisian Standard:** Lines are a suggestion.
- **L4: Karen's Law:** A peaceful street, until she arrives. Her lot, her rules. Someone is about to speak to the manager.
- **L5: Master of Tetris:** Neatly lined up, and neatly stacked. Nobody is getting out of here today.
- **L6: Los Santos:** The lines are gone. Wheels on the sidewalk, noses in the bushes.
- **L7: Civil War:** Neighbors turned enemies. You asked for this.

L2–L4 are "this could actually happen". L5 and up are for laughs, or for your post-apocalyptic city.

**How it works**
- Visual only. It changes where a parked car is drawn and nothing else. Which space a car is in, free spaces, pathfinding and traffic are untouched.
- When the game places a parked car, Hamidashi shifts that one car. It always starts from the game's own position, so applying again never adds up.
- Negligible performance cost. Re-placing about 24,000 cars takes around 0.1 seconds, and in normal play only the cars that just parked are touched.
- Nothing builds up in memory over long sessions. Offsets are recalculated each time, not stored.

**Saves and removing the mod**
- Hamidashi writes no data of its own into your save. The shifted positions of parked cars are saved, the same way the game saves any car's position.
- Removing it is safe. Shifted cars simply stay where they are until they drive off. For a perfectly clean save: press "Reset to vanilla", save, then unsubscribe.

**Compatibility**
- Move It: moving a road or a lot keeps the cars shifted.
- Tested with Parking Lot Tool, Realistic Parking and Parking Control: no errors.
- Not affected: cars in garages or inside buildings (they aren't drawn), vehicles with trailers, cars placed as props.

**Good to know**
- When a car parks or drives off, it snaps between the game's position and the shifted one. Visual only means there is no in-between.
- Chaos is purely visual and was stable in testing, but L6/L7 are use-at-your-own-risk.
- L1 looks exactly like vanilla, but the mod is still running. To switch it off completely, disable the mod.
- Languages: English, Japanese (follows the game language)

**About**
I'm takapo, an architectural visualization artist in Tokyo. I can't write code, so this was built together with an AI (Claude): I came up with the ideas and tested everything in game, Claude wrote the code. Safety and stability came first.

**Open source**
GNU GPL v3. Source code: https://github.com/takapo-cs2mods/Hamidashi
Bug reports and ideas are welcome in the forum thread (discussion below) or as a GitHub Issue.

## 日本語

**はみ出してこそ、駐車場。**

バニラの駐車場は、綺麗に整いすぎとは思いませんか？Hamidashi は駐車中の車に、人間らしい雑さを少しだけ足します。どのくらい雑いかはあなた次第。

**名前の由来**
「はみ出し」。白線からはみ出して停めた車のことです。この MOD がやるのはそれだけです。

**機能**
- 駐車中の車の位置と角度をランダムにずらす　ズレは車の大きさに比例(バイクは控えめ、トラックは大きめ)
- 奥のゆとり: 鼻先を枠の奥の線ぴったりではなく、少し手前で止める
- バック駐車(2輪は初期設定で除外、オフも可)
- やべーヤツ: 斜め停め・線踏み・2枠またぎ・歩道乗り上げ　隣の車とは重なりません
- カオス: 傾き・横倒し・ひっくり返り・2段積みを解放
- 路駐と駐車場は別々に判定してます　それぞれオン・オフ可
- プリセット7種 + 全項目スライダー(スライダーを動かすとカスタムになります)
- 街のロード時に自動で適用、新しく停まった車にも自動でかかります(オフ可)
- 「適用」「シャッフル」「元に戻す」ボタン。気に入った並びが出るまでシャッフルできるのでスクショに便利かも
- 画面にアイコンもパネルも増やしません　設定はオプション画面だけなのでMODアイコンまみれ画面に優しさプラス
- 既存のセーブでそのまま使えます

**プリセット**
- **L1 Vanilla Bureaucracy:** バニラモード　そしてディストピア
- **L2 Peaceful Town:** ちょっと寄ってて　ちょっと曲がってる　現実的
- **L3 Parisian Standard:** 白線は努力目標程度
- **L4 Karen’s Law:** 平和な街に無法者が混ざります
- **L5 Master of Tetris:** 並んで積まれてひっくり返って
- **L6 Los Santos:** 白線はもう無い　歩道に乗り上げ、植え込みに突っ込め
- **L7 Civil War:** 隣人が敵になった　自己責任

L2〜L4 は「現実にありそう」、L5 からはネタ枠。荒廃した街づくりにもどうぞ。

**中身**
- 見た目だけのMODです。変えるのは駐車中の車が描かれる位置だけで、どの枠に停まっているか・空き枠・経路探索・交通には一切触りません
- ゲームが車を停めたタイミングを拾って、その車だけずらします。毎回ゲーム本来の位置から計算するので、何度適用してもズレが積み重なりません
- 負荷はほぼゼロ。約2万4千台の置き直しで0.1秒ほど。停まったばかりの車を検知し位置を荒らすだけ。
- 長時間回してもメモリに溜まりません。ズレは毎回計算し直していて、保存していません。

**セーブと MOD を外すとき**
- MOD 独自のデータはセーブに一切書きません。ただし、ずらした車の位置は、ゲームがふだん車の位置を保存するのと同じようにセーブに残ります。
- 外しても安全です。ずれた車は出庫したら戻ります。完全にきれいにしたい場合は「元に戻す」→ セーブ → 購読解除の順でどうぞ。

**互換性**
- Move It で道路や駐車場を動かしても、車はずれたままついてきます
- Parking Lot Tool・Realistic Parking・Parking Control と一緒に使ってエラーなし
- 対象外: 車庫・建物内の車(描かれていない車)、トレーラー付きの車、プロップで置いた車

**知っておいてほしいこと**
- 車が停まる瞬間と出発する瞬間は、本来の位置とずれた位置がパッと切り替わります(見た目だけの MOD なので途中がありません)
- カオスも見た目だけで、テストでは安定していましたが、L6・L7 は自己責任でどうぞ
- L1 はバニラと見た目は同じですが、MOD は動いています。完全に止めるなら MOD を無効にしてください
- 言語: 英語・日本語

**作った人**
東京でパース屋をやっています。コードは書けないので、AI(Claude様)と一緒に作りました。アイデア出しとゲーム内のテストが私、コードを書いたのは Claude様です。安全性と安定性を最優先にしています。

**オープンソース**
GNU GPL v3。ソースコード: https://github.com/takapo-cs2mods/Hamidashi
不具合報告やアイデアはフォーラムのスレか GitHub の Issue へどうぞ。

## For developers / 開発者向け

How it works (see `Hamidashi/ParkingSystem.cs`):

- A parked car is `Game.Vehicles.ParkedCar` (`m_Lane`, `m_CurvePosition`) plus `Game.Objects.Transform`. The game computes the transform with `VehicleUtils.CalculateParkingSpaceTarget(...)` and `FixParkingLocationSystem` writes it and adds `Updated` (when a car parks, and when its lane is updated, e.g. by Move It).
- `ParkingWatchSystem` (Rendering phase) collects parked cars that got `Updated`. `ParkingSystem` (Modification1) recomputes the game's own position with the same function and adds a deterministic offset (seeded by the base position and the Shuffle seed), so applying again never stacks.
- Writes follow what the game does: set `Transform`, update the moving-object search tree (`Game.Objects.SearchSystem`), add `Updated`. Lanes, `m_Lane` / `m_CurvePosition`, free space and pathfinding are never touched. No Harmony.
- Neighbor checks (overlap avoidance, stacking) query the moving-object search tree by position, so they also work on lots made of many short lanes (e.g. Parking Lot Tool).

Build: Visual Studio 2022 with the official Cities: Skylines II modding toolchain. The `UI` folder (a developer-only test window, Debug builds with `-developerMode` only) is built with webpack after the C# build.

## License

GNU General Public License v3.0. See [LICENSE](LICENSE).
