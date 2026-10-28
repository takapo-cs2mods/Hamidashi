// Hamidashi v1.1.0: パネルを開くボタン。ユニバーサルMODメニューには常に置き、設定で左上のアイコン列にも出す。
// 出典: CS2 公式 UI Modding https://cs2.paradoxwikis.com/UI_Modding
//       Button(variant="floating") の使い方は Hover Colors(River-Mochi, GPL-3.0)を参考
import React from "react";
import { bindValue, trigger, useValue } from "cs2/api";
import { Button, Tooltip } from "cs2/ui";

const G = "Hamidashi";
export const showTopLeft$ = bindValue<boolean>(G, "ShowTopLeft");
export const panelOpen$ = bindValue<boolean>(G, "PanelOpen");

// webpack が UI/images を Mods\Hamidashi\images にコピーする。MOD のファイルは coui://ui-mods/ から見える
const ICON = "coui://ui-mods/images/Hamidashi-icon.png";

const LauncherButton = () => {
  const open = useValue(panelOpen$);
  return (
    <Tooltip tooltip="Hamidashi: Imperfect Parking">
      <Button variant="floating" src={ICON} selected={open} onSelect={() => trigger(G, "Toggle")} />
    </Tooltip>
  );
};

export const UniversalLauncher = () => <LauncherButton />;

export const TopLeftLauncher = () => {
  const show = useValue(showTopLeft$);
  return show ? <LauncherButton /> : null;
};
