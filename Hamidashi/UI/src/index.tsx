// Hamidashi UI の入口。開発用の別窓(Debug ビルドで、開発者モードのときだけ表示)をゲーム画面に足す。
// 本番の設定はオプション画面(C# の Setting)だけなので、v0.1 の UI はこの窓だけ。
// 出典: CS2 公式 UI Modding(moduleRegistry.append) https://cs2.paradoxwikis.com/UI_Modding
import { ModRegistrar } from "cs2/modding";
import { DevWindow } from "./devWindow";

export const hasCSS = false;

const register: ModRegistrar = (moduleRegistry) => {
  moduleRegistry.append("Game", DevWindow);
  console.log("[Hamidashi] UI module registered");
};

export default register;
