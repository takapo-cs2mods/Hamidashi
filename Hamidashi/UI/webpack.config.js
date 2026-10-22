// Hamidashi UI のビルド設定。出力先は C# 側のMODと同じフォルダ (Mods\Hamidashi)。
// 注意: output.clean は使わない(同じフォルダの Hamidashi.dll を消さないため)。
const path = require("path");
const os = require("os");
const MOD = require("./mod.json");

const userData =
  process.env.CSII_USERDATAPATH ||
  path.join(os.homedir(), "AppData", "LocalLow", "Colossal Order", "Cities Skylines II");
const OUTPUT_DIR = path.join(userData, "Mods", MOD.id);

module.exports = {
  mode: "production",
  stats: "errors-warnings",
  entry: { [MOD.id]: "./src/index.tsx" },
  externalsType: "window",
  externals: {
    react: "React",
    "react-dom": "ReactDOM",
    "cs2/modding": "cs2/modding",
    "cs2/api": "cs2/api",
    "cs2/bindings": "cs2/bindings",
    "cs2/l10n": "cs2/l10n",
    "cs2/ui": "cs2/ui",
    "cs2/input": "cs2/input",
    "cs2/utils": "cs2/utils",
    "cohtml/cohtml": "cohtml/cohtml",
  },
  module: {
    rules: [
      {
        test: /\.tsx?$/,
        use: { loader: "ts-loader", options: { transpileOnly: true } },
        exclude: /node_modules/,
      },
    ],
  },
  resolve: { extensions: [".tsx", ".ts", ".js"] },
  output: {
    path: OUTPUT_DIR,
    filename: "[name].mjs",
    library: { type: "module" },
  },
  optimization: { minimize: false },
  experiments: { outputModule: true },
  plugins: [
    {
      apply(compiler) {
        compiler.hooks.done.tap("HamidashiDone", () => {
          // UI/images があれば MOD フォルダの images にコピーする
          const fs = require("fs");
          const src = path.join(__dirname, "images");
          const dst = path.join(OUTPUT_DIR, "images");
          if (fs.existsSync(src)) {
            fs.mkdirSync(dst, { recursive: true });
            for (const f of fs.readdirSync(src)) fs.copyFileSync(path.join(src, f), path.join(dst, f));
          }
          console.log("[Hamidashi UI] built -> " + OUTPUT_DIR);
        });
      },
    },
  ],
};
