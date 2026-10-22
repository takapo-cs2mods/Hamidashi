// Hamidashi v0.1: オプション画面。プリセット(L1〜L7)、種類ごとの ON/OFF、スライダー、適用・シャッフル・元に戻すのボタン。
// ・プリセットを選ぶとスライダーがその値になる。スライダーを動かすとプリセットは「カスタム」になる。
// ・値を変えただけでは街は変わらない。「適用」で全部の駐車中の車にかけ直す(ロード時は自動で1回)。
// 出典: CS2 公式 Modding(ModSetting, SettingsUI 属性) https://cs2.paradoxwikis.com/Modding
namespace Hamidashi
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using Colossal;
    using Colossal.IO.AssetDatabase;
    using Game.Modding;
    using Game.SceneFlow;
    using Game.Settings;
    using Game.UI;

    public enum HamidashiPreset
    {
        L1VanillaBureaucracy = 1,
        L2PeacefulTown = 2,
        L3ParisianStandard = 3,
        L4KarensRow = 4, // 表示名は v1.0.0 から Karen's Law(たかし: 最初の打ち間違い)。設定ファイルに名前で保存されるので内部名は変えない
        L5TetrisMaster = 5, // 表示名は v1.0.0 から Master of Tetris。内部名は設定ファイルに保存されるので変えない
        L6LosSantos = 6,
        L7CivilWar = 7,
        Custom = 99,
    }

    [FileLocation(nameof(Hamidashi))]
    [SettingsUIGroupOrder(kPresetGroup, kTargetGroup, kNormalGroup, kBadGroup, kActionGroup, kAboutGroup)]
    [SettingsUIShowGroupName(kPresetGroup, kTargetGroup, kNormalGroup, kBadGroup, kActionGroup, kAboutGroup)]
    public class Setting : ModSetting
    {
        // ビルドごとに更新する(ゲーム内でどのビルドが読み込まれたか見分けるため)
        public const string kVersion = "v1.0.0";

        public const string kSection = "Main";
        public const string kPresetGroup = "Preset";
        public const string kTargetGroup = "Target";
        public const string kNormalGroup = "Normal";
        public const string kBadGroup = "Bad";
        public const string kActionGroup = "Action";
        public const string kAboutGroup = "About";

        // 設定ファイルの読み込み中・プリセットの適用中は、「カスタム」に切り替えない
        public static bool Loading;
        private bool m_ApplyingPreset;

        private HamidashiPreset m_Preset = HamidashiPreset.L2PeacefulTown;
        private float m_LongOffset;
        private float m_LatOffset;
        private float m_AngleJitter;
        private int m_ReverseRate;
        private float m_FrontClearance;
        private int m_BadRate;
        private int m_ChaosRate;

        public Setting(IMod mod)
            : base(mod)
        {
            SetDefaults();
        }

        // ---- プリセット ----
        [SettingsUISection(kSection, kPresetGroup)]
        public HamidashiPreset Preset
        {
            get => m_Preset;
            set
            {
                m_Preset = value;
                if (!Loading && value != HamidashiPreset.Custom)
                {
                    ApplyPreset(value);
                }
            }
        }

        // ---- 対象 ----
        [SettingsUISection(kSection, kTargetGroup)]
        public bool RoadsideOn { get; set; }

        [SettingsUISection(kSection, kTargetGroup)]
        public bool LotOn { get; set; }

        [SettingsUISection(kSection, kTargetGroup)]
        public bool AutoNew { get; set; }

        // ---- ふつうの車のズレ ----
        [SettingsUISlider(min = 0f, max = 1.5f, step = 0.05f, unit = Unit.kFloatTwoFractions)]
        [SettingsUISection(kSection, kNormalGroup)]
        public float LongOffset
        {
            get => m_LongOffset;
            set { m_LongOffset = value; Touched(); }
        }

        [SettingsUISlider(min = 0f, max = 1f, step = 0.05f, unit = Unit.kFloatTwoFractions)]
        [SettingsUISection(kSection, kNormalGroup)]
        public float LatOffset
        {
            get => m_LatOffset;
            set { m_LatOffset = value; Touched(); }
        }

        [SettingsUISlider(min = 0f, max = 30f, step = 0.5f, unit = Unit.kFloatSingleFraction)]
        [SettingsUISection(kSection, kNormalGroup)]
        public float AngleJitter
        {
            get => m_AngleJitter;
            set { m_AngleJitter = value; Touched(); }
        }

        [SettingsUISlider(min = 0, max = 100, step = 5, unit = Unit.kInteger)]
        [SettingsUISection(kSection, kNormalGroup)]
        public int ReverseRate
        {
            get => m_ReverseRate;
            set { m_ReverseRate = value; Touched(); }
        }

        // v0.1.6: 奥のゆとり(m)。バニラは駐車場で鼻先を枠の奥の線ぴったりに置くので、その分だけ通路側へ引く(たかし 2026-10-21)。
        // 引ける量は「枠の長さ − 車の長さ」まで(長い車は動かない、お尻は通路へ出ない)。縦列(路駐)には効かない
        [SettingsUISlider(min = 0f, max = 1.5f, step = 0.05f, unit = Unit.kFloatTwoFractions)]
        [SettingsUISection(kSection, kNormalGroup)]
        public float FrontClearance
        {
            get => m_FrontClearance;
            set { m_FrontClearance = value; Touched(); }
        }

        // v0.1.4: 2輪(バイク・自転車)はバック駐車しない(たかし 2026-10-20、初期値 ON)
        [SettingsUISection(kSection, kNormalGroup)]
        public bool NoReverseTwoWheel { get; set; }

        // ---- やべーヤツ ----
        [SettingsUISlider(min = 0, max = 100, step = 1, unit = Unit.kInteger)]
        [SettingsUISection(kSection, kBadGroup)]
        public int BadRate
        {
            get => m_BadRate;
            set { m_BadRate = value; Touched(); }
        }

        [SettingsUISection(kSection, kBadGroup)]
        public bool BadCrooked { get; set; }

        [SettingsUISection(kSection, kBadGroup)]
        public bool BadStraddle { get; set; }

        [SettingsUISection(kSection, kBadGroup)]
        public bool BadTwoSpots { get; set; }

        [SettingsUISection(kSection, kBadGroup)]
        public bool BadSidewalk { get; set; }

        [SettingsUISlider(min = 0, max = 100, step = 1, unit = Unit.kInteger)]
        [SettingsUISection(kSection, kBadGroup)]
        public int ChaosRate
        {
            get => m_ChaosRate;
            set { m_ChaosRate = value; Touched(); }
        }

        // ---- 実行 ----
        [SettingsUIButton]
        [SettingsUISection(kSection, kActionGroup)]
        // 名前は Apply にしない(親クラス Game.Settings.Setting の Apply() を隠してしまうため)
        public bool ApplyNow
        {
            set => ParkingSystem.RequestApply("適用ボタン");
        }

        [SettingsUIButton]
        [SettingsUISection(kSection, kActionGroup)]
        public bool Shuffle
        {
            set
            {
                Seed = Seed + 1;
                ApplyAndSave();
                ParkingSystem.RequestApply("シャッフル(乱数 " + Seed + ")");
            }
        }

        [SettingsUIButton]
        [SettingsUIConfirmation]
        [SettingsUISection(kSection, kActionGroup)]
        public bool Restore
        {
            set => ParkingSystem.RequestRestore();
        }

        // 乱数の種(シャッフルで +1)。画面には出さない
        [SettingsUIHidden]
        public int Seed { get; set; }

        // ---- このMODについて ----
        [SettingsUISection(kSection, kAboutGroup)]
        public string Version => kVersion;

        [SettingsUISection(kSection, kAboutGroup)]
        public string Author => "takapo";

        [SettingsUISection(kSection, kAboutGroup)]
        public string License => "GNU GPL v3.0";

        public override void SetDefaults()
        {
            RoadsideOn = true;
            LotOn = true;
            AutoNew = true;
            NoReverseTwoWheel = true;
            BadCrooked = true;
            BadStraddle = true;
            BadTwoSpots = true;
            BadSidewalk = true;
            Seed = 0;
            m_Preset = HamidashiPreset.L2PeacefulTown;
            ApplyPreset(m_Preset);
        }

        private void Touched()
        {
            if (!Loading && !m_ApplyingPreset)
            {
                m_Preset = HamidashiPreset.Custom;
            }
        }

        // プリセットの中身。前後(m)・左右(m)・角度(°)・バック(%)・やべーヤツ(%)・カオス(%)
        // v0.1.3(たかし 2026-10-20): L2・L3 = ズレの幅だけ / L4 = L2 + やべーヤツ / L5 = L4 + カオス(並びはきっちり、積み上がる)/
        // L6 = もっと酷い L3 と L4(カオスは控えめ)/ L7 = 全部酷い。L2〜L4 はあり得る範囲、L5 からはネタ
        private void ApplyPreset(HamidashiPreset p)
        {
            float lon, lat, ang, clr;
            int rev, bad, chaos;
            switch (p)
            {
                case HamidashiPreset.L1VanillaBureaucracy: lon = 0f; clr = 0f; lat = 0f; ang = 0f; rev = 0; bad = 0; chaos = 0; break;
                case HamidashiPreset.L2PeacefulTown: lon = 0.15f; clr = 0.3f; lat = 0.1f; ang = 1.5f; rev = 20; bad = 0; chaos = 0; break;
                case HamidashiPreset.L3ParisianStandard: lon = 0.35f; clr = 0.3f; lat = 0.2f; ang = 4f; rev = 30; bad = 0; chaos = 0; break;
                case HamidashiPreset.L4KarensRow: lon = 0.15f; clr = 0.3f; lat = 0.1f; ang = 1.5f; rev = 20; bad = 20; chaos = 0; break;
                case HamidashiPreset.L5TetrisMaster: lon = 0.15f; clr = 0.3f; lat = 0.1f; ang = 1.5f; rev = 20; bad = 20; chaos = 25; break;
                case HamidashiPreset.L6LosSantos: lon = 0.6f; clr = 0.5f; lat = 0.4f; ang = 10f; rev = 40; bad = 35; chaos = 5; break;
                case HamidashiPreset.L7CivilWar: lon = 1.0f; clr = 0.5f; lat = 0.8f; ang = 30f; rev = 50; bad = 60; chaos = 35; break;
                default: return;
            }

            m_ApplyingPreset = true;
            LongOffset = lon;
            LatOffset = lat;
            AngleJitter = ang;
            ReverseRate = rev;
            FrontClearance = clr;
            BadRate = bad;
            ChaosRate = chaos;
            m_ApplyingPreset = false;
        }

        // 起動引数に -developerMode があるか(開発用の別窓を出すかどうか)。Key Repeat Fix と同じ
        public static bool IsDeveloperMode()
        {
            try
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                GameManager gm = GameManager.instance;
                if (gm == null)
                {
                    return false;
                }

                object config = typeof(GameManager).GetField("configuration", flags)?.GetValue(gm) ?? typeof(GameManager).GetProperty("configuration", flags)?.GetValue(gm);
                if (config == null)
                {
                    return false;
                }

                Type t = config.GetType();
                object value = t.GetField("developerMode", flags)?.GetValue(config) ?? t.GetProperty("developerMode", flags)?.GetValue(config);
                return value is bool b && b;
            }
            catch
            {
                return false;
            }
        }
    }

    // オプション画面の文字。日本語以外は英語
    public class Locale : IDictionarySource
    {
        private readonly Setting m_Setting;
        private readonly bool m_Ja;

        public Locale(Setting setting, bool ja)
        {
            m_Setting = setting;
            m_Ja = ja;
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            Setting s = m_Setting;
            bool ja = m_Ja;
            Dictionary<string, string> d = new Dictionary<string, string>();

            d[s.GetSettingsLocaleID()] = "Hamidashi: Imperfect Parking " + Setting.kVersion;
            d[s.GetOptionTabLocaleID(Setting.kSection)] = ja ? "メイン" : "Main";
            d[s.GetOptionGroupLocaleID(Setting.kPresetGroup)] = ja ? "プリセット" : "Preset";
            d[s.GetOptionGroupLocaleID(Setting.kTargetGroup)] = ja ? "対象" : "Targets";
            d[s.GetOptionGroupLocaleID(Setting.kNormalGroup)] = ja ? "ふつうの車のズレ" : "Everyday imperfection";
            d[s.GetOptionGroupLocaleID(Setting.kBadGroup)] = ja ? "やべーヤツ" : "Bad parkers";
            d[s.GetOptionGroupLocaleID(Setting.kActionGroup)] = ja ? "実行" : "Run";
            d[s.GetOptionGroupLocaleID(Setting.kAboutGroup)] = ja ? "このMODについて" : "About";

            Opt(d, nameof(Setting.Preset), "プリセット", "Preset",
                "選ぶと下のスライダーがその値になります。スライダーを動かすと「カスタム」になります。街に反映するには「適用」を押してください。",
                "Sets the sliders below. Moving a slider switches to Custom. Press Apply to update the city.");
            Preset(d, HamidashiPreset.L1VanillaBureaucracy, "L1: Vanilla Bureaucracy — Perfect grid. Zero offset, zero rotation. Clean, and slightly dystopian.");
            Preset(d, HamidashiPreset.L2PeacefulTown, "L2: Peaceful Town — Small human imperfections. A little off-center, a little crooked. Just real.");
            Preset(d, HamidashiPreset.L3ParisianStandard, "L3: Parisian Standard — Lines are a suggestion. Cars nudged tight against each other, angles getting sloppy.");
            Preset(d, HamidashiPreset.L4KarensRow, "L4: Karen's Law — A peaceful street, until she arrives. Her lot, her rules. Diagonal parkers and two-spot hogs everywhere. Someone is about to speak to the manager.");
            Preset(d, HamidashiPreset.L5TetrisMaster, "L5: Master of Tetris — Neatly lined up, and neatly stacked. A few on their side or roof. Nobody is getting out of here today.");
            Preset(d, HamidashiPreset.L6LosSantos, "L6: Los Santos — The lines are gone. Wheels on the sidewalk, noses in the bushes, bumpers inside each other.");
            Preset(d, HamidashiPreset.L7CivilWar, "L7: Civil War — Neighbors turned enemies. Tilted, flipped, stacked into chaos. You asked for this.");
            Preset(d, HamidashiPreset.Custom, ja ? "カスタム" : "Custom");

            Opt(d, nameof(Setting.RoadsideOn), "路駐", "Street parking", "道路沿いの縦列駐車にかけます。", "Applies to parallel parking along roads.");
            Opt(d, nameof(Setting.LotOn), "駐車場", "Parking lots", "駐車場・建物の敷地の駐車枠にかけます(Parking Lot Tool で作った駐車場もたぶん含む。確認中)。", "Applies to parking lots and building parking spaces.");
            Opt(d, nameof(Setting.AutoNew), "新しく停まった車にも", "Also new arrivals",
                "ON: 新しく停まった車や、道路・駐車場を動かして置き直された車にも自動でかけます。OFF: 「適用」を押したときだけ。",
                "ON: newly parked cars (and cars re-placed after moving a road or lot) are also randomized automatically. OFF: only when you press Apply.");
            Opt(d, nameof(Setting.LongOffset), "前後のズレ (m)", "Front/back offset (m)", "車の向きに沿ったズレの大きさ(ほとんどの車はこれより小さく、たまに大きめ)。", "Typical offset along the car. Most cars stay within it, a few go further.");
            Opt(d, nameof(Setting.LatOffset), "左右のズレ (m)", "Side offset (m)", "車の横方向のズレの大きさ。", "Typical sideways offset.");
            Opt(d, nameof(Setting.AngleJitter), "角度のブレ (°)", "Angle wobble (°)", "車の向きのブレの大きさ。", "Typical rotation wobble.");
            Opt(d, nameof(Setting.FrontClearance), "奥のゆとり (m)", "Front clearance (m)", "駐車場で、鼻先を枠の奥の線から手前に引く量。ゲームは鼻先を線ぴったりに置くので、少し引くと自然に見えます。枠より長い車は動きません(路駐には効きません)。", "How far cars stop short of the end of a lot space. The game parks every nose right on the line; a little gap looks more natural. Cars too long for the space stay put (not used for street parking).");
            Opt(d, nameof(Setting.ReverseRate), "バック駐車 (%)", "Backed-in (%)", "駐車場で、頭を通路に向けて停める車の割合(路駐には効きません)。", "Share of cars backed into lot spaces (not used for street parking).");
            Opt(d, nameof(Setting.NoReverseTwoWheel), "2輪はバック駐車しない", "No backing in for two-wheelers", "バイク・自転車は、バック駐車の割合に関係なく前から停めます。", "Motorbikes and bicycles always park nose-in, whatever the backed-in share.");
            Opt(d, nameof(Setting.BadRate), "やべーヤツの出現率 (%)", "Bad parker rate (%)", "下の種類から1つ選ばれます。", "One of the enabled types below is picked.");
            Opt(d, nameof(Setting.BadCrooked), "斜め停め", "Crooked", "大きく斜めに停める。", "Parked at a big angle.");
            Opt(d, nameof(Setting.BadStraddle), "線踏み", "On the line", "枠線をまたいで停める。隣に車がいない側へ寄ります(両隣が埋まっていたら控えめな斜め停め)。", "Parked across the line, on the side without a neighbor (a mild angle if both sides are taken).");
            Opt(d, nameof(Setting.BadTwoSpots), "2枠またぎ", "Two-spot hog", "隣の枠が空いているときだけ、2枠の真ん中に停める(駐車場のみ)。", "Takes two spaces, only when the next space is free (lots only).");
            Opt(d, nameof(Setting.BadSidewalk), "歩道乗り上げ", "On the sidewalk", "縁石側に寄って歩道に乗り上げる(路駐のみ)。", "Two wheels on the sidewalk (street parking only).");
            Opt(d, nameof(Setting.ChaosRate), "カオス (%)", "Chaos (%)",
                "傾き・横倒し・ひっくり返り・積み上げ。見た目だけですが、自己責任でどうぞ。",
                "Tilted, on its side, upside down, stacked. Visual only, but use at your own risk.");
            Opt(d, nameof(Setting.ApplyNow), "適用", "Apply", "今の設定で、街の駐車中の車を全部置き直します。", "Re-places every parked car in the city with the current settings.");
            Opt(d, nameof(Setting.Shuffle), "シャッフル", "Shuffle", "乱数を振り直して適用します(構図に合わせて引き直し)。", "Rerolls the randomness and applies.");
            Opt(d, nameof(Setting.Restore), "元に戻す", "Reset to vanilla", "全部の車をゲーム本来の位置に戻します。", "Puts every parked car back where the game placed it.");
            Opt(d, nameof(Setting.Version), "バージョン", "Version", "Hamidashi: Imperfect Parking。見た目だけの MOD で、車の動きや街の計算には関わりません。", "Hamidashi: Imperfect Parking. Visual only; does not touch traffic or simulation.");
            Opt(d, nameof(Setting.Author), "作者", "Author", "takapo(東京のパース屋)。コードは AI(Claude)と一緒に書きました。", "takapo, an architectural visualization artist in Tokyo. Built together with an AI (Claude).");
            Opt(d, nameof(Setting.License), "ライセンス", "License", "Hamidashi は GNU General Public License v3.0 のフリーソフトウェアです。", "Hamidashi is free software under the GNU General Public License v3.0.");
            d[s.GetOptionWarningLocaleID(nameof(Setting.Restore))] = ja ? "全部の車をゲーム本来の位置に戻します。よろしいですか?" : "Put every parked car back where the game placed it?";
            return d;
        }

        // ローカル関数は使わない(手元のコンパイルチェック mcs が理解しないため)
        private void Opt(Dictionary<string, string> d, string name, string jaLabel, string enLabel, string jaDesc, string enDesc)
        {
            d[m_Setting.GetOptionLabelLocaleID(name)] = m_Ja ? jaLabel : enLabel;
            d[m_Setting.GetOptionDescLocaleID(name)] = m_Ja ? jaDesc : enDesc;
        }

        private void Preset(Dictionary<string, string> d, HamidashiPreset p, string label)
        {
            d[m_Setting.GetEnumValueLocaleID(p)] = label;
        }

        public void Unload()
        {
        }
    }
}
