// ゲームが実行時に提供するモジュールの、簡易的な型宣言(中身は any)。
declare module "cs2/api" {
  export const bindValue: any;
  export const trigger: any;
  export const useValue: any;
}
declare module "cs2/modding" {
  export type ModRegistrar = (moduleRegistry: any) => void;
}
