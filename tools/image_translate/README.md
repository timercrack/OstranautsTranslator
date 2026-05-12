# Image Translate Tool

这是一个本地批处理脚本：

1. 用 PaddleOCR 识别图片中的中文文本
2. 用 DeepSeek API 把识别到的文本批量翻译成目标语言
3. 用 Pillow 按 OCR 文本框把译文重新写回图片

它不是直接把图片发给 DeepSeek 做 OCR，也不是让 DeepSeek 直接返回改好的图片。DeepSeek 只负责文本翻译。

## 安装

先准备 Python 3.10+，然后安装依赖。

如果当前机器还没装 PaddlePaddle，先装它：

```powershell
pip install paddlepaddle
```

再安装脚本依赖：

```powershell
pip install -r tools/image_translate/requirements.txt
```

如果你要走 SOCKS5 代理，`requests[socks]` 已经包含在依赖里。

## 用法

单张图片：

```powershell
D:/Python314/python.exe tools/image_translate/translate_images.py \
  --input G:/path/to/source.png \
  --output G:/path/to/output.png \
  --target-language English \
  --api-key <DEEPSEEK_API_KEY> \
  --proxy http://127.0.0.1:20800
```

批量目录：

```powershell
D:/Python314/python.exe tools/image_translate/translate_images.py \
  --input G:/SteamLibrary/steamapps/common/Ostranauts/Ostranauts_Data/StreamingAssets/images \
  --output D:/tmp/translated-images \
  --target-language English \
  --api-key <DEEPSEEK_API_KEY> \
  --proxy http://127.0.0.1:20800 \
  --write-debug-json
```

如果你已经设置了环境变量 `DEEPSEEK_API_KEY`，可以省略 `--api-key`。

## 常用参数

- `--source-language`：源语言名称，默认是 `Chinese`
- `--target-language`：目标语言名称，必填
- `--ocr-language`：PaddleOCR 语言代码，默认 `ch`
- `--proxy`：HTTP 或 SOCKS5 代理，例如 `http://127.0.0.1:20800` 或 `socks5://127.0.0.1:20800`
- `--font-path`：重绘用字体，默认尝试 Windows 常见字体
- `--write-debug-json`：为每张输出图写一个 OCR/翻译结果 sidecar JSON

## 当前限制

- 当前版本用 OCR 的轴对齐包围盒重绘文本，不做复杂透视变换
- 对复杂背景，脚本只会用文本框区域的中位颜色盖掉原文，再重绘译文
- 更适合按钮、标签、面板标题、规则 UI 文本，不适合大段插画内嵌文本
- 如果背景复杂或字体风格要求高，建议先输出 debug JSON，再人工微调字体和框范围