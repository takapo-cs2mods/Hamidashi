// Hamidashi v1.1.0: 本番用のパネル(フローティング窓)。ユニバーサルMODメニューのボタン(と、設定で左上のボタン)から開く。
// ・中身はオプション画面と同じ値を読み書きする(開発用の窓のオプション再現を流用: DevUISystem.OptionsJson / SetOption / PressOption)。
// ・位置は Setting.PanelX / PanelY に保存する(次に開いたときも同じ場所)。
// 出典: CS2 公式 UI Modding(ValueBinding / TriggerBinding) https://cs2.paradoxwikis.com/UI_Modding
//       ボタンの差し込み先 "UniversalModMenu" / "GameTopLeft" は Hover Colors(River-Mochi, GPL-3.0)・Node Controller の実装を参考
namespace Hamidashi
{
    using Colossal.UI.Binding;
    using Game.UI;

    public partial class PanelUISystem : UISystemBase
    {
        private const string kGroup = "Hamidashi";

        // 開いているか。ゲームを閉じるまで覚える(保存はしない)
        public static bool Open;

        protected override void OnCreate()
        {
            base.OnCreate();

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "PanelOpen", () => Open));
            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "ShowTopLeft", () => Mod.Current != null && Mod.Current.ShowTopLeftButton));
            AddUpdateBinding(new GetterValueBinding<string>(kGroup, "Options", () => DevUISystem.OptionsJson()));
            AddUpdateBinding(new GetterValueBinding<string>(kGroup, "Pos", () =>
            {
                Setting s = Mod.Current;
                return s == null ? "-1,-1" : s.PanelX + "," + s.PanelY;
            }));
            AddUpdateBinding(new GetterValueBinding<string>(kGroup, "Version", () => Setting.kVersion));

            AddBinding(new TriggerBinding(kGroup, "Toggle", () => Open = !Open));
            AddBinding(new TriggerBinding(kGroup, "Close", () => Open = false));
            AddBinding(new TriggerBinding<string, string>(kGroup, "SetOption", (name, value) => DevUISystem.SetOption(name, value)));
            AddBinding(new TriggerBinding<string>(kGroup, "PressOption", name => DevUISystem.PressOption(name)));
            AddBinding(new TriggerBinding<int, int>(kGroup, "SavePos", (x, y) => SavePos(x, y)));
        }

        private static void SavePos(int x, int y)
        {
            Setting s = Mod.Current;
            if (s == null || (s.PanelX == x && s.PanelY == y))
            {
                return;
            }

            s.PanelX = x;
            s.PanelY = y;
            s.ApplyAndSave();
        }
    }
}
