"""更新固定版本的上游模板；开发时使用，不是 CLI 运行依赖。"""
import concurrent.futures
import json
import pathlib
import urllib.request

# 固定模板来源，避免维护脚本意外拉取不兼容的最新版本。
COMMIT = "8eff005219db1d17363add3ef4902e5b14798241"
# 仓库根目录，保证从任意工作目录运行时写入位置一致。
ROOT = pathlib.Path(__file__).resolve().parent.parent
# 对应固定提交的上游原始文件地址。
BASE = "https://raw.githubusercontent.com/electron-userland/electron-builder/" + COMMIT + "/"
# 用于筛选模板文件并计算本地相对路径的上游目录前缀。
PREFIX = "packages/app-builder-lib/templates/nsis/"


def read(url):
    """下载并返回原始字节，保留上游文件的编码和内容。"""
    with urllib.request.urlopen(url) as response:
        return response.read()


def fetch(item):
    """将 GitHub 文件树中的单个模板保存到仓库资源目录。"""
    path = item["path"]
    dest = ROOT / "src/DotnetNsis/Resources/nsis" / path[len(PREFIX):]
    dest.parent.mkdir(parents=True, exist_ok=True)
    dest.write_bytes(read(BASE + path))


if __name__ == "__main__":
    # 按固定提交枚举模板文件，再并行下载完整目录。
    tree = json.loads(read("https://api.github.com/repos/electron-userland/electron-builder/git/trees/" + COMMIT + "?recursive=1"))
    items = [x for x in tree["tree"] if x["type"] == "blob" and x["path"].startswith(PREFIX)]
    with concurrent.futures.ThreadPoolExecutor(max_workers=6) as pool:
        list(pool.map(fetch, items))
    license_path = ROOT / "src/DotnetNsis/Resources/ELECTRON-BUILDER-LICENSE.txt"
    license_path.write_bytes(read(BASE + "LICENSE"))
    # 上游实现仅存入开发缓存，便于核对宏、依赖版本及下载摘要。
    cache = ROOT / ".cache/upstream"
    cache.mkdir(parents=True, exist_ok=True)
    for name in ["NsisTarget.ts", "nsisScriptGenerator.ts", "nsisLang.ts", "nsisUtil.ts", "progId.ts"]:
        (cache / name).write_bytes(read(BASE + "packages/app-builder-lib/src/targets/win/nsis/" + name))
    for name in ["7zip.ts", "nsis.ts"]:
        (cache / name).write_bytes(read(BASE + "packages/app-builder-lib/src/toolsets/" + name))
    print("模板下载完成：", len(items))
