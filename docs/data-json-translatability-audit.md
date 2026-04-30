# Ostranauts `StreamingAssets/data` JSON 可翻译性审计（现行实现口径）

> 更新日期：2026-05-01  
> 适用范围：`scan`、`source`、`native-mod` 这条原生 JSON 文本链路。  
> 本文档记录的是**当前已经落地到代码里的审计结果**，不是未来可能考虑的扩展方案。

## 文档目的

这份文档用来回答三个问题：

- `StreamingAssets/data` 里哪些目录/字段当前被认为是**真正安全可翻译**的。
- 哪些目录只是**部分字段**可翻，必须按白名单收录。
- 哪些目录当前明确视为**资源键、逻辑规则、控制字符串或模板协议**，不能进入扫描与 native mod 导出边界。

当前实现的事实来源是：

- `src/OstranautsTranslator.Tool/Scanning/OstranautsDataScanner.cs`
- `src/OstranautsTranslator.Tool/Exporting/NativeModExporter.cs`
- `docs/bracket-token-translation-rules.md`

## 当前实现边界

### 扫描边界

只有 `OstranautsDataScanner` 里的 `Rules` 明确列出的目录、子目录和字段，才会进入 `corpus.sqlite`。

这意味着：

- **未列入规则的目录，当前就视为排除**。
- 顶层目录被标成 `mixed` 时，表示它不是整目录放行，而是**只放行指定字段、指定子目录、指定数组位或指定赋值串子字段**。

### 扫描期值过滤

即使字段本身被列入白名单，扫描器仍会在 `AddOccurrence(...)` 里继续过滤危险值：

- 跳过空值
- 跳过包含 `$TEMPLATE` 的值
- 结合 `BracketTokenPolicyAnalyzer` 跳过：
  - 控制字符串
  - 未知 token 需人工复核的值

方括号 token 的统一规则见：`docs/bracket-token-translation-rules.md`。

### 导出边界

`NativeModExporter` 现在不再镜像整棵 `data/` 树，而是只导出：

- 在翻译数据库里确实存在 source/export plan 的 `.json` 文件
- 也就是当前扫描边界实际覆盖到的 JSON 源文件集合

因此，被排除目录不会被顺手抄进 native mod。这个收敛边界已经在真机验证里体现为：

- 游戏原始 JSON：`440`
- 当前 mod 导出 JSON：`314`

## Verdict 说明

- `translatable`：当前按整类文本表纳入，没有额外的子目录或字段 caveat。
- `mixed`：当前只纳入指定字段、数组位、赋值串子字段或子目录。
- `excluded`：当前明确不扫，`native-mod` 也不会回写。

> 备注：`data/` 根目录下的 `DebugSocialAudit.csv` 不是 JSON 数据目录，不在本文审计范围内。

## `translatable` 目录

这些目录当前直接按规则纳入，属于“文本本体可见，配对值只做元数据”的口径：

- `strings` — `simple-pairs: aValues 值位`  
  通用字符串表的 value 是当前直接入库的主 UI/消息文本来源。
- `names_first` — `name-pairs: aValues 名称位`  
  名字文本本身可见，配对值只是性别/类型元数据。
- `names_full` — `name-pairs: aValues 名称位`  
  全名文本本身可见，配对值只是元数据。
- `names_last` — `name-pairs: aValues 名称位`  
  姓氏文本本身可见，配对值只是元数据。
- `names_robots` — `name-pairs: aValues 名称位`  
  机器人名字文本本身可见，配对值只是元数据。
- `names_ship` — `name-pairs: aValues 名称位`  
  船名文本本身可见，配对值只是元数据。
- `names_ship_adjectives` — `name-pairs: aValues 名称位`  
  船名形容词文本本身可见，配对值只是元数据。
- `names_ship_nouns` — `name-pairs: aValues 名称位`  
  船名名词文本本身可见，配对值只是元数据。

## `mixed` 目录

这些目录当前只允许**部分字段**或**部分子目录**进入扫描。

- `ads` — `structured: strDesc`  
  仅广告正文是显示文本，其他字段/调度信息仍属逻辑。
- `attackmodes` — `structured: coAttacks / strNameFriendly`  
  仅 `coAttacks` 里的攻击友好名纳入；其余攻击配置和 `shipAttacks` 仍属战斗逻辑。
- `careers` — `structured: strNameFriendly`  
  仅职业显示名纳入；职业规则与引用键不纳入。
- `conditions` — `structured: strNameFriendly, strDesc, strShort, strDisplayBonus`  
  条件名称、描述、短文案与显示加成纳入；其余条件字段仍属逻辑。
- `conditions_simple` — `conditions-simple: aValues 每 7 项中的友好名/描述位`  
  紧凑数组里只有友好名和描述是文本，其余槽位是条件键/数值协议。
- `condowners` — `structured: strNameFriendly, strNameShort, strDesc`  
  对象模板的长短名称与描述是显示文本，其余是模板/引用/逻辑字段。
- `context` — `structured: strMainText`  
  当前只采纳正文主文本；其他上下文字段和标题仍未纳入。
- `cooverlays` — `structured: strNameFriendly, strNameShort, strDesc`  
  overlay 变体的名称/描述是显示文本，其余是变体/资源/逻辑引用。
- `guipropmaps` — `gui-prop-map: strFriendlyName, strTitle, strBrand, strBrandSub`  
  仅 GUI 标签/标题/品牌文本纳入，prefab/trigger/test 键仍属协议。
- `headlines` — `structured: strDesc, strRegion`  
  headline 正文和地区标签是显示文本，其余调度/权重字段不纳入。
- `homeworlds` — `structured: strColonyName`  
  仅殖民地显示名纳入；生成参数与标识仍属逻辑。
- `info` — `structured: strNodeLabel, strArticleTitle, strArticleBody`  
  Info UI 的节点名、文章标题和正文纳入；其余图结构仍属逻辑。
- `interactions` — `structured: strTitle, strDesc, strTooltip, strAttackerName`  
  仅交互标题/描述/提示/攻击者名纳入；动作组、测试和协议字段不纳入。
- `interaction_overrides` — `assignment-arrays: aOverrideValues -> strTitle, strDesc, strTooltip`  
  只提取赋值串里覆写的显示文本；其他 override 值仍是 `field|value` 协议。
- `jobitems` — `structured: strFriendlyName`  
  仅 gig/job item 的友好名纳入；其余掉落/条件引用仍属逻辑。
- `ledgerdefs` — `structured: strDesc`  
  仅账本/费用说明文本纳入；账目键和经济规则不纳入。
- `manpages` — `name-pairs: aValues 偶数位标题`  
  偶数位是可见手册标签；奇数位是 `images/manuals/*` 文件夹资源名，不能翻。
- `market` — `structured: CoCollections / strFriendlyName`  
  仅 `market/CoCollections` 的货物集合友好名纳入；其他子目录仍是供需与规格逻辑。
- `pda_apps` — `structured: strFriendlyName`  
  仅 PDA app 名称纳入；app 行为/图标/类键不纳入。
- `pledges` — `structured: strNameFriendly`  
  仅 pledge/行为标签纳入；其余状态机与 AI 规则不纳入。
- `plots` — `structured: strNameFriendly, aPhaseTitles`  
  plot 友好名和阶段标题纳入；beats、IA 控制串和阶段流转协议不纳入。
- `plot_beat_overrides` — `assignment-arrays: aOverrideBeatValues, aOverrideTriggerIAValues -> strDesc`  
  只收 override 数组里的 `strDesc` 文案；其他 override 值仍是协议/控制字段。
- `racing` — `structured: leagues/tracks / strNameFriendly, strDescription`  
  仅联赛/赛道名称与描述纳入；其他 racing 配置仍是规则与资源键。
- `rooms` — `structured: strNameFriendly`  
  仅房间友好名纳入；布局、条件、灯光与生成逻辑不纳入。
- `ships` — `structured: publicName, make, model, designation, dimensions, origin, description`  
  船只展示卡片文本纳入；布局、成本、模块与引用键不纳入。
- `slots` — `structured: strNameFriendly`  
  仅装备槽显示名纳入；槽位限制/协议不纳入。
- `star_systems` — `structured: strPublicName`  
  仅公开显示名纳入；天体、轨道与导航数据不纳入。
- `tips` — `structured: strBody`  
  仅 tip 正文纳入；分类键 `strCategory` 不作为翻译目标。
- `transit` — `structured: strLabelNameOptional`  
  仅 transit 连接上的可选显示标签纳入；`regID`/条件仍属查找与控制逻辑。
- `tsv` — `structured: output/stakes/conditions、output/stakes/interactions、output/stakes/contexts`  
  仅 stakes 输出子树里的镜像文本字段纳入；`tsv` 其余内容仍是生成输入/输出脚手架。

## `excluded` 目录

这些目录当前明确不扫，也不会出现在 native mod 导出边界里。

- `ai_training`  
  AI 训练/偏好历史权重表，属于决策数据而不是显示文本。
- `archived_content`  
  归档的旧互动文本库；当前 live 扫描口径明确不纳入。
- `audioemitters`  
  音频发射器配置主要是 mixer、clip 资源键和 falloff 参数。
- `blueprints`  
  小行星/簇蓝图是程序生成参数和 loot/template 引用键。
- `chargeprofiles`  
  充放电/消耗 profile 属于条件与数值曲线规则。
- `colors`  
  通用颜色表是 RGBA 调色板与渲染元数据。
- `condrules`  
  条件阈值与连锁规则表，属于逻辑规则。
- `condtrigs`  
  条件测试/过滤/触发器定义，属于查找规则与触发协议。
- `crewskins`  
  crew 皮肤/贴图组合是资源映射，不是显示文本。
- `crime`  
  法域/犯罪类型到 pledge/loot/condition 的映射表，属于规则键。
- `explosions`  
  爆炸效果配置主要是音效、伤害、半径等效果参数。
- `gasrespires`  
  气体呼吸/交换配置是点位、条件和数值规则。
- `installables`  
  安装/拆解工序模板以动作模板、条件和产出规则为主；当前不纳入。
- `items`  
  物品图像、socket、灯光、损坏与占格配置属于渲染/摆放元数据。
- `jobs`  
  Job 定义主要引用 setup/finish interaction、loot 文本键、person spec 和 payout/duration，不是直出文本。
- `lifeevents`  
  人生事件表主要链接 interaction/奖励/ship/ATC 引用，不直接承载显示文案。
- `lights`  
  灯光颜色、sprite、位置配置属于渲染元数据。
- `loot`  
  Loot 列表是 condition owner / item / ship / image 等查找键集合，不是显示文本。
- `music`  
  音乐目录保存 OGG 文件名与标签选择键，属于资源键而非 UI 文本。
- `music_stations`  
  station 到 music tag 的映射表，属于查找键/播放规则。
- `parallax`  
  视差背景层配置主要是 pattern/sprite list 与滚动参数。
- `personspecs`  
  NPC 生成/筛选规格主要是 cond/loot/relationship/test 引用和过滤条件。
- `plot_beats`  
  plot beat 文件主要是 trigger 名、token set 和 `IA,[token]...` 控制串，属于控制协议。
- `plot_manager`  
  plot 选择/调度/运行管理配置属于触发与流程逻辑。
- `powerinfos`  
  供电输入/功耗/开关 interaction 规则属于系统逻辑。
- `schemas`  
  JSON schema/验证定义属于结构描述元数据。
- `shipspecs`  
  ship spec 是生成/载入/ATC/所属状态过滤表，不是展示文本。
- `slot_effects`  
  纸娃娃图片、mesh/texture、slot/unslot interaction 引用属于资源与协议配置。
- `tickers`  
  ticker 周期/condloot 配置属于计时与触发逻辑。
- `tokens`  
  自定义方括号 token 注册表属于模板协议而非显示文本。
- `traitscores`  
  trait/skill 评分元组表属于角色生成权重数据。
- `verbs`  
  动词 token 词形表供语法引擎替换，不是直接显示文本。
- `wounds`  
  伤口配置主要是 pain PNG、伤害效果和 loot/verb 引用键，属于效果逻辑。
- `zone_triggers`  
  区域触发器配置主要是 encounter 触发与流程控制。

## 需要特别提醒的 token / control string 目录

下面这些目录虽然字段本身被列入白名单，但**值本身仍可能因为 token/control string 规则而被扫描期跳过**：

- `strings`
- `conditions`
- `interactions`
- `interaction_overrides`
- `plots`
- `plot_beat_overrides`

原因通常包括：

- 实体槽位 token（可收录，但 token 必须保留）
- 英文语法 token / verb token（可收录，但翻译时应改写句式）
- Plot 控制字符串（整条必须跳过）
- 未验证 token（当前按人工复核或扫描期跳过处理）

这部分统一规则不要在目录级文档里重复造一套，直接以 `docs/bracket-token-translation-rules.md` 为准。

## 当前最值得保留的边界提醒

### `manpages`

这是本轮审计里最典型的“文件能翻，但不能整段乱扫”的目录：

- 可翻的是标题位
- 不能翻的是 `images/manuals/*` 资源目录名

因此当前口径是 `name-pairs`，只收 `aValues` 的标题位，而不是整段 `simple values`。

### `attackmodes`

当前只纳入 `attackmodes/coAttacks` 的 `strNameFriendly`。

- `coAttacks`：有显示给玩家的友好名
- 其他攻击配置：以战斗、资源和效果参数为主

### `market`

当前不是整棵 `market/` 目录开放，而是只纳入：

- `market/CoCollections`
- 字段：`strFriendlyName`

其余市场 actor/config/save/spec 相关内容仍按逻辑数据处理。

### `racing`

当前只纳入：

- `racing/leagues`
- `racing/tracks`

并且仅收：

- `strNameFriendly`
- `strDescription`

### `tsv`

当前只纳入 `output/stakes` 这组三个镜像子树：

- `tsv/output/stakes/conditions`
- `tsv/output/stakes/interactions`
- `tsv/output/stakes/contexts`

其余 `tsv` 内容仍按生成脚手架/中间产物处理，不进入正式文本边界。

## 后续可继续精修，但**不属于当前实现事实**

下面这些项值得继续研究，但在这份文档里必须明确标成 TODO，不能写成既成规则：

- `archived_content`：目录内确实存在“像文本”的内容，但当前实现仍明确排除。
- `installables`：样本里出现过重复的 `strTooltip`，但当前仍按规则/配方表排除。
- `careers.strNameFriendly`：仍可继续复核是否存在英文逻辑比较风险。
- `tsv/output/stakes/*`：未来可以继续评估是否要像 base 目录那样再收紧一层。
- native mod 导出目前是“只导出有合格 source plan 的 JSON”，还不是“只导出实际发生 patch 的 JSON”。

## 本轮验证结果

这套边界已经经过真实构建与命令验证：

- 解决方案构建：`0 warnings / 0 errors`
- `scan`：`sources=315`、`occurrences=28462`、`entries=19696`
- `source -tzh`：`synced=19746`、`exported=19746`
- `native-mod -tzh`：`translated=0`、`patched=0`、`files=314`、`warnings=0`
- 导出范围：从游戏原始 `440` 个 JSON 收敛到 mod 中的 `314` 个 JSON

## 用法建议

如果以后要继续调整目录边界，推荐按这个顺序改：

1. 先在本文档里补充或修改目录 verdict
2. 再更新 `OstranautsDataScanner.Rules`
3. 如涉及 bracket token / control string，再同步更新 `docs/bracket-token-translation-rules.md`
4. 最后重新跑 `scan`、`source`、`native-mod` 做真机验证
