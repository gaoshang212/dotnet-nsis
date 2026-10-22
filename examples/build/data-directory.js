// 保存 NSIS 分块传入的目录文本。
let input = "";
// 保存经过校验的数据目录。
let directory = "";
// 配置路径相对当前安装目录，由宿主设置为文件访问根目录。
const configPath = "data-directory.json";

// 开始一次读取或写入，清除上次输入。
function resetInput() {
    input = "";
}

// 追加 NSIS 传入的文本片段，避免超过栈字符串容量。
function appendText(text) {
    input += text;
    if (input.length > 1023) throw new Error("数据目录过长。");
}

// 校验绝对路径，拒绝损坏配置及 NSIS 无法完整保存的路径。
function validateDirectory(value) {
    if (typeof value !== "string" || value.trim() === "" || value.length > 1023 ||
        /[\x00-\x1f"<>|]/.test(value) || !value.isWellFormed() ||
        !/^(?:[A-Za-z]:[\\/]|[\\/]{2}[^\\/]+[\\/][^\\/]+)/.test(value)) {
        throw new Error("数据目录必须是有效的绝对路径。");
    }
    return value;
}

// 直接读取旧配置；首次安装返回空目录，由安装界面提供默认值。
function readConfig() {
    directory = "";
    if (!fs.exists(configPath)) return;
    const info = fs.stat(configPath);
    if (!info.isFile || info.size > 16384) throw new Error("配置必须是不超过 16 KiB 的文件。");
    const text = fs.readText(configPath).replace(/^\uFEFF/, "");
    directory = validateDirectory(JSON.parse(text)?.dataDirectory);
}

// 分块返回无反斜杠的目录；合法路径不含 JSON 需要转义的其他字符。
function directoryChunk(offset) {
    return Array.from(directory.replace(/\\/g, "/")).slice(offset, offset + 128).join("");
}

// 校验、序列化并直接写入 UTF-8 JSON，中文保持原文。
function writeConfig() {
    directory = validateDirectory(input);
    fs.writeText(configPath, JSON.stringify({ dataDirectory: directory }, null, 2) + "\n");
}
