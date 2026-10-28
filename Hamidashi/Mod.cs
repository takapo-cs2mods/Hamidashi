// Hamidashi v0.1: MOD の入口。駐車中の車を置き直すシステム、オプション画面、開発用の別窓の登録。
// 出典: 公式 Mod テンプレート(Cities Skylines II mod)の Mod.cs、CS2 公式 Modding https://cs2.paradoxwikis.com/Modding
namespace Hamidashi
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using Colossal.IO.AssetDatabase;
    using Colossal.Logging;
    using Game;
    using Game.Modding;
    using Game.SceneFlow;

    public class Mod : IMod
    {
        public static ILog log = LogManager.GetLogger(nameof(Hamidashi)).SetShowsErrorsInUI(false);

        public static Setting Current;
        private Setting m_Setting;

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info($"{nameof(OnLoad)} {Setting.kVersion}");

            // 置き直しはシミュレーションの変更が出そろう前(Modification1)に、見張りは描画の直前(Rendering)に動かす。
            // ゲームが置き直した車(新しく停まった・道路を動かした)は、その回だけ Updated が付くので Rendering で拾い、次のフレームでかけ直す。
            updateSystem.UpdateAt<ParkingSystem>(SystemUpdatePhase.Modification1);
            updateSystem.UpdateAt<ParkingWatchSystem>(SystemUpdatePhase.Rendering);

            // パネル(v1.1.0): ユニバーサルMODメニュー(と左上)のボタンから開くフローティング窓
            updateSystem.UpdateAt<PanelUISystem>(SystemUpdatePhase.UIUpdate);

            // 開発用の別窓(Debug ビルドで、開発者モードのときだけ表示)
            updateSystem.UpdateAt<DevUISystem>(SystemUpdatePhase.UIUpdate);

            m_Setting = new Setting(this);
            Current = m_Setting;
            m_Setting.RegisterInOptionsUI();
            foreach (string localeId in SupportedLocales())
            {
                GameManager.instance.localizationManager.AddSource(localeId, new Locale(m_Setting, localeId == "ja-JP"));
            }

            Setting.Loading = true;
            try
            {
                AssetDatabase.global.LoadSettings(nameof(Hamidashi), m_Setting, new Setting(this));
            }
            finally
            {
                Setting.Loading = false;
            }
        }

        // ゲームが対応している言語の一覧(Mikiri と同じ)。取れなければ英語と日本語だけ
        private static IEnumerable<string> SupportedLocales()
        {
            try
            {
                object manager = GameManager.instance.localizationManager;
                MethodInfo method = manager.GetType().GetMethod("GetSupportedLocales", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
                if (method?.Invoke(manager, null) is string[] locales && locales.Length > 0)
                {
                    return locales;
                }
            }
            catch (Exception e)
            {
                log.Info($"言語一覧が取れない: {e.GetType().Name}");
            }

            return new[] { "en-US", "ja-JP" };
        }

        public void OnDispose()
        {
            log.Info(nameof(OnDispose));
            if (m_Setting != null)
            {
                m_Setting.UnregisterInOptionsUI();
                m_Setting = null;
                Current = null;
            }
        }
    }
}
