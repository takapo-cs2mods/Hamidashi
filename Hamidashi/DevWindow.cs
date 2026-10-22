// Hamidashi v0.1: 開発用の別窓(Debug ビルドで、開発者モードのときだけ)。Mikiri v0.9.45 の窓と同じ作り。
// ・完全フローティング(見出しドラッグで移動)、閉じるではなく最小化(入力を保持)。
// ・左 = 検証用の操作(適用・シャッフル・元に戻す・情報をログへ)と状態、オプション画面の再現(Setting をリフレクションで並べる)。
// ・右 = チェックリスト: Claude が書いた確認項目ファイルを読み込んで並べる。OK/NG・メモを付けて「送信」すると、結果をファイルとログに書く。
//     項目ファイル: (LocalLow)\Colossal Order\Cities Skylines II\ModsData\Hamidashi\devtest\checklist.txt
//       1行目 "# タイトル"、以降 "ID<TAB>確認内容"(空行と # で始まる行は無視)
//     結果: 同じフォルダの result_<バージョン>_<日時>.json と Logs\Hamidashi.log の [TEST] 行
// 出典: CS2 公式 UI Modding(ValueBinding / TriggerBinding) https://cs2.paradoxwikis.com/UI_Modding
namespace Hamidashi
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Reflection;
    using System.Text;
    using Colossal.UI.Binding;
    using Game.UI;

    public partial class DevUISystem : UISystemBase
    {
        private const string kGroup = "HamidashiDev";

        public static bool Open = true;

        private static string s_ChecklistJson = "{\"title\":\"\",\"items\":[]}";
        private static bool s_ChecklistLoaded;

        public static string DevFolder => Path.Combine(UnityEngine.Application.persistentDataPath, "ModsData", "Hamidashi", "devtest");

        protected override void OnCreate()
        {
            base.OnCreate();

            // 公開版(Release ビルド)では開発用の窓を出さない。開発中(Debug ビルド)は開発者モードのときだけ出す(Key Repeat Fix と同じ)
#if DEBUG
            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "DevMode", () => Setting.IsDeveloperMode()));
            AddUpdateBinding(new GetterValueBinding<string>(kGroup, "Status", () => StatusJson()));
            AddUpdateBinding(new GetterValueBinding<string>(kGroup, "Options", () => OptionsJson()));
#else
            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "DevMode", () => false));
            AddUpdateBinding(new GetterValueBinding<string>(kGroup, "Status", () => "{}"));
            AddUpdateBinding(new GetterValueBinding<string>(kGroup, "Options", () => "[]"));
#endif
            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "Open", () => Open));
            AddBinding(new TriggerBinding(kGroup, "Toggle", () => Open = !Open));
            AddUpdateBinding(new GetterValueBinding<string>(kGroup, "Version", () => Setting.kVersion));

            AddBinding(new TriggerBinding(kGroup, "Apply", () => ParkingSystem.RequestApply("開発用の窓: 適用")));
            AddBinding(new TriggerBinding(kGroup, "Shuffle", () =>
            {
                Setting s = Mod.Current;
                if (s != null)
                {
                    s.Seed = s.Seed + 1;
                    s.ApplyAndSave();
                    ParkingSystem.RequestApply("開発用の窓: シャッフル(乱数 " + s.Seed + ")");
                }
            }));
            AddBinding(new TriggerBinding(kGroup, "Restore", () => ParkingSystem.RequestRestore()));
            AddBinding(new TriggerBinding(kGroup, "Info", () => ParkingSystem.RequestInfo()));
            AddBinding(new TriggerBinding<string>(kGroup, "Note", text => Mod.log.Info($"[NOTE] {text}")));

            AddBinding(new TriggerBinding<string, string>(kGroup, "SetOption", (name, value) => SetOption(name, value)));
            AddBinding(new TriggerBinding<string>(kGroup, "PressOption", name => PressOption(name)));

            AddUpdateBinding(new GetterValueBinding<string>(kGroup, "Checklist", () =>
            {
                if (!s_ChecklistLoaded)
                {
                    s_ChecklistLoaded = true;
                    LoadChecklist();
                }

                return s_ChecklistJson;
            }));
            AddBinding(new TriggerBinding(kGroup, "ReloadChecklist", () => LoadChecklist()));
            AddBinding(new TriggerBinding<string>(kGroup, "SubmitChecklist", json => Submit(json)));
        }

        private static string StatusJson()
        {
            Setting s = Mod.Current;
            StringBuilder sb = new StringBuilder("{");
            sb.Append("\"running\":").Append(Q(ParkingSystem.StatusText));
            sb.Append(",\"last\":").Append(Q(ParkingSystem.LastRunText));
            sb.Append(",\"auto\":").Append(ParkingSystem.AutoCount);
            sb.Append(",\"arrived\":").Append(ParkingSystem.Arrived);
            sb.Append(",\"departed\":").Append(ParkingSystem.Departed);
            sb.Append(",\"active\":").Append(ParkingSystem.Active ? "true" : "false");
            sb.Append(",\"preset\":").Append(Q(s != null ? s.Preset.ToString() : string.Empty));
            sb.Append(",\"seed\":").Append(s != null ? s.Seed : 0);
            return sb.Append('}').ToString();
        }

        // ---- オプションの再現(Mikiri v0.9.45 と同じ) ----
        private static string s_OptionsJson = "[]";
        private static double s_OptionsAt = -1;

        private static PropertyInfo[] OptionProps()
        {
            return typeof(Setting).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        }

        private static string AttrField(object attr, string field)
        {
            if (attr == null)
            {
                return string.Empty;
            }

            FieldInfo f = attr.GetType().GetField(field);
            object v = f != null ? f.GetValue(attr) : attr.GetType().GetProperty(field)?.GetValue(attr);
            return v is float fv ? fv.ToString("0.###", CultureInfo.InvariantCulture) : v?.ToString() ?? string.Empty;
        }

        private static string Label(string id, string fallback)
        {
            try
            {
                Colossal.Localization.LocalizationDictionary dict = Game.SceneFlow.GameManager.instance?.localizationManager?.activeDictionary;
                if (dict != null && dict.TryGetValue(id, out string label) && !string.IsNullOrEmpty(label))
                {
                    return label;
                }
            }
            catch (Exception)
            {
            }

            return fallback;
        }

        private static string OptionsJson()
        {
            double now = UnityEngine.Time.realtimeSinceStartupAsDouble;
            if (now - s_OptionsAt < 0.5)
            {
                return s_OptionsJson;
            }

            s_OptionsAt = now;
            Setting s = Mod.Current;
            if (s == null)
            {
                return s_OptionsJson = "[]";
            }

            StringBuilder sb = new StringBuilder("[");
            bool first = true;
            foreach (PropertyInfo p in OptionProps())
            {
                if (p.GetCustomAttributes(typeof(Game.Settings.SettingsUIHiddenAttribute), true).Length > 0 || !p.CanWrite)
                {
                    continue;
                }

                Type t = p.PropertyType;
                bool button = !p.CanRead || p.GetCustomAttributes(typeof(Game.Settings.SettingsUIButtonAttribute), true).Length > 0;
                string kind = button ? "button" : t == typeof(bool) ? "bool" : t == typeof(float) ? "float" : t == typeof(int) ? "int" : t.IsEnum ? "enum" : null;
                if (kind == null)
                {
                    continue;
                }

                object[] sec = p.GetCustomAttributes(typeof(Game.Settings.SettingsUISectionAttribute), true);
                object[] sl = p.GetCustomAttributes(typeof(Game.Settings.SettingsUISliderAttribute), true);
                string group = sec.Length > 0 ? AttrField(sec[0], "simpleGroup") : string.Empty;
                if (!first)
                {
                    sb.Append(',');
                }

                first = false;
                sb.Append("{\"name\":").Append(Q(p.Name))
                  .Append(",\"label\":").Append(Q(Label(s.GetOptionLabelLocaleID(p.Name), p.Name)))
                  .Append(",\"group\":").Append(Q(Label(s.GetOptionGroupLocaleID(group), group)))
                  .Append(",\"kind\":").Append(Q(kind));
                if (!button && p.CanRead)
                {
                    object v = p.GetValue(s);
                    string vs = v is float f ? f.ToString("0.###", CultureInfo.InvariantCulture) : v is bool bv ? (bv ? "true" : "false") : v?.ToString() ?? string.Empty;
                    sb.Append(",\"value\":").Append(Q(vs));
                }

                if (sl.Length > 0)
                {
                    sb.Append(",\"min\":").Append(Q(AttrField(sl[0], "min"))).Append(",\"max\":").Append(Q(AttrField(sl[0], "max"))).Append(",\"step\":").Append(Q(AttrField(sl[0], "step")));
                }

                if (t.IsEnum)
                {
                    // 選択肢は短い名前(L1 など)で出す。表示名はオプション画面の文字から
                    sb.Append(",\"options\":[");
                    Array values = Enum.GetValues(t);
                    for (int i = 0; i < values.Length; i++)
                    {
                        object ev = values.GetValue(i);
                        string label = Label(s.GetEnumValueLocaleID((Enum)ev), ev.ToString());
                        int dash = label.IndexOf(" — ", StringComparison.Ordinal);
                        sb.Append(i > 0 ? "," : string.Empty).Append("{\"v\":").Append(Q(ev.ToString())).Append(",\"t\":").Append(Q(dash > 0 ? label.Substring(0, dash) : label)).Append('}');
                    }

                    sb.Append(']');
                }

                sb.Append('}');
            }

            return s_OptionsJson = sb.Append(']').ToString();
        }

        private static void SetOption(string name, string value)
        {
            Setting s = Mod.Current;
            PropertyInfo p = s == null ? null : typeof(Setting).GetProperty(name);
            if (p == null || !p.CanWrite)
            {
                return;
            }

            try
            {
                Type t = p.PropertyType;
                object v = t == typeof(bool) ? (object)(value == "true")
                    : t == typeof(float) ? (object)float.Parse(value, CultureInfo.InvariantCulture)
                    : t == typeof(int) ? (object)(int)Math.Round(double.Parse(value, CultureInfo.InvariantCulture))
                    : t.IsEnum ? Enum.Parse(t, value) : null;
                if (v == null)
                {
                    return;
                }

                p.SetValue(s, v);
                s.ApplyAndSave();
                s_OptionsAt = -1;
                Mod.log.Info($"[DEV] オプション {name} = {value}");
            }
            catch (Exception e)
            {
                Mod.log.Info($"[DEV] オプション {name} の変更に失敗: {e.GetType().Name}: {e.Message}");
            }
        }

        private static void PressOption(string name)
        {
            Setting s = Mod.Current;
            PropertyInfo p = s == null ? null : typeof(Setting).GetProperty(name);
            if (p == null || !p.CanWrite || p.PropertyType != typeof(bool))
            {
                return;
            }

            p.SetValue(s, true);
            s_OptionsAt = -1;
            Mod.log.Info($"[DEV] ボタン {name}");
        }

        public static string Q(string v)
        {
            if (v == null)
            {
                return "\"\"";
            }

            StringBuilder sb = new StringBuilder("\"");
            foreach (char c in v)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }

            return sb.Append('"').ToString();
        }

        private static void LoadChecklist()
        {
            try
            {
                string path = Path.Combine(DevFolder, "checklist.txt");
                if (!File.Exists(path))
                {
                    s_ChecklistJson = "{\"title\":" + Q("(checklist.txt が無い)") + ",\"items\":[]}";
                    return;
                }

                string title = string.Empty;
                List<string> items = new List<string>();
                foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string line = raw.TrimEnd();
                    if (line.Length == 0)
                    {
                        continue;
                    }

                    if (line.StartsWith("#"))
                    {
                        if (title.Length == 0)
                        {
                            title = line.TrimStart('#').Trim();
                        }

                        continue;
                    }

                    int tab = line.IndexOf('\t');
                    string id = tab > 0 ? line.Substring(0, tab).Trim() : (items.Count + 1).ToString();
                    string text = tab > 0 ? line.Substring(tab + 1).Trim() : line.Trim();
                    items.Add("{\"id\":" + Q(id) + ",\"text\":" + Q(text) + "}");
                }

                s_ChecklistJson = "{\"title\":" + Q(title) + ",\"items\":[" + string.Join(",", items) + "]}";
            }
            catch (Exception e)
            {
                s_ChecklistJson = "{\"title\":" + Q("読み込みエラー: " + e.Message) + ",\"items\":[]}";
            }
        }

        private static void Submit(string json)
        {
            try
            {
                Directory.CreateDirectory(DevFolder);
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string path = Path.Combine(DevFolder, $"result_{Setting.kVersion}_{stamp}.json");
                File.WriteAllText(path, json ?? string.Empty, new UTF8Encoding(false));
                Mod.log.Info($"[TEST] チェックリスト送信 {Setting.kVersion} → {path}\n[TEST] {json}\n[TEST] 状態 {StatusJson()}");
            }
            catch (Exception e)
            {
                Mod.log.Info($"[TEST] 送信に失敗: {e.GetType().Name}: {e.Message}");
            }
        }
    }
}
