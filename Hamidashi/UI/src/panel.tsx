// Hamidashi v1.1.0: 本番用のパネル(フローティング窓)。
// ・見出しをドラッグで移動、位置は保存(Setting.PanelX/Y)。画面外に出たら画面内に戻す(解像度を変えたとき用)。
// ・中身はオプション画面と同じ値(C# の DevUISystem.OptionsJson)。スライダーは離したときに送る(ドラッグ中に保存を連発しない)。
// ・下に「適用・シャッフル・元に戻す」。元に戻すは2回押し。
// ・ドラッグ処理は開発用の窓(devWindow.tsx)と同じ作り。
// 出典: CS2 公式 UI Modding(bindValue / useValue / trigger) https://cs2.paradoxwikis.com/UI_Modding
import React, { useEffect, useRef, useState } from "react";
import { bindValue, trigger, useValue } from "cs2/api";
import { Tooltip } from "cs2/ui";
import { panelOpen$ } from "./launcher";

const G = "Hamidashi";
const options$ = bindValue<string>(G, "Options");
const pos$ = bindValue<string>(G, "Pos");

type Opt = {
  name: string;
  label: string;
  desc?: string;
  group: string;
  kind: string;
  value?: string;
  min?: string;
  max?: string;
  step?: string;
  options?: { v: string; t: string }[];
};

const parse = (json: string, fallback: any) => {
  try {
    return json ? JSON.parse(json) : fallback;
  } catch (e) {
    return fallback;
  }
};

const C = {
  bg: "rgba(24,33,43,0.94)",
  head: "rgba(255,255,255,0.06)",
  text: "rgba(255,255,255,0.92)",
  dim: "rgba(255,255,255,0.55)",
  accent: "rgba(255,170,60,1)",
  track: "rgba(255,255,255,0.12)",
  ng: "rgba(235,90,80,1)",
  border: "rgba(255,255,255,0.14)",
};

const WIDTH_REM = 400;

// ---- 小物 ----
const Btn = ({ on, onClick, children, color, wide }: { on?: boolean; onClick: () => void; children: any; color?: string; wide?: boolean }) => (
  <div
    onClick={onClick}
    style={{
      padding: "4rem 10rem",
      margin: "0 4rem 4rem 0",
      borderRadius: "3rem",
      cursor: "pointer",
      textAlign: "center",
      flex: wide ? 1 : "0 0 auto",
      border: "1rem solid " + (on ? color || C.accent : C.border),
      background: on ? color || C.accent : "rgba(255,255,255,0.07)",
      color: on ? "rgba(0,0,0,0.9)" : C.text,
    }}
  >
    {children}
  </div>
);

const Label = ({ o, width }: { o: Opt; width?: string }) => {
  const body = <div style={{ width: width || "170rem", flex: "0 0 auto", color: C.dim, paddingRight: "8rem" }}>{o.label}</div>;
  return o.desc ? <Tooltip tooltip={o.desc}>{body}</Tooltip> : body;
};

const fmt = (v: number, step: number) => {
  const d = step >= 1 ? 0 : step >= 0.1 ? 1 : 2;
  return v.toFixed(d);
};

// スライダー: つまみをドラッグ、またはトラックをクリック。離したときに C# へ送る
const Slider = ({ o }: { o: Opt }) => {
  const min = Number(o.min);
  const max = Number(o.max);
  const step = Number(o.step || "1");
  const server = Number(o.value || "0");
  const [local, setLocal] = useState<number | null>(null);
  const track = useRef<HTMLDivElement>(null);
  const v = local !== null ? local : server;
  const ratio = max > min ? (v - min) / (max - min) : 0;

  const valueAt = (clientX: number) => {
    const r = track.current ? track.current.getBoundingClientRect() : null;
    if (!r || r.width <= 0) return v;
    const t = Math.min(1, Math.max(0, (clientX - r.left) / r.width));
    const raw = min + t * (max - min);
    return Number((Math.round(raw / step) * step).toFixed(3));
  };

  const onDown = (e: any) => {
    if (e.button !== undefined && e.button !== 0) return;
    let cur = valueAt(e.clientX);
    setLocal(cur);
    const move = (ev: any) => {
      cur = valueAt(ev.clientX);
      setLocal(cur);
    };
    const up = () => {
      document.removeEventListener("mousemove", move);
      document.removeEventListener("mouseup", up);
      trigger(G, "SetOption", o.name, String(cur));
      // C# の値が返ってくるまで少しだけ手元の値を見せる
      setTimeout(() => setLocal(null), 700);
    };
    document.addEventListener("mousemove", move);
    document.addEventListener("mouseup", up);
  };

  return (
    <div style={{ flex: 1, display: "flex", flexDirection: "row", alignItems: "center" }}>
      <div ref={track} onMouseDown={onDown} style={{ flex: 1, height: "18rem", display: "flex", alignItems: "center", cursor: "pointer" }}>
        <div style={{ position: "relative", width: "100%", height: "4rem", background: C.track, borderRadius: "2rem" }}>
          <div style={{ position: "absolute", left: 0, top: 0, height: "100%", width: ratio * 100 + "%", background: C.accent, borderRadius: "2rem" }} />
          <div
            style={{
              position: "absolute",
              left: ratio * 100 + "%",
              top: "50%",
              width: "12rem",
              height: "12rem",
              marginLeft: "-6rem",
              marginTop: "-6rem",
              borderRadius: "6rem",
              background: C.text,
            }}
          />
        </div>
      </div>
      <div style={{ width: "44rem", textAlign: "right", flex: "0 0 auto" }}>{fmt(v, step)}</div>
    </div>
  );
};

const Row = ({ children }: { children: any }) => (
  <div style={{ display: "flex", flexDirection: "row", alignItems: "center", marginBottom: "6rem" }}>{children}</div>
);

const OptionRow = ({ o }: { o: Opt }) => {
  if (o.kind === "enum") {
    // プリセット: 縦に並べる
    return (
      <div style={{ marginBottom: "6rem" }}>
        {(o.options || []).map((e) => (
          <div key={e.v} style={{ display: "flex" }}>
            <Btn wide on={o.value === e.v} onClick={() => trigger(G, "SetOption", o.name, e.v)}>
              <div style={{ textAlign: "left" }}>{e.t}</div>
            </Btn>
          </div>
        ))}
      </div>
    );
  }

  if (o.kind === "bool") {
    const on = o.value === "true";
    return (
      <Row>
        <Label o={o} width="290rem" />
        <div style={{ flex: 1 }} />
        <Btn on={on} onClick={() => trigger(G, "SetOption", o.name, on ? "false" : "true")}>
          {on ? "ON" : "OFF"}
        </Btn>
      </Row>
    );
  }

  if ((o.kind === "float" || o.kind === "int") && o.min !== undefined) {
    return (
      <Row>
        <Label o={o} />
        <Slider o={o} />
      </Row>
    );
  }

  return null;
};

// ---- 位置 ----
let collapsed = false;

const clampPos = (x: number, y: number, w: number, h: number) => {
  const W = window.innerWidth;
  const H = window.innerHeight;
  // 見出しの一部が必ず画面に残るように(はみ出しはOK、行方不明はNG)
  const nx = Math.min(Math.max(x, 40 - w), W - 80);
  const ny = Math.min(Math.max(y, 0), H - 40);
  return { x: Math.round(nx), y: Math.round(ny) };
};

export const HamidashiPanel = () => {
  const open = useValue(panelOpen$);
  const opts: Opt[] = parse(useValue(options$), []);
  const saved = (useValue(pos$) || "-1,-1").split(",").map((n: string) => Number(n));
  const [drag, setDrag] = useState<{ x: number; y: number } | null>(null);
  const [isCollapsed, setCollapsed] = useState(collapsed);
  const [confirm, setConfirm] = useState(false);
  const box = useRef<HTMLDivElement>(null);

  // 画面の大きさが変わったら描き直す(位置を画面内に戻すため)
  const [, setTick] = useState(0);
  useEffect(() => {
    const onResize = () => setTick((t) => t + 1);
    window.addEventListener("resize", onResize);
    return () => window.removeEventListener("resize", onResize);
  }, []);

  if (!open) return null;

  const w = box.current ? box.current.offsetWidth : 400;
  const h = box.current ? box.current.offsetHeight : 600;
  const base =
    drag ||
    (saved[0] >= 0 && saved[1] >= 0
      ? { x: saved[0], y: saved[1] }
      : { x: window.innerWidth - w - 100, y: Math.round(window.innerHeight * 0.12) });
  const p = clampPos(base.x, base.y, w, h);

  const onDragStart = (e: any) => {
    if (e.button !== undefined && e.button !== 0) return;
    const sx = e.clientX;
    const sy = e.clientY;
    const ox = p.x;
    const oy = p.y;
    let last = { x: ox, y: oy };
    const move = (ev: any) => {
      last = clampPos(ox + ev.clientX - sx, oy + ev.clientY - sy, w, h);
      setDrag(last);
    };
    const up = () => {
      document.removeEventListener("mousemove", move);
      document.removeEventListener("mouseup", up);
      trigger(G, "SavePos", last.x, last.y);
      setTimeout(() => setDrag(null), 700);
    };
    document.addEventListener("mousemove", move);
    document.addEventListener("mouseup", up);
  };

  const buttons = opts.filter((o) => o.kind === "button");
  const btn = (name: string) => buttons.find((o) => o.name === name);
  const label = (name: string, fallback: string) => (btn(name) ? btn(name).label : fallback);

  const restore = () => {
    if (!confirm) {
      setConfirm(true);
      setTimeout(() => setConfirm(false), 3000);
      return;
    }
    setConfirm(false);
    trigger(G, "PressOption", "Restore");
  };

  // グループごとに並べる(ボタンは下のバーに出すので除く)
  const rows: any[] = [];
  let lastGroup = "";
  opts
    .filter((o) => o.kind !== "button")
    .forEach((o) => {
      if (o.group !== lastGroup) {
        rows.push(
          <div key={"g" + o.name} style={{ color: C.accent, fontWeight: "bold", margin: rows.length ? "10rem 0 6rem" : "0 0 6rem" }}>
            {o.group}
          </div>
        );
        lastGroup = o.group;
      }
      rows.push(<OptionRow key={o.name} o={o} />);
    });

  return (
    <div
      ref={box}
      style={{
        position: "absolute",
        left: p.x + "px",
        top: p.y + "px",
        width: WIDTH_REM + "rem",
        maxHeight: "80vh",
        display: "flex",
        flexDirection: "column",
        background: C.bg,
        color: C.text,
        border: "1rem solid " + C.border,
        borderRadius: "6rem",
        fontSize: "13rem",
        pointerEvents: "auto",
      }}
    >
      {/* 見出し(ここを掴んで移動) */}
      <div
        onMouseDown={onDragStart}
        style={{ display: "flex", flexDirection: "row", alignItems: "center", padding: "6rem 8rem 6rem 12rem", background: C.head, cursor: "move", flex: "0 0 auto" }}
      >
        <div style={{ fontWeight: "bold", flex: 1 }}>Hamidashi</div>
        <div
          onMouseDown={(e: any) => e.stopPropagation()}
          onClick={() => {
            collapsed = !isCollapsed;
            setCollapsed(collapsed);
          }}
          style={{ width: "24rem", textAlign: "center", cursor: "pointer", color: C.dim }}
        >
          {isCollapsed ? "+" : "−"}
        </div>
        <div onMouseDown={(e: any) => e.stopPropagation()} onClick={() => trigger(G, "Close")} style={{ width: "24rem", textAlign: "center", cursor: "pointer", color: C.dim }}>
          ×
        </div>
      </div>

      {/* 中身(折りたたんでも状態は消さない) */}
      <div style={{ flex: 1, minHeight: 0, overflowY: "scroll", padding: "10rem 12rem", display: isCollapsed ? "none" : "flex", flexDirection: "column" }}>{rows}</div>

      {/* 実行バー(折りたたんでも出しておく) */}
      <div style={{ display: "flex", flexDirection: "row", padding: "8rem 8rem 4rem 12rem", borderTop: "1rem solid " + C.border, flex: "0 0 auto" }}>
        <Btn wide on onClick={() => trigger(G, "PressOption", "ApplyNow")}>
          {label("ApplyNow", "Apply")}
        </Btn>
        <Btn wide onClick={() => trigger(G, "PressOption", "Shuffle")}>
          {label("Shuffle", "Shuffle")}
        </Btn>
        <Btn wide on={confirm} color={C.ng} onClick={restore}>
          {confirm ? "OK?" : label("Restore", "Reset")}
        </Btn>
      </div>
    </div>
  );
};
