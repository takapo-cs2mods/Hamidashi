// Hamidashi UI の入口。
// ・本番のパネル(v1.1.0): ユニバーサルMODメニューのボタン(設定で左上にも)から開くフローティング窓
// ・開発用の別窓(Debug ビルドで、開発者モードのときだけ表示)
// 出典: CS2 公式 UI Modding(moduleRegistry.append) https://cs2.paradoxwikis.com/UI_Modding
import { ModRegistrar } from "cs2/modding";
import { DevWindow } from "./devWindow";
import { HamidashiPanel } from "./panel";
import { TopLeftLauncher, UniversalLauncher } from "./launcher";

export const hasCSS = false;

const register: ModRegistrar = (moduleRegistry) => {
  // 1か所の登録に失敗しても、ほかは止めない
  let universalOk = false;
  try {
    moduleRegistry.append("UniversalModMenu", UniversalLauncher);
    universalOk = true;
  } catch (e) {
    console.log("[Hamidashi] UniversalModMenu に登録できない: " + e);
  }

  try {
    // ユニバーサルMODメニューが無い(古いゲーム)ときは、左上に必ず出す
    moduleRegistry.append("GameTopLeft", universalOk ? TopLeftLauncher : UniversalLauncher);
  } catch (e) {
    console.log("[Hamidashi] GameTopLeft に登録できない: " + e);
  }

  moduleRegistry.append("Game", HamidashiPanel);
  moduleRegistry.append("Game", DevWindow);
  console.log("[Hamidashi] UI module registered");
};

export default register;
