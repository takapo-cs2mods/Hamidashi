// Hamidashi v0.1: 駐車中の車をランダムに置き直す(見た目だけ)。
//
// 仕組み(Game.dll で確認、2026-10-19):
// ・駐車中の車 = Game.Vehicles.ParkedCar(m_Lane = 駐車レーン、m_CurvePosition = レーン上の位置)+ Game.Objects.Transform(見た目の位置・向き)。
// ・ゲームは VehicleUtils.CalculateParkingSpaceTarget(駐車レーン, レーンの設定, 車の大きさ, レーンの曲線, レーンの持ち主の Transform, 位置)
//   で Transform を計算し、FixParkingLocationSystem が書き込んで Updated を付ける。車が停まったとき(ParkCar が FixParkingLocation を付ける)と、
//   レーンが更新されたとき(道路・駐車場を Move It で動かしたとき、レーン上の車を全部集め直す = CollectParkedCarsJob)に動く。
// ・Hamidashi は同じ関数で「ゲーム本来の位置」を毎回計算し直し、そこからのズレを足す。今の位置からは足さないので、何回かけても二重にずれない。
// ・書き込み方はゲームと同じ: Transform を書き、動く物の探索木(カメラの範囲判定などに使う)を更新し、Updated を付ける。
//
// 割り切り(v0.1):
// ・駐車レーン(Game.Net.ParkingLane)に停まっている車だけ。建物の中の車庫(Unspawned)、トレーラー付き、仮想レーンは触らない。
// ・ずらした位置はセーブにも入る(Transform はセーブされる)。ロードし直しても本来の位置から計算するので二重にはならない。
//   MOD を外した場合は、その車が出入りするまでずれたまま(実害はない見込み)。セーブ前に戻すかは結果を見て決める。
namespace Hamidashi
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using Colossal.Collections;
    using Colossal.Mathematics;
    using Colossal.Serialization.Entities;
    using Game;
    using Game.Common;
    using Game.Net;
    using Game.Objects;
    using Game.Prefabs;
    using Game.Tools;
    using Game.Vehicles;
    using Unity.Collections;
    using Unity.Entities;
    using Unity.Jobs;
    using Unity.Mathematics;
    using ParkingLane = Game.Net.ParkingLane;
    using Road = Game.Net.Road;
    using Transform = Game.Objects.Transform;

    public partial class ParkingSystem : GameSystemBase
    {
        // ---- 外から(オプション・開発用の窓)の依頼 ----
        private static bool s_ApplyRequested;
        private static bool s_RestoreRequested;
        private static string s_RequestReason = string.Empty;
        private static int s_LoadDelay = -1;
        private static bool s_InfoRequested;

        // false の間(「元に戻す」のあと、次に「適用」するまで)は、新しく停まった車にもかけない
        public static bool Active = true;

        // 見張り(ParkingWatchSystem)が拾った「ゲームが置き直した車」
        public static readonly HashSet<Entity> Pending = new HashSet<Entity>();

        // ---- 開発用の窓に出す状態 ----
        public static string StatusText = string.Empty;
        public static string LastRunText = string.Empty;
        public static int AutoCount;

        // v0.1.1: 見えている駐車中の車の出入り(2秒ごとに数え直して差を取る。ロード・適用の直後から数える)
        public static int Arrived;
        public static int Departed;

        private const int kPerFrame = 20000;
        private const double kBudgetMs = 6.0;

        private EntityQuery m_ParkedQuery;
        private NativeArray<Entity> m_All;
        private int m_Index = -1;
        private bool m_Restore;
        private Stopwatch m_Watch = new Stopwatch();
        private double m_RunMs;
        private Stats m_Stats;
        private Stats m_AutoStats;
        private string m_RunReason = string.Empty;
        private Game.Objects.SearchSystem m_SearchSystem;
        private readonly List<Entity> m_Changed = new List<Entity>();
        private readonly List<Entity> m_PendingWork = new List<Entity>();
        private readonly HashSet<Entity> m_Neighbors = new HashSet<Entity>();

        // v0.1.5: 近くの(8m 以内の)見えている駐車中の車。レーンではなく位置で探す
        // (PLT の駐車場はレーンが細切れで、隣の車が別のレーンにいることが多かったため。v0.1.4 の L5 で積み上げが 27 台しか出なかった)
        private void AddNeighbors(Entity car)
        {
            try
            {
                if (!Alive(car) || !EntityManager.HasComponent<Transform>(car))
                {
                    return;
                }

                float3 pos = EntityManager.GetComponentData<Transform>(car).m_Position;
                foreach (Entity o in Near(pos, 8f))
                {
                    if (o != car && IsVisibleParked(o))
                    {
                        m_Neighbors.Add(o);
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        // ---- v0.1.5: 位置で近くの車を探す(ゲームの「動く物の探索木」= 駐車中の車も入っている)----
        private NativeQuadTree<Entity, QuadTreeBoundsXZ> m_Tree;
        private bool m_TreeReady;
        private readonly List<Entity> m_NearList = new List<Entity>();

        private struct NearIterator : INativeQuadTreeIterator<Entity, QuadTreeBoundsXZ>
        {
            public float2 Min;
            public float2 Max;
            public List<Entity> Out;

            public bool Intersect(QuadTreeBoundsXZ bounds)
            {
                return bounds.m_Bounds.min.x <= Max.x && bounds.m_Bounds.max.x >= Min.x && bounds.m_Bounds.min.z <= Max.y && bounds.m_Bounds.max.z >= Min.y;
            }

            public void Iterate(QuadTreeBoundsXZ bounds, Entity item)
            {
                if (Intersect(bounds))
                {
                    Out.Add(item);
                }
            }
        }

        private void BeginTree()
        {
            if (!m_TreeReady)
            {
                m_Tree = m_SearchSystem.GetMovingSearchTree(false, out JobHandle deps);
                deps.Complete();
                m_TreeReady = true;
            }
        }

        private void EndTree()
        {
            if (m_TreeReady)
            {
                m_SearchSystem.AddMovingSearchTreeWriter(default(JobHandle));
                m_TreeReady = false;
            }
        }

        // 中心から radius(m)の四角に入る物。結果は使い回しのリストなので、すぐ使い切ること
        private List<Entity> Near(float3 center, float radius)
        {
            m_NearList.Clear();
            BeginTree();
            NearIterator it = new NearIterator { Min = center.xz - radius, Max = center.xz + radius, Out = m_NearList };
            m_Tree.Iterate(ref it);
            return m_NearList;
        }

        private bool IsVisibleParked(Entity o)
        {
            return Alive(o) && EntityManager.HasComponent<ParkedCar>(o) && !EntityManager.HasComponent<Unspawned>(o) && !EntityManager.HasComponent<Deleted>(o) && !EntityManager.HasComponent<Temp>(o);
        }

        public static void RequestApply(string reason)
        {
            s_ApplyRequested = true;
            s_RestoreRequested = false;
            s_RequestReason = reason ?? string.Empty;
        }

        public static void RequestInfo()
        {
            s_InfoRequested = true;
        }

        public static void RequestRestore()
        {
            s_RestoreRequested = true;
            s_ApplyRequested = false;
            s_RequestReason = "元に戻す";
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            m_ParkedQuery = GetEntityQuery(
                ComponentType.ReadOnly<ParkedCar>(),
                ComponentType.ReadOnly<Transform>(),
                ComponentType.ReadOnly<PrefabRef>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());
            m_SearchSystem = World.GetOrCreateSystemManaged<Game.Objects.SearchSystem>();
        }

        protected override void OnDestroy()
        {
            if (m_All.IsCreated)
            {
                m_All.Dispose();
            }

            base.OnDestroy();
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            Pending.Clear();
            if (m_All.IsCreated)
            {
                m_All.Dispose();
            }

            m_Index = -1;
            if (mode != GameMode.Game)
            {
                s_LoadDelay = -1;
                return;
            }

            // ロード直後は少し待ってから1回だけかける(何回かけても同じ結果になる作りなので、待つのは念のため)
            Active = true;
            s_LoadDelay = 30;
            Mod.log.Info($"[LOAD] ロード完了({purpose})。30 フレーム後に全部の駐車中の車にかける");
        }

        protected override void OnUpdate()
        {
            Setting s = Mod.Current;
            if (s == null)
            {
                return;
            }

            if (s_LoadDelay > 0)
            {
                s_LoadDelay--;
                if (s_LoadDelay == 0)
                {
                    s_LoadDelay = -1;
                    RequestApply("ロード時");
                }
            }

            if (s_InfoRequested)
            {
                s_InfoRequested = false;
                LogInfo();
            }

            if (s_ApplyRequested || s_RestoreRequested)
            {
                StartRun(s_RestoreRequested, s_RequestReason);
                s_ApplyRequested = false;
                s_RestoreRequested = false;
            }

            m_Watch.Restart();
            try
            {
                if (m_Index >= 0)
                {
                    ContinueRun(s);
                }
                else if (Pending.Count > 0)
                {
                    ProcessPending(s);
                }
            }
            finally
            {
                // v0.1.5: 探索木を借りたら、途中で失敗しても必ずそのフレームのうちに返す
                EndTree();
            }
        }

        private void StartRun(bool restore, string reason)
        {
            if (m_All.IsCreated)
            {
                m_All.Dispose();
            }

            m_All = m_ParkedQuery.ToEntityArray(Allocator.Persistent);
            m_Index = 0;
            m_Restore = restore;
            m_RunMs = 0;
            m_Stats = default;
            m_RunReason = reason;
            Pending.Clear();
            Active = !restore;
            Mod.log.Info($"[RUN] {reason}: 駐車中の車 {m_All.Length} 台にかけ始める(元に戻す = {restore})");
        }

        private void ContinueRun(Setting s)
        {
            Params p = Params.From(s, m_Restore);
            m_Changed.Clear();
            int end = math.min(m_All.Length, m_Index + kPerFrame);
            while (m_Index < end)
            {
                Entity e = m_All[m_Index++];
                if (Alive(e))
                {
                    SafeProcess(e, ref p, ref m_Stats, false);
                }

                if ((m_Index & 255) == 0 && m_Watch.Elapsed.TotalMilliseconds > kBudgetMs)
                {
                    break;
                }
            }

            Commit();
            m_RunMs += m_Watch.Elapsed.TotalMilliseconds;
            StatusText = $"{m_RunReason}: {m_Index} / {m_All.Length} 台";
            if (m_Index >= m_All.Length)
            {
                LastRunText = $"{DateTime.Now:HH:mm:ss} {m_RunReason}: {m_Stats.Summary()} / 計算 {m_RunMs:0.0}ms";
                Mod.log.Info($"[RUN] 完了 {LastRunText}");
                Mod.log.Info($"[RUN] 設定: {Params.From(s, m_Restore).Describe()} / 路駐 {s.RoadsideOn} 駐車場 {s.LotOn} 自動 {s.AutoNew} 乱数 {s.Seed} プリセット {s.Preset}");
                m_All.Dispose();
                m_Index = -1;
                StatusText = string.Empty;
            }
        }

        // ゲームが置き直した車(新しく停まった・レーンが動いた)と、Hamidashi 自身が書いた車(同じ値ならすぐ飛ばす)
        private void ProcessPending(Setting s)
        {
            if (!s.AutoNew || !Active)
            {
                Pending.Clear();
                return;
            }

            m_PendingWork.Clear();
            m_PendingWork.AddRange(Pending);
            Pending.Clear();
            Params p = Params.From(s, false);
            m_Changed.Clear();
            int before = m_AutoStats.Changed;
            // v0.1.4: やべーヤツは隣の車を見て決まるので、新しく停まった車の同じレーンの近所(前後 2 台ぶん)も判定し直す
            if (p.Bad > 0f)
            {
                m_Neighbors.Clear();
                foreach (Entity e in m_PendingWork)
                {
                    AddNeighbors(e);
                }

                foreach (Entity e in m_Neighbors)
                {
                    if (!Pending.Contains(e) && !m_PendingWork.Contains(e))
                    {
                        m_PendingWork.Add(e);
                    }
                }
            }

            foreach (Entity e in m_PendingWork)
            {
                if (Alive(e) && EntityManager.HasComponent<ParkedCar>(e) && !EntityManager.HasComponent<Deleted>(e) && !EntityManager.HasComponent<Temp>(e))
                {
                    SafeProcess(e, ref p, ref m_AutoStats, true);
                }
            }

            Commit();
            AutoCount += m_AutoStats.Changed - before;
            LogAuto();
        }

        // 診断: 駐車レーンの種類ごとに、停まっている台数・路駐か・枠の角度と大きさをログに出す(PLT の駐車場の見分け用)
        private void LogInfo()
        {
            try
            {
                PrefabSystem prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
                Dictionary<Entity, int[]> counts = new Dictionary<Entity, int[]>();
                int garage = 0;
                int noLane = 0;
                NativeArray<Entity> all = m_ParkedQuery.ToEntityArray(Allocator.Temp);
                foreach (Entity car in all)
                {
                    if (EntityManager.HasComponent<Unspawned>(car))
                    {
                        garage++;
                        continue;
                    }

                    Entity lane = EntityManager.GetComponentData<ParkedCar>(car).m_Lane;
                    if (lane == Entity.Null || !Alive(lane) || !EntityManager.HasComponent<ParkingLane>(lane))
                    {
                        noLane++;
                        continue;
                    }

                    Entity lp = EntityManager.GetComponentData<PrefabRef>(lane).m_Prefab;
                    bool road = false;
                    if (EntityManager.HasComponent<Owner>(lane))
                    {
                        Entity o = EntityManager.GetComponentData<Owner>(lane).m_Owner;
                        road = o != Entity.Null && Alive(o) && EntityManager.HasComponent<Road>(o) && !EntityManager.HasComponent<Owner>(o);
                    }

                    if (!counts.TryGetValue(lp, out int[] c))
                    {
                        c = new int[2];
                        counts[lp] = c;
                    }

                    c[road ? 0 : 1]++;
                }

                Mod.log.Info($"[INFO] 駐車中の車 {all.Length} 台(車庫の中 {garage}、駐車レーン以外 {noLane})。駐車レーンの種類 {counts.Count}:");
                all.Dispose();
                foreach (KeyValuePair<Entity, int[]> kv in counts)
                {
                    string name = "?";
                    try
                    {
                        name = prefabs.GetPrefabName(kv.Key);
                    }
                    catch (Exception)
                    {
                    }

                    ParkingLaneData d = EntityManager.HasComponent<ParkingLaneData>(kv.Key) ? EntityManager.GetComponentData<ParkingLaneData>(kv.Key) : default;
                    Mod.log.Info($"[INFO]   {name}: 路駐 {kv.Value[0]} / 駐車場 {kv.Value[1]} 台、枠の角度 {math.degrees(d.m_SlotAngle):0}°、枠 {d.m_SlotSize.x:0.0}×{d.m_SlotSize.y:0.0}m、間隔 {d.m_SlotInterval:0.0}m");
                }
            }
            catch (Exception e)
            {
                Mod.log.Info($"[INFO] 失敗: {e.GetType().Name}: {e.Message}");
            }
        }

        // 書いた車に Updated を付けて、動く物の探索木を更新する(FixParkingLocationJob と同じ後始末)
        private void Commit()
        {
            if (m_Changed.Count == 0)
            {
                EndTree();
                return;
            }

            try
            {
                BeginTree();
                NativeQuadTree<Entity, QuadTreeBoundsXZ> tree = m_Tree;
                foreach (Entity e in m_Changed)
                {
                    Transform t = EntityManager.GetComponentData<Transform>(e);
                    Entity prefab = EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
                    if (EntityManager.HasComponent<ObjectGeometryData>(prefab))
                    {
                        ObjectGeometryData g = EntityManager.GetComponentData<ObjectGeometryData>(prefab);
                        tree.AddOrUpdate(e, new QuadTreeBoundsXZ(ObjectUtils.CalculateBounds(t.m_Position, t.m_Rotation, g)));
                    }
                }

            }
            catch (Exception ex)
            {
                Mod.log.Info($"[ERROR] 探索木の更新に失敗: {ex.GetType().Name}: {ex.Message}");
            }

            EndTree();

            NativeArray<Entity> arr = new NativeArray<Entity>(m_Changed.Count, Allocator.Temp);
            for (int i = 0; i < m_Changed.Count; i++)
            {
                arr[i] = m_Changed[i];
            }

            EntityManager.AddComponent(arr, ComponentType.ReadWrite<Updated>());
            arr.Dispose();
            m_Changed.Clear();
        }

        // v0.1.2: エンティティの番号が範囲外のときに EntityManager.Exists が落ちた(NullReferenceException、17:5x)。
        // Unity.Entities の EntityStore.Exists は番号を 8192 ずつの区画に割って表を直接読むだけで、範囲を確かめない(IL で確認)。
        // 負の番号(作りかけのエンティティの仮番号と推察)や上限超えだと表の外を読んで落ちる。上限 = MaximumTheoreticalAmountOfEntities(134,217,728)
        private const int kMaxEntities = 134217728;

        private bool Alive(Entity e)
        {
            return e != Entity.Null && e.Index >= 0 && e.Index < kMaxEntities && EntityManager.Exists(e);
        }

        private int m_ErrorLogged;

        private void SafeProcess(Entity car, ref Params p, ref Stats st, bool auto)
        {
            try
            {
                ProcessCar(car, ref p, ref st, auto);
            }
            catch (Exception ex)
            {
                st.Error++;
                if (m_ErrorLogged < 10)
                {
                    m_ErrorLogged++;
                    string lane = "?";
                    try
                    {
                        Entity l = EntityManager.GetComponentData<ParkedCar>(car).m_Lane;
                        lane = $"{l.Index}:{l.Version}";
                    }
                    catch (Exception)
                    {
                    }

                    Mod.log.Info($"[ERROR] 車 {car.Index}:{car.Version}(レーン {lane}、番号の上限 {kMaxEntities})で失敗、この車は飛ばす: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                }
            }
        }

        // v0.1.2 診断: 自動のかけ直しが多すぎる(55 分で 4.5 万回、停まりに来たのは 2,233 台)。
        // ゲームが本来の位置に戻した車をかけ直しているのか、同じ車を何度も書いているのかを 60 秒ごとにログに出す
        private readonly Dictionary<Entity, int> m_AutoHits = new Dictionary<Entity, int>();
        private int m_AutoFromBase;
        private int m_AutoOther;
        private double m_AutoLogAt;

        private void NoteAuto(Entity car, bool wasBase)
        {
            if (wasBase)
            {
                m_AutoFromBase++;
            }
            else
            {
                m_AutoOther++;
            }

            m_AutoHits.TryGetValue(car, out int n);
            m_AutoHits[car] = n + 1;
        }

        private void LogAuto()
        {
            double now = UnityEngine.Time.realtimeSinceStartupAsDouble;
            if (m_AutoLogAt <= 0)
            {
                m_AutoLogAt = now;
                return;
            }

            if (now - m_AutoLogAt < 60.0)
            {
                return;
            }

            double sec = now - m_AutoLogAt;
            m_AutoLogAt = now;
            if (m_AutoHits.Count == 0)
            {
                return;
            }

            int repeated = 0;
            int maxHits = 0;
            Entity maxCar = Entity.Null;
            foreach (KeyValuePair<Entity, int> kv in m_AutoHits)
            {
                if (kv.Value > 1)
                {
                    repeated += kv.Value - 1;
                }

                if (kv.Value > maxHits)
                {
                    maxHits = kv.Value;
                    maxCar = kv.Key;
                }
            }

            string lane = "?";
            try
            {
                if (Alive(maxCar) && EntityManager.HasComponent<ParkedCar>(maxCar))
                {
                    Entity l = EntityManager.GetComponentData<ParkedCar>(maxCar).m_Lane;
                    Entity lp = Alive(l) ? EntityManager.GetComponentData<PrefabRef>(l).m_Prefab : Entity.Null;
                    lane = lp != Entity.Null ? World.GetOrCreateSystemManaged<PrefabSystem>().GetPrefabName(lp) : "?";
                }
            }
            catch (Exception)
            {
            }

            Mod.log.Info($"[AUTO] 直近 {sec:0} 秒: 書いた {m_AutoFromBase + m_AutoOther} 回(ゲームが本来の位置に戻していた {m_AutoFromBase} / それ以外 {m_AutoOther})、車 {m_AutoHits.Count} 台、同じ車の2回目以降 {repeated} 回、いちばん多い車 {maxCar.Index}:{maxCar.Version} = {maxHits} 回(レーン {lane})");
            m_AutoHits.Clear();
            m_AutoFromBase = 0;
            m_AutoOther = 0;
        }

        // ---- 1台ぶん ----
        private void ProcessCar(Entity car, ref Params p, ref Stats st, bool auto)
        {
            st.Seen++;
            if (EntityManager.HasComponent<Unspawned>(car))
            {
                st.SkipGarage++;
                return;
            }

            if (EntityManager.HasComponent<Controller>(car) && EntityManager.GetComponentData<Controller>(car).m_Controller != car)
            {
                st.SkipTrailer++;
                return;
            }

            if (EntityManager.HasBuffer<LayoutElement>(car) && EntityManager.GetBuffer<LayoutElement>(car, true).Length > 1)
            {
                st.SkipTrailer++;
                return;
            }

            ParkedCar pc = EntityManager.GetComponentData<ParkedCar>(car);
            if (!TryGetBase(car, pc.m_Lane, pc.m_CurvePosition, out Transform baseT, out LaneInfo lane, out ObjectGeometryData geom))
            {
                st.SkipOther++;
                return;
            }

            bool enabled = !p.Restore && (lane.Roadside ? p.Roadside : p.Lot);
            if (lane.Roadside)
            {
                st.Roadside++;
            }
            else
            {
                st.Lot++;
            }

            Transform target = enabled ? Randomize(car, pc, baseT, lane, geom, ref p, ref st) : baseT;
            if (!IsValid(target))
            {
                st.Invalid++;
                target = baseT;
            }

            Transform cur = EntityManager.GetComponentData<Transform>(car);
            if (Same(cur, target))
            {
                st.Same++;
                return;
            }

            if (auto)
            {
                NoteAuto(car, Same(cur, baseT));
            }

            EntityManager.SetComponentData(car, target);
            m_Changed.Add(car);
            st.Changed++;
        }

        private struct LaneInfo
        {
            public ParkingLane Lane;
            public ParkingLaneData Data;
            public Curve Curve;
            public Transform OwnerTransform;
            public bool Roadside;
            public bool Parallel;
            public Entity Entity;
        }

        // ゲーム本来の位置(FixParkingLocationJob と同じ計算)
        private bool TryGetBase(Entity car, Entity laneEntity, float curvePos, out Transform result, out LaneInfo lane, out ObjectGeometryData geom)
        {
            result = default;
            lane = default;
            geom = default;
            if (laneEntity == Entity.Null || !Alive(laneEntity) || !EntityManager.HasComponent<ParkingLane>(laneEntity) || !EntityManager.HasComponent<Curve>(laneEntity))
            {
                return false;
            }

            lane.Entity = laneEntity;
            lane.Lane = EntityManager.GetComponentData<ParkingLane>(laneEntity);
            if ((lane.Lane.m_Flags & ParkingLaneFlags.VirtualLane) != 0)
            {
                return false;
            }

            Entity lanePrefab = EntityManager.GetComponentData<PrefabRef>(laneEntity).m_Prefab;
            Entity carPrefab = EntityManager.GetComponentData<PrefabRef>(car).m_Prefab;
            if (!EntityManager.HasComponent<ParkingLaneData>(lanePrefab) || !EntityManager.HasComponent<ObjectGeometryData>(carPrefab))
            {
                return false;
            }

            lane.Data = EntityManager.GetComponentData<ParkingLaneData>(lanePrefab);
            lane.Curve = EntityManager.GetComponentData<Curve>(laneEntity);
            geom = EntityManager.GetComponentData<ObjectGeometryData>(carPrefab);
            lane.OwnerTransform = default;
            lane.Roadside = false;
            if (EntityManager.HasComponent<Owner>(laneEntity))
            {
                Entity owner = EntityManager.GetComponentData<Owner>(laneEntity).m_Owner;
                if (owner != Entity.Null && Alive(owner))
                {
                    if (EntityManager.HasComponent<Transform>(owner))
                    {
                        lane.OwnerTransform = EntityManager.GetComponentData<Transform>(owner);
                    }

                    // 路駐 = 道路(建物の中の道路ではない)のレーン
                    lane.Roadside = EntityManager.HasComponent<Road>(owner) && !EntityManager.HasComponent<Owner>(owner);
                }
            }

            // 角度 0.25 rad(約 14°)以下はゲームでも縦列扱い
            lane.Parallel = lane.Data.m_SlotAngle <= 0.25f;
            result = VehicleUtils.CalculateParkingSpaceTarget(lane.Lane, lane.Data, geom, lane.Curve, lane.OwnerTransform, curvePos);
            return IsValid(result);
        }

        // ---- ズレの計算 ----
        private Transform Randomize(Entity car, ParkedCar pc, Transform baseT, LaneInfo lane, ObjectGeometryData geom, ref Params p, ref Stats st)
        {
            // 乱数は「本来の位置(25cm 単位)+ 乱数の種」から作る。同じ枠なら別の車が来ても似たズレ方になる
            int3 q = (int3)math.round(baseT.m_Position * 4f);
            uint h = math.hash(new int4(q, p.Seed)) | 1u;
            Unity.Mathematics.Random r = new Unity.Mathematics.Random(h);

            // 引く順番を固定(スライダーを変えてもパターンは同じで、大きさだけ変わる)
            float gLong = Gauss(ref r);
            float gLat = Gauss(ref r);
            float gAng = Gauss(ref r);
            float uBack = r.NextFloat();
            float uBad = r.NextFloat();
            float uBadType = r.NextFloat();
            float uBadA = r.NextFloat();
            float uBadB = r.NextFloat();
            float uChaos = r.NextFloat();
            float uChaosType = r.NextFloat();
            float uC1 = r.NextFloat();
            float uC2 = r.NextFloat();

            float3 fwd = math.rotate(baseT.m_Rotation, new float3(0f, 0f, 1f));
            float3 right = math.rotate(baseT.m_Rotation, new float3(1f, 0f, 0f));
            float3 up = math.rotate(baseT.m_Rotation, new float3(0f, 1f, 0f));

            // v0.1.1: ふつうのズレは車の大きさに比例(乗用車 長さ 4.5m・幅 1.8m を 1 として、0.35〜2 倍)。バイクが小さい枠から通路へ出ないように
            float sizeLong = math.clamp(geom.m_Size.z / 4.5f, 0.35f, 2f);
            float sizeLat = math.clamp(geom.m_Size.x / 1.8f, 0.35f, 2f);
            float dLong = gLong * p.Long * sizeLong;

            // v0.1.6: 奥のゆとり。駐車場で鼻先を枠の奥から通路側へ(fwd の逆)。引ける量は 枠の長さ − 車の長さ まで。
            // バック駐車は車の真ん中で 180° 回すので、同じだけ引けばお尻側にゆとりができる
            if (!lane.Parallel && p.Clearance > 0f)
            {
                float room = math.max(0f, lane.Data.m_SlotSize.y - geom.m_Size.z);
                dLong -= math.min(p.Clearance, room);
            }
            float dLat = gLat * p.Lat * sizeLat;
            float yaw = math.radians(gAng * p.Angle);
            float roll = 0f;
            float pitch = 0f;
            bool reverse = false;
            float lift = 0f;
            bool groundFix = false;
            Transform? stackOn = null;
            ObjectGeometryData stackGeom = default;

            float slotW = math.max(lane.Data.m_SlotSize.x, 1f);
            float slotL = math.max(lane.Data.m_SlotSize.y, 2f);
            float interval = lane.Data.m_SlotInterval > 0.1f ? lane.Data.m_SlotInterval : (lane.Parallel ? slotL : slotW);

            // 縁石側(ゲームが車を寄せる側): ParkingLeft なら左、ParkingRight なら右
            float curb = (lane.Lane.m_Flags & ParkingLaneFlags.ParkingLeft) != 0 ? -1f : (lane.Lane.m_Flags & ParkingLaneFlags.ParkingRight) != 0 ? 1f : 1f;

            // v0.1.4: 2輪(幅 1m 未満、または自転車置き場のような幅 1m 未満の枠)はバック駐車しない(オプション、初期値 ON)
            bool twoWheel = geom.m_Size.x < 1f || lane.Data.m_SlotSize.x < 1f;
            if (!lane.Parallel && uBack * 100f < p.Reverse && !(p.NoReverseTwoWheel && twoWheel))
            {
                reverse = true;
            }

            // やべーヤツ
            if (uBad * 100f < p.Bad)
            {
                int n = 0;
                Span4 types = default;
                if (p.Crooked)
                {
                    types.Add(1, ref n);
                }

                if (p.Straddle)
                {
                    types.Add(2, ref n);
                }

                if (p.TwoSpots && !lane.Parallel)
                {
                    types.Add(3, ref n);
                }

                if (p.Sidewalk && lane.Roadside)
                {
                    types.Add(4, ref n);
                }

                if (n > 0)
                {
                    int type = types.Get(math.min(n - 1, (int)(uBadType * n)));
                    float sign = uBadA < 0.5f ? -1f : 1f;

                    // v0.1.4: やべーヤツは隣の車と重ならないようにする(カオスはお構いなし)。
                    // 駐車場 = 隣の枠(間隔 × 1.5 以内)に車がいるか。縦列 = 前後の車までの空き(m)
                    bool nbPlus = false;
                    bool nbMinus = false;
                    if (!lane.Parallel)
                    {
                        Occupancy(car, baseT.m_Position, right, fwd, interval, slotL, out nbPlus, out nbMinus);
                    }

                    if (type == 2 && !lane.Parallel)
                    {
                        // 線踏み: 空いている側へ寄る。両隣が埋まっていたら控えめな斜め停めにする
                        float side = sign > 0f ? (!nbPlus ? 1f : !nbMinus ? -1f : 0f) : (!nbMinus ? -1f : !nbPlus ? 1f : 0f);
                        if (side == 0f)
                        {
                            type = 1;
                        }
                        else
                        {
                            sign = side;
                        }
                    }

                    if (type == 3)
                    {
                        // 隣の枠が空いている側にだけ寄る。両方埋まっていたら斜め停めにする
                        float side = sign > 0f ? (!nbPlus ? 1f : !nbMinus ? -1f : 0f) : (!nbMinus ? -1f : !nbPlus ? 1f : 0f);
                        if (side == 0f)
                        {
                            type = 1;
                        }
                        else
                        {
                            dLat += side * interval * 0.5f;
                            yaw += side * math.radians(math.lerp(4f, 10f, uBadB));
                            st.BadTwoSpots++;
                        }
                    }

                    if (type == 1)
                    {
                        // v0.1.4: 駐車場で隣に車がいるなら角度を控えめに(25° だと角が隣の枠へ 0.3m ほど出る)
                        float maxAng = lane.Parallel ? 12f : (nbPlus || nbMinus) ? 10f : 25f;
                        float minAng = lane.Parallel ? 6f : (nbPlus || nbMinus) ? 6f : 12f;
                        yaw += sign * math.radians(math.lerp(minAng, maxAng, uBadB));
                        if (lane.Parallel)
                        {
                            dLat -= curb * 0.4f; // 縦列はお尻か鼻が車道側に出る
                        }

                        st.BadCrooked++;
                    }
                    else if (type == 2)
                    {
                        if (lane.Parallel)
                        {
                            // v0.1.4: 縦列は前後の空き(m)まで。足りなければ空いている方へ、どちらも無ければ寄せない
                            float want = slotL * math.lerp(0.3f, 0.5f, uBadB);
                            float half = geom.m_Size.z * 0.5f;
                            float gapS = Gap(car, pc, lane, fwd, sign, half);
                            float gapO = Gap(car, pc, lane, fwd, -sign, half);
                            if (gapS < want && gapO > gapS)
                            {
                                sign = -sign;
                                gapS = gapO;
                            }

                            dLong += sign * math.clamp(math.min(want, gapS - 0.3f), 0f, want);
                        }
                        else
                        {
                            dLat += sign * slotW * math.lerp(0.4f, 0.55f, uBadB);
                        }

                        st.BadStraddle++;
                    }
                    else if (type == 4)
                    {
                        dLat += curb * math.lerp(0.6f, 1.1f, uBadB);
                        lift += 0.08f; // v0.1.1: 0.15 → 0.08(縁石の高さは道路ごとに違い、低い歩道で浮いて見えたため)
                        roll += -curb * math.radians(math.lerp(3f, 5f, uBadA));
                        st.BadSidewalk++;
                    }
                }
            }

            // カオス(L6・L7)
            if (uChaos * 100f < p.Chaos)
            {
                int type = (int)(uChaosType * 4f);
                float sign = uC1 < 0.5f ? -1f : 1f;
                switch (type)
                {
                    case 0: // 傾き
                        roll += sign * math.radians(math.lerp(15f, 40f, uC2));
                        pitch += math.radians(math.lerp(-15f, 15f, uC1));
                        groundFix = true;
                        st.ChaosTilt++;
                        break;
                    case 1: // 横倒し
                        roll += sign * math.radians(90f);
                        groundFix = true;
                        st.ChaosSide++;
                        break;
                    case 2: // ひっくり返り
                        roll += math.radians(180f);
                        yaw += math.radians(math.lerp(-20f, 20f, uC2));
                        groundFix = true;
                        st.ChaosFlip++;
                        break;
                    default: // 積み上げ(隣の車の上。隣がいなければ傾きにする)
                        if (TryNeighbor(car, baseT.m_Position, geom, out Transform nb, out stackGeom))
                        {
                            stackOn = nb;
                            st.ChaosStack++;
                        }
                        else
                        {
                            roll += sign * math.radians(math.lerp(15f, 40f, uC2));
                            groundFix = true;
                            st.ChaosTilt++;
                        }

                        break;
                }
            }

            // 回転は車の真ん中(大きさの箱の中心)まわり
            Bounds3 b = geom.m_Bounds;
            float3 center = (b.min + b.max) * 0.5f;
            quaternion local = math.mul(quaternion.AxisAngle(new float3(0f, 1f, 0f), yaw + (reverse ? math.PI : 0f)), math.mul(quaternion.AxisAngle(new float3(0f, 0f, 1f), roll), quaternion.AxisAngle(new float3(1f, 0f, 0f), pitch)));
            quaternion rot = math.mul(baseT.m_Rotation, local);
            float3 pos = baseT.m_Position + math.rotate(baseT.m_Rotation, center) - math.rotate(rot, center) + fwd * dLong + right * dLat + up * lift;

            if (stackOn.HasValue)
            {
                Transform nb = stackOn.Value;
                float3 nbUp = math.rotate(nb.m_Rotation, new float3(0f, 1f, 0f));
                rot = math.mul(nb.m_Rotation, quaternion.AxisAngle(new float3(0f, 1f, 0f), math.radians(math.lerp(-25f, 25f, uC2))));
                pos = nb.m_Position + nbUp * (stackGeom.m_Bounds.max.y - b.min.y + 0.02f);
            }
            else if (groundFix)
            {
                // 回したあと一番低い角が地面(本来の車の底)に来るように持ち上げる
                float ground = baseT.m_Position.y + b.min.y;
                float minY = float.MaxValue;
                for (int i = 0; i < 8; i++)
                {
                    float3 c = new float3((i & 1) != 0 ? b.max.x : b.min.x, (i & 2) != 0 ? b.max.y : b.min.y, (i & 4) != 0 ? b.max.z : b.min.z);
                    minY = math.min(minY, (pos + math.rotate(rot, c)).y);
                }

                pos.y += ground - minY;
            }

            return new Transform(pos, rot);
        }

        // 2枠またぎ用: 欲しい側(sign)が空いていればその向き、だめなら反対側、両方埋まっていれば 0
        // v0.1.4: 駐車場の両隣(間隔 × 1.5 以内)に見えている車がいるか。plus = 車の右側
        // v0.1.5: レーンではなく位置で探す。隣の車の「本来の位置」が、横(right)に 0.3m〜間隔×1.5、奥行き(fwd)に枠の長さの 6 割以内なら隣
        private void Occupancy(Entity car, float3 basePos, float3 right, float3 fwd, float interval, float slotL, out bool plus, out bool minus)
        {
            plus = false;
            minus = false;
            m_NearWork.Clear();
            m_NearWork.AddRange(Near(basePos, interval * 1.5f + 3f));
            foreach (Entity o in m_NearWork)
            {
                if (o == car || !IsVisibleParked(o))
                {
                    continue;
                }

                ParkedCar opc = EntityManager.GetComponentData<ParkedCar>(o);
                if (!TryGetBase(o, opc.m_Lane, opc.m_CurvePosition, out Transform ob, out LaneInfo oLane, out ObjectGeometryData oGeom))
                {
                    continue;
                }

                float3 v = ob.m_Position - basePos;
                float along = math.dot(v, right);
                float depth = math.dot(v, fwd);
                if (math.abs(depth) < slotL * 0.6f && math.abs(along) > 0.3f && math.abs(along) < interval * 1.5f)
                {
                    if (along > 0f)
                    {
                        plus = true;
                    }
                    else
                    {
                        minus = true;
                    }
                }
            }
        }

        private readonly List<Entity> m_NearWork = new List<Entity>();

        // v0.1.4: 縦列で、車の前(sign = 1)か後ろ(-1)の向きに、次の車のお尻・鼻までの空き(m)。いなければ 99
        private float Gap(Entity car, ParkedCar pc, LaneInfo lane, float3 fwd, float sign, float ownHalf)
        {
            float len = math.max(lane.Curve.m_Length, 0.01f);
            float3 tangent = math.normalizesafe(MathUtils.Tangent(lane.Curve.m_Bezier, pc.m_CurvePosition));
            float dir = math.dot(tangent, fwd) >= 0f ? 1f : -1f; // レーンの + 方向が車の前なら 1
            float best = 99f;
            DynamicBuffer<LaneObject> objs = EntityManager.GetBuffer<LaneObject>(lane.Entity, true);
            for (int i = 0; i < objs.Length; i++)
            {
                Entity o = objs[i].m_LaneObject;
                if (o == car || !Alive(o) || !EntityManager.HasComponent<ParkedCar>(o) || EntityManager.HasComponent<Unspawned>(o))
                {
                    continue;
                }

                float d = (objs[i].m_CurvePosition.x - pc.m_CurvePosition) * len * dir * sign;
                if (d <= 0f || d > 30f)
                {
                    continue;
                }

                Entity op = EntityManager.GetComponentData<PrefabRef>(o).m_Prefab;
                float oHalf = EntityManager.HasComponent<ObjectGeometryData>(op) ? EntityManager.GetComponentData<ObjectGeometryData>(op).m_Size.z * 0.5f : 2.25f;
                best = math.min(best, d - ownHalf - oHalf);
            }

            return best;
        }

        private float FreeSide(Entity car, ParkedCar pc, LaneInfo lane, float3 right, float interval, float sign)
        {
            float len = math.max(lane.Curve.m_Length, 0.01f);
            float3 tangent = math.normalizesafe(MathUtils.Tangent(lane.Curve.m_Bezier, pc.m_CurvePosition));
            float dir = math.dot(tangent, right) >= 0f ? 1f : -1f; // レーンの + 方向が車の右なら 1
            bool plus = false;
            bool minus = false;
            DynamicBuffer<LaneObject> objs = EntityManager.GetBuffer<LaneObject>(lane.Entity, true);
            for (int i = 0; i < objs.Length; i++)
            {
                Entity o = objs[i].m_LaneObject;
                if (o == car || !Alive(o) || !EntityManager.HasComponent<ParkedCar>(o))
                {
                    continue;
                }

                float d = (objs[i].m_CurvePosition.x - pc.m_CurvePosition) * len;
                if (math.abs(d) < interval * 1.5f)
                {
                    if (d * dir > 0f)
                    {
                        plus = true;
                    }
                    else
                    {
                        minus = true;
                    }
                }
            }

            if (sign > 0f)
            {
                return !plus ? 1f : !minus ? -1f : 0f;
            }

            return !minus ? -1f : !plus ? 1f : 0f;
        }

        // 積み上げ用: 近くでいちばん近い「自分と同じくらいか大きい」駐車中の車の本来の位置
        // v0.1.1: 大きさを見るようにした(バイクの上に乗用車が乗って宙に浮いて見えたため)。長さ・幅とも自分の 0.8 倍以上
        // v0.1.5: レーンではなく位置で探す(8m 以内)。PLT の駐車場でも隣の枠の車に乗れるように
        private bool TryNeighbor(Entity car, float3 basePos, ObjectGeometryData own, out Transform t, out ObjectGeometryData g)
        {
            t = default;
            g = default;
            float best = float.MaxValue;
            m_NearWork.Clear();
            m_NearWork.AddRange(Near(basePos, 8f));
            foreach (Entity o in m_NearWork)
            {
                if (o == car || !IsVisibleParked(o))
                {
                    continue;
                }

                ParkedCar opc = EntityManager.GetComponentData<ParkedCar>(o);
                if (!TryGetBase(o, opc.m_Lane, opc.m_CurvePosition, out Transform ob, out LaneInfo oLane, out ObjectGeometryData og))
                {
                    continue;
                }

                if (og.m_Size.z < own.m_Size.z * 0.8f || og.m_Size.x < own.m_Size.x * 0.8f)
                {
                    continue;
                }

                float d = math.distancesq(ob.m_Position, basePos);
                if (d > 0.04f && d < best && d < 64f)
                {
                    best = d;
                    t = ob;
                    g = og;
                }
            }

            return best < float.MaxValue && IsValid(t);
        }

        private static float Gauss(ref Unity.Mathematics.Random r)
        {
            float u1 = r.NextFloat(1e-6f, 1f);
            float u2 = r.NextFloat();
            float g = math.sqrt(-2f * math.log(u1)) * math.cos(2f * math.PI * u2);
            return math.clamp(g, -2.5f, 2.5f);
        }

        private static bool IsValid(Transform t)
        {
            return math.all(math.isfinite(t.m_Position)) && math.all(math.isfinite(t.m_Rotation.value)) && math.lengthsq(t.m_Rotation.value) > 0.5f;
        }

        private static bool Same(Transform a, Transform b)
        {
            return math.distancesq(a.m_Position, b.m_Position) < 1e-8f && math.abs(math.dot(a.m_Rotation.value, b.m_Rotation.value)) > 0.999999f;
        }

        // 種類を最大4つまで入れておく小さい入れ物
        private struct Span4
        {
            private int m_A;
            private int m_B;
            private int m_C;
            private int m_D;

            public void Add(int v, ref int n)
            {
                switch (n)
                {
                    case 0: m_A = v; break;
                    case 1: m_B = v; break;
                    case 2: m_C = v; break;
                    default: m_D = v; break;
                }

                n++;
            }

            public int Get(int i)
            {
                return i == 0 ? m_A : i == 1 ? m_B : i == 2 ? m_C : m_D;
            }
        }

        public struct Params
        {
            public bool Restore;
            public bool Roadside;
            public bool Lot;
            public float Long;
            public float Lat;
            public float Angle;
            public float Clearance;
            public float Reverse;
            public float Bad;
            public bool Crooked;
            public bool Straddle;
            public bool TwoSpots;
            public bool Sidewalk;
            public bool NoReverseTwoWheel;
            public float Chaos;
            public int Seed;

            public static Params From(Setting s, bool restore)
            {
                return new Params
                {
                    Restore = restore,
                    Roadside = s.RoadsideOn,
                    Lot = s.LotOn,
                    Long = math.max(0f, s.LongOffset),
                    Lat = math.max(0f, s.LatOffset),
                    Angle = math.max(0f, s.AngleJitter),
                    Clearance = math.max(0f, s.FrontClearance),
                    Reverse = s.ReverseRate,
                    Bad = s.BadRate,
                    Crooked = s.BadCrooked,
                    Straddle = s.BadStraddle,
                    TwoSpots = s.BadTwoSpots,
                    Sidewalk = s.BadSidewalk,
                    NoReverseTwoWheel = s.NoReverseTwoWheel,
                    Chaos = s.ChaosRate,
                    Seed = s.Seed,
                };
            }

            public string Describe()
            {
                return $"前後 {Long:0.00}m 左右 {Lat:0.00}m 奥のゆとり {Clearance:0.00}m 角度 {Angle:0.0}° バック {Reverse}% やべー {Bad}% カオス {Chaos}%";
            }
        }

        public struct Stats
        {
            public int Seen;
            public int Changed;
            public int Same;
            public int Roadside;
            public int Lot;
            public int SkipGarage;
            public int SkipTrailer;
            public int SkipOther;
            public int Invalid;
            public int Error;
            public int BadCrooked;
            public int BadStraddle;
            public int BadTwoSpots;
            public int BadSidewalk;
            public int ChaosTilt;
            public int ChaosSide;
            public int ChaosFlip;
            public int ChaosStack;

            public string Summary()
            {
                return $"見た {Seen} 台(路駐 {Roadside} / 駐車場 {Lot})、書いた {Changed}、同じ {Same}、飛ばした 車庫 {SkipGarage}・トレーラー {SkipTrailer}・その他 {SkipOther}、計算エラー {Invalid}、失敗 {Error}" +
                       $" / やべー 斜め {BadCrooked}・線踏み {BadStraddle}・2枠 {BadTwoSpots}・歩道 {BadSidewalk} / カオス 傾き {ChaosTilt}・横倒し {ChaosSide}・逆さ {ChaosFlip}・積み {ChaosStack}";
            }
        }
    }

    // 描画の直前に、ゲームが今回 Transform を書いた駐車中の車(Updated 付き)を拾う。次のフレームの ParkingSystem でかけ直す
    public partial class ParkingWatchSystem : GameSystemBase
    {
        private EntityQuery m_Query;
        private EntityQuery m_VisibleQuery;
        private HashSet<Entity> m_Known = new HashSet<Entity>();
        private HashSet<Entity> m_Now = new HashSet<Entity>();
        private double m_CountAt;
        private bool m_HaveBase;

        public static bool ResetCount = true;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_Query = GetEntityQuery(
                ComponentType.ReadOnly<ParkedCar>(),
                ComponentType.ReadOnly<Updated>(),
                ComponentType.ReadOnly<Transform>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());
            m_VisibleQuery = GetEntityQuery(
                ComponentType.ReadOnly<ParkedCar>(),
                ComponentType.Exclude<Unspawned>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>());
        }

        protected override void OnGameLoadingComplete(Colossal.Serialization.Entities.Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            ResetCount = true;
        }

        protected override void OnUpdate()
        {
#if DEBUG
            CountInOut(); // 開発用の数字なので公開版(Release)では数えない
#endif
            if (m_Query.IsEmptyIgnoreFilter)
            {
                return;
            }

            Setting s = Mod.Current;
            if (s == null || !s.AutoNew || !ParkingSystem.Active)
            {
                return;
            }

            NativeArray<Entity> arr = m_Query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < arr.Length; i++)
            {
                ParkingSystem.Pending.Add(arr[i]);
            }

            arr.Dispose();
        }

        private void CountInOut()
        {
            double now = UnityEngine.Time.realtimeSinceStartupAsDouble;
            if (ResetCount)
            {
                ResetCount = false;
                m_HaveBase = false;
                ParkingSystem.Arrived = 0;
                ParkingSystem.Departed = 0;
                m_CountAt = 0;
            }

            if (now - m_CountAt < 2.0)
            {
                return;
            }

            m_CountAt = now;
            NativeArray<Entity> arr = m_VisibleQuery.ToEntityArray(Allocator.Temp);
            m_Now.Clear();
            for (int i = 0; i < arr.Length; i++)
            {
                m_Now.Add(arr[i]);
            }

            arr.Dispose();
            if (m_HaveBase)
            {
                foreach (Entity e in m_Now)
                {
                    if (!m_Known.Contains(e))
                    {
                        ParkingSystem.Arrived++;
                    }
                }

                foreach (Entity e in m_Known)
                {
                    if (!m_Now.Contains(e))
                    {
                        ParkingSystem.Departed++;
                    }
                }
            }

            HashSet<Entity> t = m_Known;
            m_Known = m_Now;
            m_Now = t;
            m_HaveBase = true;
        }
    }
}
