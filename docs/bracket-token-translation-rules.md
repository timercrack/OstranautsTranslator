# Ostranauts 方括号 Token 扫描与翻译规范

> 适用范围：`StreamingAssets/data` 扫描、`native_mod_source` / `runtime_source` 入库、`source` 导出、`translate` 翻译、`native-mod` 回写。
>
> 本文目标不是描述“所有可能字符串”，而是统一 **哪些方括号内容是真实运行时模板**、**哪些字符串根本不该翻**、以及 **中文翻译时哪些 token 必须保留、哪些应该改写掉**。

## 目的

当前仓库同时处理两类文本来源：

- `native_mod_source`：来自 `Ostranauts_Data/StreamingAssets/data` 的原始数据文本
- `runtime_source`：运行时捕获的漏网文本

`OstranautsTranslator` 自己的 `RuntimeTextProjector` **不会**解析 `[...]` 方括号 token；数据库里保留的 `raw_text/runtime_key/render_key` 只是原样记录。真正解析这些 token 的是游戏本体：

- 互动/语法系统：`DataHandler` + `GrammarUtils` + `Interaction`
- Plot 系统：`JsonPlotSave` + `PlotManager`
- 个别手工替换路径：例如 `Interaction.cs` 里的 `Replace("[object]", ...)`

因此，凡是含有方括号的字符串，都不能靠“看起来像英文句子”来决定翻译策略，必须先做分类。

## 当前已验证的 token 家族

### 1. 实体槽位 token（必须保留）

这类 token 指向人物、对象或运行时插值槽位；**保留原 token 文本和方括号**，允许调整中文语序，但禁止改名。

已验证示例：

- `[us]`
- `[them]`
- `[3rd]`
- `[protag]`
- `[contact]`
- `[target]`
- `[object]`
- `[prereq0]`、`[prereq1]`、...

其中：

- `[us]` / `[them]` / `[3rd]` 走 `Interaction` / `GrammarUtils`
- `[protag]` / `[contact]` / `[target]` 走 `JsonPlotSave.dictCOTokens`
- `[object]`、`[prereqN]` 走手工 `Replace(...)`

### 2. 英文语法/代词 token（原则上改写掉）

这类 token 是英文语法引擎用的，不适合直接原封不动留在中文里。它们虽然是合法 token，但如果保留到最终中文译文，游戏往往会输出英文代词或英文缩写。

已验证示例：

- `[us-subj]`
- `[us-obj]`
- `[us-pos]`
- `[us-reflexive]`
- `[us-contractIs]`
- `[us-contractHas]`
- `[us-contractWill]`
- `[us-fullName]`
- `them` / `3rd` 的对应变体

中文规则：

- **优先改写句子**，把它们改写成自然中文
- 必要时可以改写成基础实体槽位，例如把 `[them-pos] ship` 改成 `[them] 的船`
- **不要**在最终中文里保留英文代词 token，除非已经人工确认该句必须依赖原英语语法输出

### 3. 动词 token（原则上改写掉）

这类 token 来自 `StreamingAssets/data/verbs/verbs.json`，由 `DataHandler.dictVerbs` 加载。

已验证示例：

- `[messages]`
- `[misses]`
- 以及 `verbs.json` 中的其他条目

中文规则：

- 这类 token **不是普通单词**，而是英文动词变形入口
- 中文通常不应保留成 bracket token，而应吸收到自然句式里
- 例如：
  - 原文：`[them] [messages] [us], asking for a photo of [3rd].`
  - 推荐：`[them] 发来消息，请 [us] 拍一张 [3rd] 的照片。`
- **不要**把 `[messages]` 翻成 `[消息]` 或 `[发消息]`

### 4. Plot 控制字符串（禁止翻译）

这类字符串不是显示文案，而是 “interaction 名 + token 参数” 的机器控制串。

已验证示例：

- `PLOTSnapPhotoTarget,[protag],[target],[contact]`
- `PLOT_Messenger_Offer,[protag],[contact],[target]`
- `PLOTTEMP3ActBEGTarget,[protag],[target],[contact]`

中文规则：

- **整条禁止翻译**
- interaction 名、逗号、token 顺序都必须保持原样
- 这类字符串应视为 `scan` 的非可翻译项，或至少标成 `review/ignore`

### 5. 自定义 token（基础游戏已存在，MOD 仍可扩展）

游戏加载流程里存在 `tokens/` 目录和 `JsonCustomTokens` / `listCustomTokens` 机制；当前基础游戏已经包含可枚举的 `StreamingAssets/data/tokens/*.json`，例如：

- `tokens/specialCases.json`
- `tokens/shipComms.json`
- `tokens/partsOfSpeech.json`

这些文件里已验证的 token 包括：

- `object`
- `prereq0`
- `us-regID` / `them-shipfriendly` / `3rd-captain`
- `us-friendly` / `them-fullName` / `3rd-friendly`
- `data`

因此：

- 本文列出的 token 集合是 **当前已验证集合**，已经覆盖基础游戏现有 `verbs/` 与 `tokens/` 数据
- 但它仍然不是语言层面的闭集；后续如果 MOD 再注入新的 `tokens/*.json`，默认按 **“先枚举、再分类、再决定保留/改写”** 处理

## 统一判定流程

看到含 `[...]` 的文本时，按下面顺序判断：

1. **是否是控制字符串而不是显示文案？**
   - 典型特征：`InteractionName,[token],[token]...`
   - 如果是：**禁止翻译**
2. **是否包含已验证的实体槽位 / plot 槽位 / 手工替换槽位？**
   - 如果是：**允许翻译正文，但 token 必须原样保留**
3. **是否包含已验证的英文语法/动词 token？**
   - 如果是：**允许翻译，但应优先把这类 token 改写掉**
4. **是否包含未验证 token？**
   - 如果是：**不要自动定稿，转人工审查**

## 给 `scan` / `source` / `translate` 的统一口径

### `scan`

### 必须采集为可翻译项

- 白名单显示字段中的自然语言文本
- 即使含有以下 token，也仍属于可翻译文本：
  - 实体槽位 token
  - plot 槽位 token
  - 手工替换 token
  - 英文语法/动词 token（但后续翻译策略不同）

### 必须排除或标记为不可翻译项

- Plot 控制字符串
- interaction / plot / asset 标识符
- 任何“逗号分隔参数串”且语义明显为机器调用约定的字段

### 后续建议的统一标签

后续若要把本文直接接进扫描元数据，建议在 `metadataJson` 里统一输出如下标签：

- `token_policy = preserve-slot`
- `token_policy = rewrite-grammar`
- `token_policy = skip-control`
- `token_policy = review-unknown`

对应含义：

- `preserve-slot`：可以翻译正文，但 bracket token 必须原样保留
- `rewrite-grammar`：可以翻译，但应优先改写掉英文语法/动词 token
- `skip-control`：整条禁止翻译
- `review-unknown`：存在未验证 token，需人工确认

### `source`

当前导出 JSONL 还没有显式输出 token 规则字段。后续如需接入，推荐增加：

- `token_policy`
- `token_examples`
- `needs_manual_review`

在此之前，人工翻译和 LLM 翻译应以本文为准。

### `translate`

建议把下列规则固化到系统提示词或翻译前检查：

1. **绝不要输出中文方括号 token 名**
   - 错误：`[联系人]`、`[目标]`
2. **保留型 token 必须逐字符保留**
   - `[us]` 不能改成 `[Us]`、`[ us ]` 或 `[them]`
3. **语法/动词 token 可以消失，但不能被翻成中文 bracket token**
   - `[messages]` 可以被改写成“发来消息”
   - 不能改成 `[发来消息]`
4. **控制字符串整条跳过**
5. **未知 token 一律人工复核，不自动定稿**

## 显式异常 token 映射

当前基础游戏数据里还存在少量可以确认语义、但拼写或词形明显异常的 token。它们不再按“未知 token”直接打回，而是走**显式映射表**，便于审计与复现。

当前已收录：

- `[they-obj]` → `[them-obj]`
- `[us-contractHave]` → `[us-contractHas]`
- `[us-contractWould]` → `[us-contractWill]`
- `[them-contractWould]` → `[them-contractWill]`
- `[us-walks]` → 视为缺失 verb 的英语动词辅助 token，按 `rewrite-grammar` 处理

处理规则：

- 这些映射只针对**已确认异常的孤例**，不是模糊 fallback
- `translate` 会把映射关系显式发给模型
- 如果最终译文仍需保留 bracket token，应优先使用 canonical token；如果该 token 属于语法/动词类，则仍然应优先改写成自然中文

## 中文翻译硬规则

### 必须保留原样的 token

- `[us]`
- `[them]`
- `[3rd]`
- `[protag]`
- `[contact]`
- `[target]`
- `[object]`
- `[prereqN]`
- 以及后续确认过的自定义槽位 token

### 原则上应改写掉的 token

- `[us-subj]` / `[them-obj]` / `[3rd-pos]` 等英文语法 token
- `[messages]` / `[misses]` 等英文动词 token

### 绝对不能翻的字符串

- `InteractionName,[token],[token]...` 这类控制串
- 各类 ID、名称键、机读协议串

## 正反例

### 正例 1：实体槽位保留

- 原文：`[contact] wants a photo of [target].`
- 推荐：`[contact] 想要一张 [target] 的照片。`

### 正例 2：plot token 保留，语序中文化

- 原文：`Return to [contact] with the photo of [target].`
- 推荐：`带着 [target] 的照片回去交给 [contact]。`

### 正例 3：动词 token 改写掉

- 原文：`[them] [messages] [us], asking for a photo of [3rd].`
- 推荐：`[them] 发来消息，请 [us] 拍一张 [3rd] 的照片。`

### 正例 4：英文所有格改写成中文结构

- 原文：`[contact]'s plan`
- 推荐：`[contact] 的计划`

### 反例 1：把 token 翻成中文

- 错误：`[联系人] 想要一张 [目标] 的照片。`

### 反例 2：保留英文语法 token 不处理

- 不推荐：`[them] [messages] [us]，请求拍摄 [3rd]。`

### 反例 3：翻译控制字符串

- 错误：`拍照任务目标,[主角],[目标],[联系人]`

## 对 `runtime_source` 的补充要求

`runtime_source` 里的 bracket token 仍是原样捕获，因为当前 `RuntimeTextProjector` 不处理方括号模板。对这类文本应追加以下规则：

- 若能回溯到 `native_mod_source` / interaction / plot 原文，优先按源数据规则处理
- 若包含未验证 token，不要直接自动定稿
- 若只是把英文语法 token 生硬保留到了中文里，视为低质量翻译，需要人工修订

## 与当前代码实现的关系

当前仓库已具备以下行为：

- `OstranautsDataScanner` 通过目录/字段白名单决定哪些 JSON 字段入库
- `native_mod_source.is_translatable` 会影响：
  - `source`
  - `native-mod`
  - 运行时 native source ignore 逻辑
- `NativeModExporter` 只会 patch `is_translatable = 1` 的项
- `RuntimeTranslationCatalog` 会先检查“未翻译或 ignore 的 native mod source”，命中后直接忽略 runtime lookup

这意味着：

- 本规范首先是**人工与流程规则**
- 下一步若要程序化接入，最合适的落点是：
  - `OstranautsDataScanner`：补充 `token_policy`
  - `SourceExporter`：把 `token_policy` 带到 JSONL
  - `translate`：按 `token_policy` 选择提示词或跳过策略

## 证据来源

已核对的主要代码与数据位置：

- `g:/SteamLibrary/steamapps/common/Ostranauts/dnspy/Assembly-CSharp/DataHandler.cs`
- `g:/SteamLibrary/steamapps/common/Ostranauts/dnspy/Assembly-CSharp/GrammarUtils.cs`
- `g:/SteamLibrary/steamapps/common/Ostranauts/dnspy/Assembly-CSharp/Interaction.cs`
- `g:/SteamLibrary/steamapps/common/Ostranauts/dnspy/Assembly-CSharp/JsonPlotSave.cs`
- `g:/SteamLibrary/steamapps/common/Ostranauts/dnspy/Assembly-CSharp/PlotManager.cs`
- `g:/SteamLibrary/steamapps/common/Ostranauts/dnspy/Assembly-CSharp/JsonCustomTokens.cs`
- `g:/SteamLibrary/steamapps/common/Ostranauts/Ostranauts_Data/StreamingAssets/data/tokens/specialCases.json`
- `g:/SteamLibrary/steamapps/common/Ostranauts/Ostranauts_Data/StreamingAssets/data/tokens/shipComms.json`
- `g:/SteamLibrary/steamapps/common/Ostranauts/Ostranauts_Data/StreamingAssets/data/tokens/partsOfSpeech.json`
- `g:/SteamLibrary/steamapps/common/Ostranauts/Ostranauts_Data/StreamingAssets/data/verbs/verbs.json`
- `d:/Github/OstranautsTranslator/src/OstranautsTranslator.Core/Processing/RuntimeTextProjector.cs`

---

如果后续要把本文继续落成代码，建议从“给扫描结果加 `token_policy` 元数据”开始；这样改动面最小，也最容易同时服务 `source` 和 `translate`。
