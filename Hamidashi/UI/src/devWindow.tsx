// Hamidashi v0.1: 開発用の別窓(Debug ビルドで、開発者モードのときだけ)。Mikiri v0.9.45 の窓と同じ作り。
// ・完全フローティング(見出しドラッグで移動)、閉じるではなく最小化(中身を保持)。位置はゲームを閉じるまで覚えておく。
// ・左 = 操作(適用・シャッフル・元に戻す・情報をログへ)と状態、オプション画面の再現。右 = チェックリスト。
// 出典: CS2 公式 UI Modding(bindValue / useValue / trigger / moduleRegistry.append) https://cs2.paradoxwikis.com/UI_Modding
import React, { useState } from "react";
import { bindValue, trigger, useValue } from "cs2/api";

const G = "HamidashiDev";

const devMode$ = bindValue<boolean>(G, "DevMode");
const open$ = bindValue<boolean>(G, "Open");
const version$ = bindValue<string>(G, "Version");
const status$ = bindValue<string>(G, "Status");
const options$ = bindValue<string>(G, "Options");
const checklist$ = bindValue<string>(G, "Checklist");

const parse = (json: string, fallback: any) => {
  try {
    return json ? JSON.parse(json) : fallback;
  } catch (e) {
    return fallback;
  }
};

const C = {
  bg: "rgba(18,24,32,0.92)",
  panel: "rgba(255,255,255,0.06)",
  text: "rgba(255,255,255,0.92)",
  dim: "rgba(255,255,255,0.5)",
  accent: "rgba(255,170,60,1)",
  ok: "rgba(90,200,120,1)",
  ng: "rgba(235,90,80,1)",
  border: "rgba(255,255,255,0.15)",
};

const sWindow: any = {
  position: "absolute",
  width: "900rem",
  display: "flex",
  flexDirection: "column",
  background: C.bg,
  color: C.text,
  borderRadius: "6rem",
  padding: "10rem 12rem",
  fontSize: "13rem",
  pointerEvents: "auto",
  border: "1rem solid " + C.border,
};
const sSection: any = { background: C.panel, borderRadius: "4rem", padding: "8rem", marginTop: "8rem" };
const sH: any = { fontWeight: "bold", color: C.accent, marginBottom: "6rem" };
const sRow: any = { display: "flex", flexDirection: "row", alignItems: "center", marginBottom: "5rem", flexWrap: "wrap" };
const sLabel: any = { width: "190rem", color: C.dim, flex: "0 0 auto" };
const sInput: any = {
  flex: 1,
  background: "rgba(0,0,0,0.4)",
  color: C.text,
  border: "1rem solid " + C.border,
  borderRadius: "3rem",
  padding: "3rem 6rem",
  fontSize: "13rem",
};

const Btn = ({ on, onClick, children, color, title }: { on?: boolean; onClick: () => void; children: any; color?: string; title?: string }) => (
  <div
    title={title}
    onClick={onClick}
    style={{
      padding: "3rem 9rem",
      marginRight: "4rem",
      marginBottom: "2rem",
      borderRadius: "3rem",
      cursor: "pointer",
      border: "1rem solid " + (on ? color || C.accent : C.border),
      background: on ? color || C.accent : "rgba(255,255,255,0.08)",
      color: on ? "rgba(0,0,0,0.9)" : C.text,
    }}
  >
    {children}
  </div>
);

// 入力中のキーをゲームのショートカットに渡さない
const stopKeys = (e: any) => {
  if (e && e.stopPropagation) e.stopPropagation();
};

const now = () => {
  const d = new Date();
  const p2 = (n: number) => (n < 10 ? "0" : "") + n;
  return p2(d.getHours()) + ":" + p2(d.getMinutes()) + ":" + p2(d.getSeconds());
};

// ---- 操作と状態 ----
const ActionSection = () => {
  const st = parse(useValue(status$), {});
  const [pressed, setPressed] = useState("");
  const press = (name: string, label: string) => {
    trigger(G, name);
    setPressed(label + " " + now());
  };
  return (
    <div style={sSection}>
      <div style={sH}>操作</div>
      <div style={sRow}>
        <Btn on onClick={() => press("Apply", "適用")}>適用</Btn>
        <Btn onClick={() => press("Shuffle", "シャッフル")}>シャッフル</Btn>
        <Btn onClick={() => press("Restore", "元に戻す")} title="全部の車をゲーム本来の位置へ。次に「適用」するまで新しく停まった車にもかけない">
          元に戻す
        </Btn>
        <Btn onClick={() => press("Info", "情報をログへ")} title="駐車レーンの種類ごとの台数・枠の角度と大きさをログに書く(PLT の確認用)">
          情報をログへ
        </Btn>
        <div style={{ color: C.dim }}>{pressed ? "押した: " + pressed : ""}</div>
      </div>
      <div style={sRow}>
        <div style={sLabel}>プリセット / 乱数</div>
        <div>
          {st.preset || "-"} / {st.seed}
        </div>
      </div>
      <div style={sRow}>
        <div style={sLabel}>自動(新しく停まった車)</div>
        <div style={{ color: st.active ? C.text : C.ng }}>{st.active ? "かけた " + (st.auto || 0) + " 台" : "止めている(「元に戻す」のあと。次の「適用」で再開)"}</div>
      </div>
      <div style={sRow}>
        <div style={sLabel}>見えている車の出入り</div>
        <div>
          停まりに来た {st.arrived || 0} 台 / 出ていった {st.departed || 0} 台(ロードしてから・2秒ごと)
        </div>
      </div>
      {st.running ? <div style={{ color: C.accent }}>実行中: {st.running}</div> : null}
      <div style={{ color: C.dim, fontSize: "11rem", whiteSpace: "pre-wrap", wordBreak: "break-all" }}>{st.last ? "前回: " + st.last : "(まだ実行していない)"}</div>
    </div>
  );
};

// ---- オプション画面の再現(Mikiri v0.9.45 と同じ。値を変えたら「適用」で街に反映) ----
type Opt = { name: string; label: string; group: string; kind: string; value?: string; min?: string; max?: string; step?: string; options?: { v: string; t: string }[] };

const NumberEdit = ({ value, onCommit }: { value: string; onCommit: (v: string) => void }) => {
  const [text, setText] = useState<string | null>(null);
  const commit = () => {
    if (text !== null && text.trim() !== "" && !isNaN(Number(text))) onCommit(String(Number(text)));
    setText(null);
  };
  return (
    <input
      type="text"
      style={{ ...sInput, flex: "0 0 auto", width: "70rem" }}
      value={text !== null ? text : value}
      onFocus={() => setText(value)}
      onChange={(e: any) => setText(e.target.value)}
      onBlur={commit}
      onKeyDown={(e: any) => {
        stopKeys(e);
        if (e.key === "Enter" || e.keyCode === 13) {
          commit();
          e.target.blur?.();
        }
      }}
    />
  );
};

const OptionsSection = () => {
  const opts: Opt[] = parse(useValue(options$), []);
  const [confirm, setConfirm] = useState("");
  let lastGroup = "";
  return (
    <div style={sSection}>
      <div style={sH}>オプション(設定画面の再現)</div>
      <div style={{ color: C.dim, fontSize: "11rem", marginBottom: "4rem" }}>設定画面と同じ値を読み書きする。街に反映するのは「適用」。ボタンは2回押しで実行</div>
      {opts.map((o) => {
        const head =
          o.group !== lastGroup ? (
            <div key={"g" + o.name} style={{ ...sH, marginTop: "8rem", color: C.dim }}>
              {o.group || "-"}
            </div>
          ) : null;
        lastGroup = o.group;
        let control: any = null;
        if (o.kind === "bool") {
          const on = o.value === "true";
          control = (
            <Btn on={on} onClick={() => trigger(G, "SetOption", o.name, on ? "false" : "true")}>
              {on ? "ON" : "OFF"}
            </Btn>
          );
        } else if (o.kind === "float" || o.kind === "int") {
          const v = Number(o.value || "0");
          const step = Number(o.step || (o.kind === "int" ? "1" : "0.1"));
          const min = o.min !== undefined ? Number(o.min) : -1e9;
          const max = o.max !== undefined ? Number(o.max) : 1e9;
          const clamp = (x: number) => Math.min(max, Math.max(min, Math.round(x / step) * step));
          const send = (x: number) => trigger(G, "SetOption", o.name, String(Number(clamp(x).toFixed(3))));
          control = (
            <>
              <Btn onClick={() => send(v - step)}>−</Btn>
              <NumberEdit value={o.value || "0"} onCommit={(x) => send(Number(x))} />
              <Btn onClick={() => send(v + step)}>+</Btn>
              {o.min !== undefined && (
                <div style={{ color: C.dim, marginLeft: "6rem" }}>
                  {o.min}〜{o.max}
                </div>
              )}
            </>
          );
        } else if (o.kind === "enum") {
          control = (o.options || []).map((e) => (
            <Btn key={e.v} on={o.value === e.v} onClick={() => trigger(G, "SetOption", o.name, e.v)}>
              {e.t}
            </Btn>
          ));
        } else if (o.kind === "button") {
          const armed = confirm === o.name;
          control = (
            <Btn
              on={armed}
              color={C.ng}
              onClick={() => {
                if (!armed) {
                  setConfirm(o.name);
                  setTimeout(() => setConfirm(""), 3000);
                  return;
                }
                setConfirm("");
                trigger(G, "PressOption", o.name);
              }}
            >
              {armed ? "もう一度押すと実行" : "実行"}
            </Btn>
          );
        }
        return (
          <React.Fragment key={o.name}>
            {head}
            <div style={sRow}>
              <div style={sLabel} title={o.name}>
                {o.label}
              </div>
              {control}
            </div>
          </React.Fragment>
        );
      })}
    </div>
  );
};

// ---- チェックリスト ----
type Result = { r: string; note: string };

const ChecklistSection = ({ setSubmit }: { setSubmit: (fn: () => void) => void }) => {
  const cl = parse(useValue(checklist$), { title: "", items: [] });
  const version = useValue(version$);
  const [results, setResults] = useState<{ [id: string]: Result }>({});
  const [memo, setMemo] = useState("");
  const [forTitle, setForTitle] = useState("");

  // 項目ファイルが変わったら入力を空にする
  if (forTitle !== cl.title) {
    setForTitle(cl.title);
    setResults({});
    setMemo("");
  }

  const set = (id: string, patch: Partial<Result>) => {
    const cur = results[id] || { r: "", note: "" };
    setResults({ ...results, [id]: { ...cur, ...patch } });
  };

  const submit = () => {
    const items = (cl.items || []).map((it: any) => ({ id: it.id, text: it.text, result: (results[it.id] || {}).r || "", note: (results[it.id] || {}).note || "" }));
    trigger(G, "SubmitChecklist", JSON.stringify({ version, title: cl.title, items, memo }));
  };

  setSubmit(submit);

  return (
    <div style={sSection}>
      <div style={sRow}>
        <div style={{ ...sH, flex: 1, marginBottom: 0 }}>チェックリスト</div>
        <Btn onClick={() => trigger(G, "ReloadChecklist")} title="項目ファイルを読み直す">
          読み直し
        </Btn>
      </div>
      <div style={{ color: C.dim, marginBottom: "6rem" }}>{cl.title || "(項目なし)"}</div>
      {(cl.items || []).map((it: any) => {
        const res = results[it.id] || { r: "", note: "" };
        return (
          <div key={it.id} style={{ borderTop: "1rem solid " + C.border, paddingTop: "6rem", marginTop: "6rem" }}>
            <div style={{ marginBottom: "4rem" }}>
              <span style={{ color: C.accent }}>{it.id}. </span>
              {it.text}
            </div>
            <div style={sRow}>
              <Btn on={res.r === "OK"} color={C.ok} onClick={() => set(it.id, { r: res.r === "OK" ? "" : "OK" })}>
                OK
              </Btn>
              <Btn on={res.r === "NG"} color={C.ng} onClick={() => set(it.id, { r: res.r === "NG" ? "" : "NG" })}>
                NG
              </Btn>
              <Btn on={res.r === "-"} onClick={() => set(it.id, { r: res.r === "-" ? "" : "-" })}>
                未確認
              </Btn>
            </div>
            <input type="text" style={{ ...sInput, width: "100%" }} value={res.note} placeholder="ひとこと(任意)" onChange={(e: any) => set(it.id, { note: e.target.value })} onKeyDown={stopKeys} />
          </div>
        );
      })}
      <div style={{ marginTop: "10rem", color: C.dim }}>備考</div>
      <textarea style={{ ...sInput, width: "100%", height: "60rem" }} value={memo} onChange={(e: any) => setMemo(String(e.target.value))} onKeyDown={stopKeys} />
      <div style={{ height: "40rem" }} />
    </div>
  );
};

let submitFn: () => void = () => {};
let winPos = { x: -1, y: 90 };

export const DevWindow = () => {
  const devMode = useValue(devMode$);
  const open = useValue(open$);
  const version = useValue(version$);
  const [sentMark, setSentMark] = useState("");
  const [pos, setPosState] = useState(winPos);
  if (!devMode) return null;

  const setPos = (p: { x: number; y: number }) => {
    winPos = p;
    setPosState(p);
  };
  const x = pos.x < 0 ? Math.max(0, window.innerWidth - 960) : pos.x;

  // 見出しをドラッグで移動
  const onDragStart = (e: any) => {
    if (e.button !== undefined && e.button !== 0) return;
    const sx = e.clientX;
    const sy = e.clientY;
    const ox = x;
    const oy = pos.y;
    const move = (ev: any) => setPos({ x: Math.max(0, ox + ev.clientX - sx), y: Math.max(0, oy + ev.clientY - sy) });
    const up = () => {
      document.removeEventListener("mousemove", move);
      document.removeEventListener("mouseup", up);
    };
    document.addEventListener("mousemove", move);
    document.addEventListener("mouseup", up);
  };

  const sendNow = () => {
    submitFn();
    setSentMark(now());
  };

  return (
    <div style={{ ...sWindow, left: x + "px", top: pos.y + "px", height: open ? "780rem" : "auto" }}>
      <div style={{ ...sRow, flex: "0 0 auto", cursor: "move" }} onMouseDown={onDragStart}>
        <div style={{ fontWeight: "bold", marginRight: "12rem" }}>Hamidashi Dev {version}</div>
        <Btn on onClick={sendNow} title="チェックリストの結果を送る">
          送信
        </Btn>
        <Btn onClick={() => trigger(G, "Toggle")}>{open ? "最小化" : "広げる"}</Btn>
        <div style={{ flex: 1 }} />
        <div style={{ color: C.dim, fontSize: "11rem" }}>見出しをドラッグで移動</div>
      </div>
      {sentMark && <div style={{ color: C.ok, flex: "0 0 auto" }}>送信しました {sentMark}(スレには「試した」だけでOK)</div>}
      {/* 最小化しても中身は消さない(display だけ切り替えて、チェックリストの入力を保持) */}
      <div style={{ flex: 1, minHeight: 0, display: open ? "flex" : "none", flexDirection: "row", marginTop: "4rem" }}>
        <div style={{ flex: 1, overflowY: "scroll", minHeight: 0, marginRight: "10rem" }}>
          <ActionSection />
          <OptionsSection />
        </div>
        <div style={{ width: "400rem", flex: "0 0 auto", overflowY: "scroll", minHeight: 0 }}>
          <ChecklistSection setSubmit={(fn) => (submitFn = fn)} />
        </div>
      </div>
    </div>
  );
};
