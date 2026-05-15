from __future__ import annotations

import argparse
import configparser
import json
import os
import re
from dataclasses import dataclass
from pathlib import Path

import fitz
from PIL import Image, ImageDraw

from translate_images import (
    DeepSeekTranslator,
    expand_bbox,
    fit_text_to_box,
    get_effective_line_height,
    get_contrasting_colors,
    render_lines,
    resolve_font_path,
    sample_fill_color,
    wrap_text,
)


DEFAULT_TARGET_LANGUAGE = "Simplified Chinese"
DEFAULT_SOURCE_LANGUAGE = "English"
DEFAULT_BATCH_SIZE = 20
DEFAULT_CACHE_FILE_NAME = "manuals-en-to-zh-v2.json"
TEXT_LIKE_PATTERN = re.compile(r"[A-Za-z]")
STRUCTURED_MARKER_PATTERN = re.compile(r"^(?:[●○•◦▪▫■□]+|\d+)$")
PDF_BLOCK_MERGE_GAP_THRESHOLD = 8.0
PDF_BLOCK_MERGE_LEFT_TOLERANCE = 24.0
PDF_TEXTBOX_HORIZONTAL_INSET_RATIO = 0.06
PDF_TEXTBOX_VERTICAL_INSET_RATIO = 0.14
PDF_TEXTBOX_MIN_HORIZONTAL_INSET = 6
PDF_TEXTBOX_MIN_VERTICAL_INSET = 4
PDF_TEXTBOX_MAX_FONT_SCALE = 0.8
PDF_SINGLE_LINE_HEIGHT_THRESHOLD = 34
PDF_SINGLE_LINE_MIN_FONT_SIZE = 4
PDF_PARAGRAPH_LINE_GAP_RATIO = 0.3
PDF_SINGLE_LINE_GAP_RATIO = 0.18

MANUAL_TRANSLATION_SYSTEM_PROMPT = (
    "You translate Ostranauts in-game manual and handbook text from English into Simplified Chinese. "
    "The user message is always a JSON array of manual text blocks extracted from PDF pages. "
    "Return exactly one valid JSON object in the form {\"translations\":[...]}. "
    "The translations array must contain the same number of items and the same order as the input. "
    "Output only the JSON object. Do not output markdown, comments, or extra text. "
    "Preserve line breaks such as \n when they are already present in the input unless concise Chinese can naturally keep the same block readable. "
    "This is hard-sci-fi technical documentation, checklists, operating procedures, warning labels, and legal notices. "
    "Use concise, professional, readable Simplified Chinese. "
    "Preserve explicit technical identifiers and control labels in English when they are UI/control tokens or obvious machine labels, including but not limited to FIELD COILS, FUEL REG, FWD, REAR, MHD, CORE PURGE, CAPACITOR CHARGE, CYCLE, READY, IGNITION, ON, OFF, ICFR, TEC, kPa, Kelvin, model names, serial-style IDs, and bracketed control states. "
    "Translate surrounding prose naturally, but do not translate those control tokens into Chinese. "
    "Translate Verify... patterns into concise confirmation-style Chinese, translate NOTE and CAUTION into concise Chinese note/warning wording, and keep repeated warning lines consistent across the batch. "
    "Prefer stable terminology across all blocks. Avoid verbose paraphrasing. Preserve bullets, brackets, arrows, and warning markers when possible using renderable plain text symbols."
)


@dataclass(frozen=True)
class PdfPagePlan:
    folder_name: str
    image_path: Path
    output_path: Path
    pdf_size: tuple[float, float]
    blocks: tuple[tuple[tuple[float, float, float, float], str], ...]


@dataclass(frozen=True)
class ManualOverlay:
    bbox: tuple[int, int, int, int]
    translated_text: str
    padding: int = 10
    min_font_size: int = 11
    max_font_size: int = 72
    align_left: bool = False
    text_fill: tuple[int, int, int, int] | None = None
    stroke_fill: tuple[int, int, int, int] | None = None


def ends_with_hard_break(text: str) -> bool:
    stripped = text.rstrip()
    if not stripped:
        return True

    return stripped.endswith((".", "!", "?", ":", ";", ")", "]", "。", "！", "？", "：", "；"))


def is_structured_layout_block(text: str) -> bool:
    lines = [line.strip() for line in text.splitlines() if line.strip()]
    if not lines:
        return False

    if any(STRUCTURED_MARKER_PATTERN.fullmatch(line) for line in lines):
        return True

    if len(lines) == 2 and STRUCTURED_MARKER_PATTERN.fullmatch(lines[0]):
        return True

    if len(lines) >= 2:
        average_length = sum(len(line) for line in lines) / len(lines)
        if average_length <= 24 and not any(ends_with_hard_break(line) for line in lines):
            return True

    return False


def should_split_block_into_lines(lines: list[dict[str, object]]) -> bool:
    texts = []
    for line in lines:
        text = "".join(span["text"] for span in line["spans"])
        if text:
            texts.append(text.strip())

    if len(texts) <= 1:
        return False

    # PDF 目录页常把多个项目和项目符号塞进同一个 block；这类结构化块必须按行拆开。
    return any(text in {"●", "○"} for text in texts)


def merge_pdf_blocks(blocks: list[tuple[tuple[float, float, float, float], str]]) -> list[tuple[tuple[float, float, float, float], str]]:
    if not blocks:
        return []

    merged: list[tuple[tuple[float, float, float, float], str]] = []
    current_bbox, current_text = blocks[0]

    for next_bbox, next_text in blocks[1:]:
        vertical_gap = next_bbox[1] - current_bbox[3]
        left_delta = abs(next_bbox[0] - current_bbox[0])
        should_merge = (
            vertical_gap <= PDF_BLOCK_MERGE_GAP_THRESHOLD
            and left_delta <= PDF_BLOCK_MERGE_LEFT_TOLERANCE
            and not ends_with_hard_break(current_text)
            and not is_structured_layout_block(current_text)
            and not is_structured_layout_block(next_text)
        )

        if should_merge:
            current_bbox = (
                min(current_bbox[0], next_bbox[0]),
                min(current_bbox[1], next_bbox[1]),
                max(current_bbox[2], next_bbox[2]),
                max(current_bbox[3], next_bbox[3]),
            )
            current_text = f"{current_text}\n{next_text}"
            continue

        merged.append((current_bbox, current_text))
        current_bbox, current_text = next_bbox, next_text

    merged.append((current_bbox, current_text))
    return merged


BASIC_CONTROLS_TRANSLATIONS = {
    "Opens game\nmenu.\nExits other\nscreens.": "打开游戏菜单。\n关闭其他界面。",
    "Toggles\ninventory\nscreen.": "切换\n物品栏。",
    "Pans camera.\nManeuvers\nship.": "平移镜头。\n操纵飞船。",
    "Zoom\ncamera\nin/out.": "缩放\n镜头",
    "Center\ncamera\non player.": "镜头居中\n到玩家",
    "Select object.\nWalk to point.\nOpens\ncontext\nmenu on\nitem\nor crew.": "选择对象。\n移动到目标点。\n打开物品或船员\n上下文菜单。",
    "Pauses time.": "暂停时间",
    "Slow down\n& speed up\ngame time.": "减慢或加快\n时间流速",
    "Info about\nitem under\ncursor.": "显示光标下\n物品信息",
    "Toggle\ngoals.": "切换\n目标",
    "Help.": "帮助",
    "Crew rules,\nschedules,\nand duties.": "船员规则、\n日程与职责",
    "Toggle view\noverlays.": "切换视图\n叠层",
    "Queue orders\nfor building, \nrepairing, and\nother crew tasks.": "排队下达\n建造、修理\n等船员任务",
    "Note: Key mapping may be different if customized by user.": "注意：如果用户自定义了按键映射，实际按键可能不同。",
    "RST was founded in 2027 by Renbao Semiconductor Fabrication at the tail of the 20s fab boom. Its goal was to explore alternative computing architecture in the face of the rising costs of lithographic processes. While designing\ntests for a system in development, Lai Ying Ho, then a Junior Engineer, developed the predictive-priority-interface as a tool to assist her own work, populating it with surveillance data taken of herself. Her tool was soon in use by\nher coworkers. Anecdotal accounts have an unknown manager misunderstanding the interface’s origin and threatening to fire the entire floor for corporate espionage. When the directors realised the interface could become a\nmainstream success, they integrated it into their next-generation flagship devices. RST’s first major release, the 公式 (‘Formula’) smartphone, leapfrogged competition, setting industry standards of usability and performance.\nRST soon expanded into disparate software and hardware fields such as artificial intelligence, cloud computing, high velocity data, cryptography, cybernetics, and wearable technologies. In 2050 Renbao Semiconductor Fabrication\nrestructured itself to refocus the company’s mission, renaming itself ‘Renbao’ and giving RST the remit to pursue its focus on the consumer electronics environment. Renbao was one of the major investors in the construction of\nSuzhou Orbital, and today monopolises 6% of its interior surface area.": "RST 成立于 2027 年，由任宝半导体制造在 20 年代末晶圆热潮的尾声创办。它的目标是在光刻工艺成本不断上升的背景下，探索替代性的计算架构。时任初级工程师的赖映荷在为一套研发中的系统设计测试时，开发出“预测优先界面”来辅助自己的工作，并将采集自自身的监控数据填入其中。她的工具很快就在同事之间流传开来。坊间传闻称，一位不知名的经理误解了这套界面的来源，甚至以企业间谍罪名威胁要开除整层楼的人。董事会意识到该界面有望成为主流成功产品后，便将其整合进公司下一代旗舰设备。RST 的首个重大产品是“公式”（Formula）智能手机，它一举甩开竞争对手，树立了可用性与性能的行业标准。此后，RST 很快扩展到人工智能、云计算、高速数据、密码学、控制论和可穿戴技术等软硬件领域。2050 年，任宝半导体制造为重新聚焦公司的使命而进行重组，并更名为“任宝”，同时赋予 RST 专注消费电子生态的职责。任宝是苏州轨道站建设的主要投资方之一，如今垄断了其内部表面积的 6%。",
    "Basic controls in": "Ostranauts",
    "Ostranauts.": "基础操作",
}


PAGE_TRANSLATION_OVERRIDES = {
    "Basic Controls": BASIC_CONTROLS_TRANSLATIONS,
}


PAGE_IMAGE_TRANSLATION_OVERRIDES = {
    ("Fusion Reactor Manual", "004.png"): {
        "►\nPress button FUEL REG on FIELD COILS": "> 按下 FIELD COILS 上的 FUEL REG 按钮",
        "(Verify button FUEL REG lamp indicates solid on)": "（确认 FUEL REG 指示灯常亮）",
        "[⚠CAUTION: FIELD COILS REQUIRE HIGH BATTERY LOAD. PROCEED WITH HASTE ⚠]": "[△ 注意：FIELD COILS 负载较高，请迅速操作 △]",
        "►\nPress button FWD on FIELD COILS": "> 按下 FIELD COILS 上的 FWD 按钮",
        "(Verify FWD lamp indicates solid on)": "（确认 FWD 指示灯常亮）",
        "►\nPress button REAR on FIELD COILS": "> 按下 FIELD COILS 上的 REAR 按钮",
        "(Verify REAR lamp indicates solid on)": "（确认 REAR 指示灯常亮）",
        "(Verify RATIO in position MHD)": "（确认 RATIO 位于 MHD）",
        "►\nSet MHD position 1 [ON]": "> 将 MHD 置于 1 [ON]",
        "[NOTE:\nIf\nno\nMHD\npresent,\nreactor\nwill\nNOT\noutput\npower\nto\ncharge\nbatteries]": "[注：若未安装 MHD，反应堆将不会输出功率为电池充电]",
        "(Verify CORE PURGE in position OFF)": "（确认 CORE PURGE 位于 OFF）",
        "(Verify CAPACITOR CHARGE indicates READY)": "（确认 CAPACITOR CHARGE 显示 READY）",
        "(Verify CYCLE in position CLOSED)": "（确认 CYCLE 处于 CLOSED）",
        "(Verify Field Coils FWD lamp indicates solid on)": "（确认 Field Coils FWD 指示灯常亮）",
        "(Verify Field Coils REAR lamp indicates solid on)": "（确认 Field Coils REAR 指示灯常亮）",
        "(Verify READY lamp indicates solid green)": "（确认 READY 指示灯呈绿色常亮）",
        "⚠⚠⚠⚠⚠": "△ △ △ △ △",
        "DO NOT INITIATE REACTOR WITHOUT FIELD COILS ACTIVE": "未激活场线圈时不得启动反应堆",
        "►\nSet IGNITION position 1 [ON]": "> 将 IGNITION 置于 1 [ON]",
        "⚠⚠⚠THIS DOCUMENT MUST REMAIN ACCESSIBLE TO SULAIMAN “X[X] TW” ICFR TECHNICIANS AT ALL TIMES ⚠⚠⚠": "△△△ 本文件必须始终可供 SULAIMAN “X[X] TW” ICFR 技术人员查阅 △△△",
    },
}


PAGE_IMAGE_OVERLAY_OVERRIDES = {
    ("Fusion Reactor Manual", "005.png"): [
        ManualOverlay(
            (70, 110, 1540, 1420),
            "　",
            min_font_size=12,
            max_font_size=12,
            padding=0,
        ),
        ManualOverlay(
            (120, 120, 1500, 760),
            "（确认 X-RAY WARN 指示灯保持熄灭）\n"
            "[△ 注意：若 X-RAY WARN 指示灯亮起或 X-RAY WARN 警报响起，请立即停机 △]\n"
            "（确认 ABL WALL WARN 指示灯保持熄灭）\n"
            "（确认 CORE PRESSURE 显示并保持在 ROUGH 以上，琥珀色）\n"
            "（确认 CORE TEMP 显示并保持琥珀色或绿色）\n"
            "（确认 FUEL kg/day 显示波动 < 50 kg/day）\n"
            "（确认 POWER THR 显示绿色）\n"
            "（确认 POWER FUS 显示绿色）\n"
            "（确认 POWER TOTAL 显示绿色）\n"
            "（确认 POWER LOAD 显示绿色）\n"
            "[注：接近最小值时不会有指示灯显示]",
            min_font_size=18,
            max_font_size=28,
            align_left=True,
            padding=20,
        ),
        ManualOverlay(
            (120, 790, 1500, 1160),
            "电池充电：\n"
            "> MHD 置 1 [ON]\n"
            "> PWR BUS 置 CHRG\n"
            "（确认 POWER MHD 显示绿色）\n"
            "（确认所有已连接的电池正在充电）",
            min_font_size=20,
            max_font_size=30,
            align_left=True,
            padding=20,
        ),
        ManualOverlay(
            (120, 1180, 1500, 1360),
            "推力操作：\n> THRUST ACTIVE SAFETY 置 1 [ON]",
            min_font_size=20,
            max_font_size=28,
            align_left=True,
            padding=18,
        ),
        ManualOverlay(
            (120, 2160, 1530, 2295),
            "△△△ 本文件必须始终可供苏莱曼“X[X] TW”惯性约束聚变反应堆技术人员查阅 △△△",
            min_font_size=16,
            max_font_size=24,
            padding=22,
        ),
    ],
    ("Fusion Reactor Manual", "006.png"): [
        ManualOverlay(
            (70, 110, 1540, 1900),
            "　",
            min_font_size=12,
            max_font_size=12,
            padding=0,
        ),
        ManualOverlay(
            (120, 120, 1500, 670),
            "反应堆停机检查表\n"
            "[注意：为安全停机，需禁用反应堆燃料调节器、禁用点火，或将电源总线切换至关闭]\n"
            "> 按下 FUEL REG\n"
            "> IGNITION 置 0 [OFF]\n"
            "> PWR BUS 置 OFF\n"
            "（确认 FUEL REG 按钮指示灯熄灭）\n"
            "（确认反应堆已停机）\n"
            "（确认所有指示灯无显示）\n"
            "（确认 PWR BUS 位置为 OFF）- 继续：",
            min_font_size=18,
            max_font_size=30,
            align_left=True,
            padding=20,
        ),
        ManualOverlay(
            (120, 700, 1500, 1860),
            "反应堆停堆安全清单\n"
            "> PWR BUS 置 BATT\n"
            "> 等待灯检完成\n"
            "（确认所有指示灯在测试期间点亮）\n"
            "[注：若灯光测试无指示，请将 PWR BUS 置于 OFF，并重新开始停机安全确认清单]\n"
            "> CRYO 置 1 [ON]\n"
            "> 等 CORE TEMP 降至 0 MeV\n"
            "> CRYO 置 0 [OFF]\n"
            "（确认 CORE TEMP 下降）\n"
            "> CORE PURGE 置 RGH\n"
            "> 等 CORE PRESSURE 仅显示 VAC\n"
            "> CORE PURGE 置 OFF\n"
            "> PWR BUS 置 OFF\n"
            "（确认 CORE PRESSURE 下降）\n"
            "[注：反应堆停机现已完成]",
            min_font_size=18,
            max_font_size=28,
            align_left=True,
            padding=20,
        ),
        ManualOverlay(
            (120, 2160, 1530, 2295),
            "△△△ 本文件必须始终可供苏莱曼“X[X] TW”惯性约束聚变反应堆技术人员查阅 △△△",
            min_font_size=16,
            max_font_size=24,
            padding=22,
        ),
    ],
    ("Fusion Reactor Manual", "003.png"): [
        ManualOverlay(
            (120, 150, 900, 290),
            "冷启动检查清单",
            min_font_size=30,
            max_font_size=52,
            align_left=True,
            padding=14,
        ),
        ManualOverlay(
            (120, 320, 1520, 2140),
            "> 将 PWR BUS 置于 BATT\n"
            "> 等待灯光测试完成\n"
            "  （确认所有指示灯在测试期间点亮）\n"
            "[注：若灯光测试未指示，请将 PWR BUS 置于 OFF，并返回检查清单开头]\n"
            "  （确认 BATT. % POWER 显示绿色）\n"
            "  （确认 CAPACITOR CHARGE 正在上升或显示 READY）\n"
            "[注：若 CAPACITOR CHARGE 指示灯无显示，请将 PWR BUS 置于 OFF，并返回检查清单开头]\n"
            "  （确认燃料 He3 [氦-3] 千克液位显示 [> 6.6 千克，最低推荐值]）\n"
            "  （确认燃料 D [氘] 千克液位显示 [> 4.45 千克，最低推荐值]）\n"
            "  （确认 CORE PRESSURE 指示为 VAC）\n"
            "      如果粗糙：\n"
            "      > 将 CORE PURGE 置于 RHG\n"
            "        （确认 CORE PRESSURE 下降）\n"
            "      > 等待 CORE PRESSURE 指示为 VAC\n"
            "      > 将 CORE PURGE 置于 OFF\n"
            "[注：若未安装核心泵，CORE PURGE 将不起作用]\n"
            "          若无核心泵：\n"
            "          > 将 CYCLE 置于 OPEN [仅真空]\n"
            "            （确认 CORE PRESSURE 下降）\n"
            "          > 等待 CORE PRESSURE 指示为 VAC\n"
            "          > 将 CYCLE 置于 CLOSED\n"
            "[注：新的或长期休眠的 SULAIMAN X[X] TW ICF 反应堆，其 CORE PRESSURE 灯可能不会对 VAC 亮起。\n"
            "通病：某些构建版本中，如果真空接近完美或已近乎完美，CORE PRESSURE 灯不会对 VAC 亮起，可视为 VAC 指示失灵]\n"
            "（确认 LAS ALIGN 指示灯显示稳定白色）\n"
            "> 将 LAS ALIGN 置于 1 [ON]\n"
            "  （确认 LAS ALIGN READY 指示灯为绿色常亮）\n"
            "（确认 PEL FEED 指示灯为白色常亮）\n"
            "> 将 PEL FEED 置于 1 [ON]\n"
            "  （确认 PEL FEED READY 指示灯为绿色常亮）\n"
            "> 将 CRYO 置于 1 [ON]\n"
            "[注：若未安装低温泵，CRYO 开关将保持不可操作]\n"
            "（确认 THRUST ACTIVE 安全开关位于 OFF）\n"
            "（确认 FLOW 位于 MINIMUM）\n"
            "（确认 CYCLE 位于 CLOSED）",
            min_font_size=18,
            max_font_size=28,
            align_left=True,
            padding=22,
        ),
        ManualOverlay(
            (110, 2190, 1530, 2305),
            "△△△ 本文件必须始终可供苏莱曼“X[X] TW”惯性约束聚变反应堆技术人员查阅 △△△",
            min_font_size=16,
            max_font_size=24,
            padding=22,
        ),
    ],
    ("Fusion Reactor Manual", "002.png"): [
        ManualOverlay(
            (90, 150, 1520, 470),
            "反应堆冷启动至点火\n及怠速电池充电序列",
            min_font_size=34,
            max_font_size=60,
            align_left=True,
            padding=26,
        ),
        ManualOverlay(
            (90, 430, 1540, 1560),
            "启动前检查清单\n\n"
            "（确认聚变场线圈组件无损坏）\n"
            "（确认 X(X) TW 反应堆核心无损坏）\n"
            "（确认低温储罐无损坏）\n"
            "（确认低温分配泵无损坏且磨损极小）\n"
            "（确认聚变级激光电容器无损坏）\n"
            "（确认聚变激光阵列无损坏）\n"
            "[注：一台 [1] 聚变级激光电容器可为两组 [2] 聚变激光阵列供电]\n"
            "（确认反应堆燃料调节器无损坏）\n"
            "（确认燃料颗粒进料器无损坏）\n"
            "[注：一个 [1] 反应堆燃料调节器可为两个 [2] 颗粒进料器供给燃料]\n"
            "（确认磁流体 [MHD] 发电机无损坏）\n"
            "（确认聚变核心泵无损坏）\n"
            "（确认飞船电池电量足以执行反应堆冷启动）",
            min_font_size=22,
            max_font_size=38,
            align_left=True,
            padding=30,
        ),
        ManualOverlay(
            (90, 2160, 1540, 2295),
            "△△△ 本文件必须始终可供苏莱曼“X[X] TW”惯性约束聚变反应堆技术人员查阅 △△△",
            min_font_size=16,
            max_font_size=24,
            padding=24,
        ),
    ],
    ("Fusion Reactor Manual", "004.png"): [
        ManualOverlay(
            (60, 120, 1500, 770),
            "> 按下场线圈上的燃料调节按钮\n"
            "  （确认燃料调节指示灯常亮）\n"
            "[△ 注意：场线圈负载较高，请迅速操作 △]\n"
            "> 按下场线圈上的前向按钮\n"
            "  （确认前向指示灯常亮）\n"
            "> 按下场线圈上的后向按钮\n"
            "  （确认后向指示灯常亮）\n"
            "（确认比率位于磁流体模式）\n"
            "> 将磁流体模式置于 1 [开]\n"
            "[注：若未安装磁流体发电机，反应堆将不会输出功率为电池充电]",
            min_font_size=20,
            max_font_size=36,
            align_left=True,
            padding=28,
        ),
        ManualOverlay(
            (60, 760, 1500, 1285),
            "（确认核心吹扫位于关）\n"
            "（确认电容充电显示就绪）\n\n"
            "（确认循环处于关闭）\n"
            "（确认循环处于关闭）\n"
            "（确认循环处于关闭）\n\n"
            "（确认场线圈前向指示灯常亮）\n"
            "（确认场线圈前向指示灯常亮）\n"
            "（确认场线圈前向指示灯常亮）\n\n"
            "（确认场线圈后向指示灯常亮）\n"
            "（确认场线圈后向指示灯常亮）\n"
            "（确认场线圈后向指示灯常亮）\n\n"
            "（确认就绪指示灯呈绿色常亮）",
            min_font_size=18,
            max_font_size=30,
            align_left=True,
            padding=28,
        ),
        ManualOverlay((420, 1285, 1160, 1365), "△   △   △   △   △", min_font_size=22, max_font_size=34, padding=16),
        ManualOverlay(
            (60, 1350, 1500, 1575),
            "未激活场线圈时不得启动反应堆\n"
            "未激活场线圈时不得启动反应堆\n"
            "未激活场线圈时不得启动反应堆",
            min_font_size=28,
            max_font_size=44,
            align_left=True,
            padding=28,
        ),
        ManualOverlay((60, 1580, 1500, 1670), "> 将点火置于 1 [开]", min_font_size=22, max_font_size=36, align_left=True, padding=24),
        ManualOverlay(
            (60, 1670, 1500, 1915),
            "未激活场线圈时不得启动反应堆\n"
            "未激活场线圈时不得启动反应堆\n"
            "未激活场线圈时不得启动反应堆",
            min_font_size=28,
            max_font_size=44,
            align_left=True,
            padding=28,
        ),
        ManualOverlay((420, 1900, 1160, 1980), "△   △   △   △   △", min_font_size=22, max_font_size=34, padding=16),
        ManualOverlay((20, 1960, 1562, 2048), "△△△ 本文件必须始终可供苏莱曼“X[X] TW”惯性约束聚变反应堆技术人员查阅 △△△", min_font_size=14, max_font_size=20, padding=26),
        ManualOverlay((0, 2180, 1647, 2327), "　", min_font_size=12, max_font_size=12, padding=0),
    ],
    ("Environmental Control Systems Certification Guide", "001.png"): [
        ManualOverlay((600, 320, 980, 430), "目录", min_font_size=30, max_font_size=54),
        ManualOverlay((150, 450, 1360, 650), "本指南分为五个单元，并以 TEC 技术人员在创建适航环境时可使用的快速启动检查表作为总结。", min_font_size=20, max_font_size=34, align_left=True, padding=14),
        ManualOverlay(
            (150, 620, 1260, 1860),
            "● 单元1：环境系统导论\n"
            "  ○ 目标\n"
            "  ○ 结构\n"
            "  ○ 环境混合物的成分\n"
            "  ○ 压力服\n"
            "  ○ 缺氧\n\n"
            "● 单元2：在真空中维持环境\n"
            "  ○ 主舱室\n"
            "  ○ 完整性测试\n\n"
            "● 单元3：气泵的安装与维护\n"
            "  ○ 结构\n"
            "  ○ 安装\n"
            "  ○ 设置\n\n"
            "● 单元4：加热与冷却系统的安装与维护\n"
            "  ○ 加热单元\n"
            "  ○ 冷却单元\n"
            "  ○ 面板控制\n\n"
            "● 单元5：将环境设备连接至警报与传感器\n"
            "  ○ 输入信号面板访问\n"
            "  ○ 输入信号连接\n\n"
            "● 附录：创建适航环境的检查清单\n"
            "  ○ 技术员\n"
            "  ○ 主舱\n"
            "  ○ 气泵\n"
            "  ○ 加热与冷却系统\n"
            "  ○ 最终环境检查",
            min_font_size=20,
            max_font_size=34,
            align_left=True,
            padding=14,
        ),
    ],
    ("DON'T CRASH", "000.png"): [
        ManualOverlay((405, 28, 1170, 190), "别撞船！！", min_font_size=42, max_font_size=86, text_fill=(24, 78, 160, 255), stroke_fill=(24, 78, 160, 255)),
        ManualOverlay((1030, 270, 1460, 410), "对接检查表", min_font_size=24, max_font_size=46, text_fill=(24, 78, 160, 255), stroke_fill=(24, 78, 160, 255), padding=14),
        ManualOverlay((860, 400, 1545, 1115), "- 选中目标\n- 调整 Brg = 0（也就是 360）\n- 飞向目标\n  （留出减速空间！）\n- 当 Rng < 5 km 时：\n- 切到对接界面 >\n- COMMS -> 呼叫飞船\n- 选择目标飞船\n- 请求许可\n- 对齐圆环\n- 以 Vrel < 100 m/s 接近\n...\n- 绿灯亮起时\n- 点击 \"CLAMPS\"\n- 然后大家来一杯 Bismertnaya！", min_font_size=16, max_font_size=24, align_left=True, text_fill=(24, 78, 160, 255), stroke_fill=(24, 78, 160, 255), padding=18),
        ManualOverlay((1004, 1315, 1410, 1398), "速查表", min_font_size=28, max_font_size=56, text_fill=(24, 78, 160, 255), stroke_fill=(24, 78, 160, 255)),
        ManualOverlay((878, 1418, 1525, 1470), "P.o.r. - 最近/最大引力井", min_font_size=17, max_font_size=28, align_left=True, text_fill=(24, 78, 160, 255), stroke_fill=(24, 78, 160, 255)),
        ManualOverlay((878, 1510, 1510, 1688), "Vrel - 相对速度\nVcrs - 横向速度（侧滑）\nRng - 距离\nBrg - 方位（0 = 正前方）", min_font_size=17, max_font_size=28, align_left=True, text_fill=(24, 78, 160, 255), stroke_fill=(24, 78, 160, 255)),
        ManualOverlay((878, 1725, 1478, 1772), "Delta V - 剩余燃料（m/s）", min_font_size=16, max_font_size=28, align_left=True, text_fill=(24, 78, 160, 255), stroke_fill=(24, 78, 160, 255)),
        ManualOverlay((878, 1813, 1554, 1898), "RCS - 姿态控制系统（推力）\nETA - 预计到达时间", min_font_size=16, max_font_size=28, align_left=True, text_fill=(24, 78, 160, 255), stroke_fill=(24, 78, 160, 255)),
        ManualOverlay((210, 960, 520, 1006), "地图控制", min_font_size=20, max_font_size=34, text_fill=(24, 78, 160, 255), stroke_fill=(24, 78, 160, 255)),
        ManualOverlay((235, 1118, 520, 1168), "RCS 机动", min_font_size=22, max_font_size=34, text_fill=(24, 78, 160, 255), stroke_fill=(24, 78, 160, 255)),
        ManualOverlay((380, 1210, 662, 1260), "飞行模式！", min_font_size=20, max_font_size=32, text_fill=(24, 78, 160, 255), stroke_fill=(24, 78, 160, 255)),
        ManualOverlay((50, 1308, 264, 1366), "行星(小行星)", min_font_size=18, max_font_size=28, text_fill=(24, 78, 160, 255), stroke_fill=(24, 78, 160, 255)),
        ManualOverlay((575, 1510, 665, 1556), "飞船", min_font_size=18, max_font_size=28, text_fill=(24, 78, 160, 255), stroke_fill=(24, 78, 160, 255)),
        ManualOverlay((548, 1628, 698, 1674), "目标", min_font_size=18, max_font_size=28, text_fill=(24, 78, 160, 255), stroke_fill=(24, 78, 160, 255)),
        ManualOverlay((548, 1800, 630, 1844), "???", min_font_size=18, max_font_size=28, text_fill=(24, 78, 160, 255), stroke_fill=(24, 78, 160, 255)),
        ManualOverlay((112, 1960, 170, 2008), "我", min_font_size=18, max_font_size=28, text_fill=(24, 78, 160, 255), stroke_fill=(24, 78, 160, 255)),
    ],
    ("Holden Patch by Halvorson", "000.png"): [
        ManualOverlay((90, 90, 1490, 168), "霍尔登补丁™", min_font_size=34, max_font_size=76),
        ManualOverlay((90, 170, 300, 212), "哈沃森出品", min_font_size=12, max_font_size=28),
        ManualOverlay((90, 246, 1490, 412), "霍尔登补丁™是一套工业级快固聚环氧修补系统，用于在飞船船体上快速形成气密密封。标准套件包含修复受损地板、墙壁与其他表面，并维持受损舱室压力所需的一切。", min_font_size=18, max_font_size=32, align_left=True),
        ManualOverlay((90, 446, 1462, 568), "霍尔登补丁™由半柔性尼龙贴片与专利三组分环氧热固聚合物构成，可覆盖船体裂缝并形成气密封层，在太空环境中可安全维持长达五年。", min_font_size=18, max_font_size=30, align_left=True),
        ManualOverlay((90, 602, 1490, 810), "使用霍尔登补丁™时，只需将尼龙贴片覆盖在船体裂缝上，并用临时磁铁固定。随后拉开四边拉绳释放聚环氧化物，沿补丁外缘按压使三组分材料充分混合。约三十秒后开始硬化，两分钟后即可完全固化并安全用于太空环境。", min_font_size=18, max_font_size=28, align_left=True),
        ManualOverlay((1140, 838, 1495, 916), "用于打捞作业", min_font_size=18, max_font_size=34),
        ManualOverlay((90, 944, 1490, 1232), "打捞太空残骸时，问题总是一样：它到底破了多少处？修完又要多久？原本顺手的副业，往往会变成花上数小时焊接地板、墙壁和天花板的大工程。霍尔登补丁™提供了几分钟内完成紧急修补的办法；一旦发现舱室因船体裂缝失压，只要贴上补丁、混合密封材料，就能迅速恢复安全。", min_font_size=18, max_font_size=28, align_left=True),
        ManualOverlay((1160, 1264, 1495, 1338), "用于竞速比赛", min_font_size=18, max_font_size=34),
        ManualOverlay((90, 1368, 1468, 1578), "想象一下，你正在赛道最后一圈，以高 G 绕过小行星时擦撞岩面，船体当场受损。原本稳拿的胜利，瞬间变成全员冲去焊补船壳的灾难。有了霍尔登补丁™，比赛中临时加热金属板的日子一去不返。只要把补丁拍上去、拉开拉绳，就能继续前冲，让对手只能在你的尾焰里吃灰。", min_font_size=18, max_font_size=28, align_left=True),
        ManualOverlay((900, 1608, 1490, 1682), "用于长途航行", min_font_size=18, max_font_size=34),
        ManualOverlay((90, 1708, 1490, 1956), "筹备星际远航，拼的就是资源。你能带多少，又能省下什么？过去，长途船员每次横渡虚空都得携带沉重的替换地板、墙壁和天花板。霍尔登补丁™轻便、可堆叠，货架寿命可达数十年。下次你规划从内行星飞往小行星带乃至更远处时，带上一叠霍尔登补丁™，整段长途都会安心许多。", min_font_size=18, max_font_size=28, align_left=True),
    ],
    ("Fusion Reactor Manual", "007.png"): [
        ManualOverlay((150, 130, 980, 260), "标准反应堆组件", min_font_size=30, max_font_size=58, padding=14),
        ManualOverlay(
            (220, 260, 980, 1260),
            "核心系统\n"
            "1  ICFR 核心\n"
            "2  聚变场线圈组件\n"
            "3  聚变核心泵\n\n"
            "点火系统\n"
            "4  聚变级激光电容器\n"
            "5  聚变激光组件\n\n"
            "燃料系统\n"
            "6  反应堆燃料调节器\n"
            "7  弹丸进料器组件\n"
            "8  D2O 罐\n"
            "9  液态 He3 罐\n\n"
            "冷却系统\n"
            "10  低温分配泵\n"
            "11  低温储罐\n\n"
            "电力系统\n"
            "12  磁流体（MHD）发电机\n"
            "13  舰船电池",
            min_font_size=20,
            max_font_size=38,
            align_left=True,
            padding=14,
        ),
        ManualOverlay((220, 1910, 1340, 1990), "△△△ 本文件必须随时可供 SULAIMAN “X[X] TW” ICFR 技术人员查阅 △△△", min_font_size=14, max_font_size=20, padding=10),
    ],
    ("Fusion Reactor Manual", "008.png"): [
        ManualOverlay((65, 70, 620, 250), "核心系统", min_font_size=28, max_font_size=54, align_left=True, padding=6),
        ManualOverlay((650, 165, 1510, 500), "ICFR 核心\nSulaiman “X[X] TW” 惯性约束聚变反应堆核心包含控制系统、约束腔、烧蚀壁与主驱动结构。", min_font_size=22, max_font_size=34, align_left=True, padding=8),
        ManualOverlay((65, 560, 1510, 900), "安装指南：\nICFR 核心直接围绕聚变场线圈组件构建。安装时请确保核心正确居中于线圈组件上方。\n\n反应堆电力输入接口由向内的红色箭头标示（见图中反应堆左下角）。\n反应堆输出接口由向外的红色箭头标示（见图中舰船电池左上方）。", min_font_size=20, max_font_size=30, align_left=True, padding=8),
        ManualOverlay((650, 1080, 1510, 1340), "聚变场线圈组件\n聚变场线圈组件是反应堆核心的关键组成部分，用于保护核心免受损伤。", min_font_size=22, max_font_size=34, align_left=True, padding=8),
        ManualOverlay((65, 1450, 1510, 1910), "安装指南：\n聚变场线圈组件直接安装在 Sulaiman X[X] TW ICFR 核心内部，为核心等离子体提供必要约束。\n它需要一个由两格 [2] 地板框架组成的完整支撑环，并在支撑环中心移除一格 [1] 地板，以直接接触真空。", min_font_size=20, max_font_size=28, align_left=True, padding=8),
        ManualOverlay((35, 2170, 1560, 2288), "△△△ 本文件必须始终可供苏莱曼“X[X] TW”惯性约束聚变反应堆技术人员查阅 △△△", min_font_size=14, max_font_size=20, padding=20),
    ],
    ("Fusion Reactor Manual", "009.png"): [
        ManualOverlay((65, 150, 1505, 430), "聚变堆芯泵\n聚变堆芯泵可直接从反应堆核心排出等离子体或残余气体，而不会给飞船带来推力。", min_font_size=22, max_font_size=34, align_left=True, padding=8),
        ManualOverlay((65, 520, 1510, 700), "安装指南：\n聚变堆芯泵可连接到反应堆核心外围十二个 [12] 进料端口中的任意一个。", min_font_size=20, max_font_size=30, align_left=True, padding=8),
        ManualOverlay((65, 715, 620, 860), "点火系统", min_font_size=28, max_font_size=52, align_left=True, padding=8),
        ManualOverlay((980, 820, 1505, 1150), "聚变激光组件（左）\n聚变级激光电容器（右）\n点火系统会以高功率激光瞄准聚变颗粒，在颗粒到达堆芯腔室中心时触发聚变。", min_font_size=20, max_font_size=30, align_left=True, padding=8),
        ManualOverlay((170, 1050, 900, 1140), "堆芯腔室中心。", min_font_size=20, max_font_size=32, padding=8),
        ManualOverlay((65, 1240, 1510, 1600), "安装指南：\n聚变激光组件与聚变级激光电容器均可连接到反应堆核心外围十二个 [12] 堆芯进料端口中的任意一个。\n\n一个 [1] 聚变级激光电容器可为两个 [2] 聚变激光组件提供足够电力。", min_font_size=20, max_font_size=28, align_left=True, padding=8),
        ManualOverlay((35, 1910, 1560, 2028), "△△△ 本文件必须始终可供苏莱曼“X[X] TW”惯性约束聚变反应堆技术人员查阅 △△△", min_font_size=14, max_font_size=20, padding=20),
    ],
    ("Fusion Reactor Manual", "010.png"): [
        ManualOverlay((65, 85, 620, 230), "燃料系统", min_font_size=28, max_font_size=54, align_left=True, padding=8),
        ManualOverlay((980, 215, 1505, 700), "燃料丸供给组件（左）\n反应堆燃料调节器（右）\n燃料系统以预混、压缩并带电的颗粒形式为反应堆提供燃料。调节器负责预成形每颗燃料丸，供给组件则将颗粒高速注入堆芯。", min_font_size=20, max_font_size=30, align_left=True, padding=8),
        ManualOverlay((350, 545, 1110, 650), "将颗粒流高速直接注入堆芯。", min_font_size=22, max_font_size=34, padding=8),
        ManualOverlay((65, 700, 1510, 1015), "安装指南：\n燃料丸供给组件和反应堆燃料调节器都可连接到反应堆核心外围十二个 [12] 堆芯进料端口中的任意一个。\n\n一个 [1] 反应堆燃料调节器可为两个 [2] 燃料丸供给组件提供足够的燃料丸。", min_font_size=20, max_font_size=28, align_left=True, padding=8),
        ManualOverlay((980, 1120, 1505, 1515), "D2O 罐（左）\nHe3 罐（右）\n反应堆燃料罐连接到地板下集成燃料舱，用于储存 ICFR 运行所需的氘（D2O）和氦-3（He-3）燃料。", min_font_size=20, max_font_size=28, align_left=True, padding=8),
        ManualOverlay((65, 1540, 1510, 2135), "安装指南：\n反应堆燃料罐除标准占地外，还需要一个由两格 [2] 地板框架组成的完整支撑环。\n\n当两个反应堆燃料罐靠得较近时，至少需要保留四格 [4] 框架距离，以便为较宽的罐体部分和地板下管路留出足够空间。\n\n[注：由于氘/氦-3 聚变反应的特性，正常运行的反应堆会按约 1:5 的比例消耗 D2O 和 He3。]", min_font_size=18, max_font_size=26, align_left=True, padding=8),
        ManualOverlay((35, 2170, 1560, 2288), "△△△ 本文件必须始终可供苏莱曼“X[X] TW”惯性约束聚变反应堆技术人员查阅 △△△", min_font_size=14, max_font_size=20, padding=20),
    ],
    ("Fusion Reactor Manual", "011.png"): [
        ManualOverlay((65, 85, 620, 230), "冷却系统", min_font_size=28, max_font_size=54, align_left=True, padding=8),
        ManualOverlay((960, 220, 1505, 700), "低温储液罐（左）\n低温分配泵（右）\n低温分配泵及其配套的低温储液罐是自动调节反应堆核心温度的关键组件。", min_font_size=20, max_font_size=30, align_left=True, padding=8),
        ManualOverlay((65, 740, 1510, 1020), "安装指南：\n低温储液罐除标准占地外，还需要一个由两格 [2] 地板框架组成的完整支撑环。\n\n低温分配泵可连接到反应堆核心外围十二个 [12] 进料端口中的任意一个。", min_font_size=20, max_font_size=28, align_left=True, padding=8),
        ManualOverlay((65, 1060, 620, 1205), "电力系统", min_font_size=28, max_font_size=52, align_left=True, padding=8),
        ManualOverlay((1080, 1260, 1505, 1635), "舰船电池（左）\n1：迷你 XS\n2：紧凑 S\n3：标准\nMHD 发电机（右）\n电池在启动期间\n为反应堆组件提供电荷。", min_font_size=18, max_font_size=28, align_left=True, padding=8),
        ManualOverlay((180, 1635, 1490, 1765), "少量等离子体会被引导流经 MHD 发电机，以产生电力并恢复舰船电池电量。", min_font_size=20, max_font_size=30, padding=8),
        ManualOverlay((65, 1860, 1510, 2175), "安装指南：\n电池充电连接由向内的红色箭头标示（见图中舰船电池底部）。\n电池输出连接由向外的红色箭头标示（见图中舰船电池顶部）。", min_font_size=20, max_font_size=28, align_left=True, padding=8),
        ManualOverlay((35, 2170, 1560, 2288), "△△△ 本文件必须始终可供苏莱曼“X[X] TW”惯性约束聚变反应堆技术人员查阅 △△△", min_font_size=14, max_font_size=20, padding=20),
    ],
    ("Fusion Reactor Manual", "016.png"): [
        ManualOverlay((165, 145, 760, 210), "反应堆控制面板 第3部分", min_font_size=24, max_font_size=38, align_left=True, padding=6),
        ManualOverlay((165, 210, 760, 275), "反应堆设置与输出", min_font_size=24, max_font_size=38, align_left=True, padding=6),
        ManualOverlay(
            (70, 1260, 1510, 2060),
            "1 电源指示灯\n"
            "a) 总功率\n"
            "b) FUS（聚变发电）\n"
            "c) MHD（磁流体动力学发电机输出）\n"
            "d) THR（总推力输出）\n"
            "e) LOAD（总系统负载）\n\n"
            "2 电源设置\n"
            "a) PWR BUS（电源总线）\n"
            "a1: BATT（外接电池供电模式）\n"
            "反应堆从已连接的舰船电池取电，用于启动操作。\n"
            "a2: OFF（模式执行就绪/访问待机）\n"
            "切断反应堆全部供电。\n"
            "a3: CHRG（主动充电状态）\n"
            "正常运行，允许 MHD 电力回充至舰船电池。\n"
            "b) MHD（磁流体动力学发电机等离子体供给开关）\n"
            "激活 MHD，并在反应堆处于循环状态且 PWR BUS 设为 CHRG 时允许电池充电。\n\n"
            "3 反应堆流量节流阀\n"
            "调节进入反应堆核心的燃料输送速率；会产生额外热量与电力。",
            min_font_size=18,
            max_font_size=28,
            align_left=True,
            padding=10,
        ),
        ManualOverlay((35, 2170, 1560, 2288), "△△△ 本文件必须始终可供苏莱曼“X[X] TW”惯性约束聚变反应堆技术人员查阅 △△△", min_font_size=14, max_font_size=20, padding=20),
    ],
    ("Fusion Reactor Manual", "017.png"): [
        ManualOverlay(
            (70, 80, 1510, 730),
            "4 反应堆推力设置\n"
            "a) THRUST 指示灯（ACTIVE）\n"
            "指示反应堆已设为开环运行，并可能产生推力。\n"
            "b) 反应堆循环节流阀\n"
            "调节反应堆核心孔径，允许直接等离子体排气。\n"
            "[注意：直接操作反应堆推力控制将绕过 G-SAFE 锁定，可能产生极端 G 力！]\n"
            "c) THRUST ALLOW 开关与安全盖\n"
            "设为 ACTIVE 时启用反应堆核心排气。\n"
            "d) STATION PROXIMITY WARN\n"
            "警告指示灯会在舰船位于本地 NO-WAKE ZONE 内时亮起。\n"
            "启用反应堆推力前，请先用 RCS 动力驶离 NO-WAKE ZONE。",
            min_font_size=20,
            max_font_size=30,
            align_left=True,
            padding=10,
        ),
        ManualOverlay((70, 790, 760, 845), "反应堆控制面板 第4部分", min_font_size=24, max_font_size=38, align_left=True, padding=2),
        ManualOverlay((70, 845, 360, 885), "燃料表", min_font_size=24, max_font_size=38, align_left=True, padding=2),
        ManualOverlay(
            (70, 1600, 1510, 1990),
            "反应堆燃料罐储量\n"
            "1 He3（氦-3）反应堆燃料 HMD 读数\n"
            "a) 显示 He3 [He-3]（氦-3）总储量，单位千克。\n"
            "b) 显示当前反应堆状态下 He3 [He-3]（氦-3）的消耗率，单位 kg/天（每日总消耗千克数）。\n\n"
            "2 D [D2O]（氘）反应堆燃料 HMD 读数\n"
            "a) 显示 D [D2O]（氘）总储量，单位千克。\n"
            "b) 显示当前反应堆状态下 D [D2O]（氘）的消耗率，单位 kg/天（每日总消耗千克数）。",
            min_font_size=20,
            max_font_size=30,
            align_left=True,
            padding=10,
        ),
        ManualOverlay((35, 2170, 1560, 2288), "△△△ 本文件必须始终可供苏莱曼“X[X] TW”惯性约束聚变反应堆技术人员查阅 △△△", min_font_size=14, max_font_size=20, padding=20),
    ],
    ("Fusion Reactor Manual", "019.png"): [
        ManualOverlay((165, 145, 760, 210), "反应堆控制面板 第6部分", min_font_size=24, max_font_size=38, align_left=True, padding=6),
        ManualOverlay((165, 210, 1080, 275), "内部核心压力与电容器充电", min_font_size=24, max_font_size=38, align_left=True, padding=6),
        ManualOverlay(
            (70, 860, 1510, 1125),
            "1 核心压力显示\n"
            "a) VAC（真空）\n"
            "b) ROUGH（压力过低，聚变不稳定）\n"
            "c) GOOD（无标记；标准压力范围，聚变最佳）\n"
            "d) DANGER（高压；ABL WALL 损伤与包容失效风险高）",
            min_font_size=20,
            max_font_size=30,
            align_left=True,
            padding=8,
        ),
        ManualOverlay((165, 1140, 760, 1195), "反应堆控制面板 第7部分", min_font_size=24, max_font_size=38, align_left=True, padding=4),
        ManualOverlay((165, 1195, 900, 1230), "集中就绪与警示面板", min_font_size=24, max_font_size=38, align_left=True, padding=4),
        ManualOverlay(
            (70, 2040, 1510, 2135),
            "1 X 射线检测\n"
            "a) X 射线探测器功能指示灯",
            min_font_size=20,
            max_font_size=30,
            align_left=True,
            padding=8,
        ),
        ManualOverlay((35, 2170, 1560, 2288), "△△△ 本文件必须始终可供苏莱曼“X[X] TW”惯性约束聚变反应堆技术人员查阅 △△△", min_font_size=14, max_font_size=20, padding=20),
    ],
    ("Fusion Reactor Manual", "020.png"): [
        ManualOverlay(
            (70, 70, 1510, 1040),
            "b) X 射线探测警示灯\n"
            "2 ABL WALL（防护烧蚀壁）\n"
            "a) ABL WALL\n"
            "b) ABL WALL 警示灯\n"
            "[注：若 ABL WARN 灯显示\n"
            "闪烁：ABL WALL 剩余 50%\n"
            "- 下次维护时处理\n"
            "常亮：ABL WALL 已完全或接近完全烧蚀\n"
            "- 立即关闭，禁止使用]\n"
            "3 LAS CAP（激光电容器）\n"
            "a) X 射线探测器\n"
            "b) XRAY WARN 警示灯\n"
            "4 LAS ALIGN（激光阵列对准）\n"
            "a) 激光阵列正确对准\n"
            "b) 电容器已完全充电\n"
            "5 PELL FEED（燃料丸进料器）",
            min_font_size=20,
            max_font_size=30,
            align_left=True,
            padding=8,
        ),
        ManualOverlay((165, 1135, 760, 1190), "反应堆控制面板 第8部分", min_font_size=24, max_font_size=38, align_left=True, padding=4),
        ManualOverlay((165, 1190, 980, 1235), "核心包容、进入与提取", min_font_size=24, max_font_size=38, align_left=True, padding=4),
        ManualOverlay(
            (70, 1775, 1510, 2135),
            "1 场线圈控制\n"
            "聚变场线圈组件是反应堆系统的一个组成部分，\n"
            "通过其产生的电磁场为舰船提供保护。\n\n"
            "a) FWD\n"
            "启动前部场线圈组件。",
            min_font_size=20,
            max_font_size=30,
            align_left=True,
            padding=8,
        ),
        ManualOverlay((35, 2170, 1560, 2288), "△△△ 本文件必须始终可供苏莱曼“X[X] TW”惯性约束聚变反应堆技术人员查阅 △△△", min_font_size=14, max_font_size=20, padding=20),
    ],
    ("Fusion Reactor Manual", "021.png"): [
        ManualOverlay(
            (60, 70, 1510, 1940),
            "b) REAR\n"
            "启用后部场线圈组件\n\n"
            "[注意：未建立包容时请勿操作反应堆]\n\n"
            "2 反应堆燃料调节器控制\n"
            "FUEL REG（反应堆燃料调节器）按钮用于将反应堆燃料调节器设为通电或断电状态。"
            "反应堆燃料调节器是 PELL FEED 功能运行所必需的。"
            "将 FUEL REG 设为 OFF 位置可安全关闭反应堆。\n\n"
            "3 核心吹扫\n"
            "CORE PURGE 旋钮有三个位置设定，用于控制反应堆的聚变核心泵 [如已安装]。\n"
            "a) OFF\n"
            "将 CORE PURGE 旋钮置于 OFF 位置，聚变核心泵进入断电状态。"
            "在此状态下，聚变核心泵不会对反应堆核心进行排气。\n"
            "b) RGH（常规）\n"
            "将 CORE PURGE 旋钮置于 RGH 位置，聚变核心泵进入通电状态。"
            "在此状态下，聚变核心泵将开始安全地对反应堆核心进行排气。\n"
            "c) TRB（涡轮）\n"
            "将 CORE PURGE 旋钮置于 TRB 位置，聚变核心泵进入通电状态并以高容量“涡轮”速度运行。"
            "在此状态下，聚变核心泵将快速对反应堆核心进行排气。\n\n"
            "[注：聚变核心泵无法在聚变进行时对反应堆核心进行排气。]\n\n"
            "反应堆控制面板 第9部分\n"
            "核心点火",
            min_font_size=20,
            max_font_size=30,
            align_left=True,
            padding=8,
        ),
        ManualOverlay((35, 2170, 1560, 2288), "△△△ 本文件必须始终可供苏莱曼“X[X] TW”惯性约束聚变反应堆技术人员查阅 △△△", min_font_size=14, max_font_size=20, padding=20),
    ],
    ("Fusion Reactor Manual", "022.png"): [
        ManualOverlay(
            (60, 930, 1510, 1980),
            "1 反应堆就绪指示灯\n"
            "当指示灯显示 READY 时，聚变反应点火已准备就绪。\n\n"
            "2 反应堆点火开关\n"
            "将 IGNITION 开关置于 IGNITION 位置时，反应堆启动。\n"
            "如果 POWER BUS 置于 OFF 位置，点火不会触发。\n"
            "如果 CAPACITOR 未 READY，点火不会触发。\n"
            "如果 CORE PRESSURE 未达到 VAC，点火不会触发。\n"
            "如果 LASER ALIGNMENT 未完成，点火不会触发。\n"
            "如果 FUEL REGULATOR 未激活，点火不会触发。\n"
            "如果 PELLET FEED 未激活，点火不会触发。\n\n"
            "[△ 注意：在无 CRYO COOLING 和 FIELD CONTAINMENT 的情况下，点火仍会触发，用于低功率紧急启动。]\n\n"
            "紧急低功率反应堆启动后，立即激活场线圈和低温冷却。"
            "在无场约束或冷却的情况下启动反应堆，将使苏莱曼 X[X]TW 内部约束聚变反应堆保修失效。",
            min_font_size=20,
            max_font_size=30,
            align_left=True,
            padding=8,
        ),
        ManualOverlay((35, 2170, 1560, 2288), "△△△ 本文件必须始终可供苏莱曼“X[X] TW”惯性约束聚变反应堆技术人员查阅 △△△", min_font_size=14, max_font_size=20, padding=20),
    ],
}


PAGE_IMAGE_PRESERVE_REGIONS = {
    ("Fusion Reactor Manual", "008.png"): ((203, 312, 610, 718), (203, 1236, 610, 1642), (1165, 36, 1215, 88)),
    ("Fusion Reactor Manual", "009.png"): ((204, 237, 479, 466), (205, 776, 896, 1016), (1165, 36, 1215, 88)),
    ("Fusion Reactor Manual", "010.png"): ((205, 297, 910, 520), (206, 1066, 907, 1410), (1165, 36, 1215, 88)),
    ("Fusion Reactor Manual", "011.png"): ((199, 288, 869, 705), (199, 1179, 1034, 1598), (1165, 36, 1215, 88)),
    ("Fusion Reactor Manual", "016.png"): ((203, 287, 1451, 1243), (1165, 36, 1215, 88)),
    ("Fusion Reactor Manual", "017.png"): ((203, 890, 1451, 1579), (1165, 36, 1215, 88)),
    ("Fusion Reactor Manual", "019.png"): ((203, 287, 1451, 841), (203, 1237, 1451, 2015), (1165, 36, 1215, 88)),
    ("Fusion Reactor Manual", "020.png"): ((203, 1259, 1451, 1751), (1165, 36, 1215, 88)),
}


NON_PDF_PAGE_OVERRIDES = {
    "Ayotimiwa Salvage License Notice": [
        ManualOverlay((40, 205, 1540, 430), "AYOTIMIWA 废船打捞条例与许可证", min_font_size=28, max_font_size=74),
        ManualOverlay((40, 455, 1540, 760), "Ayotimiwa 拆船公司董事会已依据公司法典第 234 条采取行动，以规范船舶打捞与转售。\n\n该措施将要求符合审查标准，并通过“仅限持证人”终端签发按日计费的许可证。\n\n在列明期限内，许可证持有人获准进入指定外域区域内属于 Ayotimiwa 的废弃财产，并享有打捞权。", min_font_size=18, max_font_size=38),
        ManualOverlay((190, 770, 1390, 930), "下列为常见问题：", min_font_size=24, max_font_size=44),
        ManualOverlay((40, 945, 1540, 1085), "问：许可证多少钱？", min_font_size=18, max_font_size=34),
        ManualOverlay((40, 1070, 1540, 1190), "答：许可证每 24 小时收费 5000 美元。", min_font_size=18, max_font_size=34),
        ManualOverlay((40, 1210, 1540, 1345), "问：如果我没有 Ayotimiwa 打捞许可证，会被开罚单吗？", min_font_size=18, max_font_size=30),
        ManualOverlay((40, 1330, 1540, 1515), "答：会。如果你的业务被认定符合“打捞服务商”的条例定义，那么在没有许可证或没有适当设备的情况下持续经营的每一天，都可能被开罚单。", min_font_size=15, max_font_size=28),
        ManualOverlay((40, 1510, 1540, 1655), "问：如果我不办理 Ayotimiwa 打捞许可证，Ayotimiwa 拆船公司能关闭我的生意吗？", min_font_size=17, max_font_size=28),
        ManualOverlay((40, 1645, 1540, 1805), "答：可以。所有违反该条例却继续运营的企业，都可能被 OKLG 港务局处以罚款，最高处罚包括监禁和资产没收。", min_font_size=15, max_font_size=28),
        ManualOverlay((40, 1795, 1540, 1935), "问：Ayotimiwa 如何认定一家企业属于打捞服务商还是转售商？", min_font_size=18, max_font_size=28),
        ManualOverlay((40, 1920, 1540, 2040), "答：地方当局将在 NASS 指定船只上巡逻 OKLG 空域，并检查正在打捞的船舶。若观察到条例所定义的打捞作业，则该企业会被认定为打捞服务商，必须持有许可证并配备符合条例的设备。若企业没有许可证或合规设备，便可被处以罚款。", min_font_size=12, max_font_size=22),
    ],
    "CO2 AtmoScrubber User Guide": [
        ManualOverlay((95, 60, 520, 190), "KANG CO2 大气净化器™\n示意图", padding=8, min_font_size=16, max_font_size=30),
        ManualOverlay((120, 445, 620, 560), "CO2 语义标记", min_font_size=18, max_font_size=34),
        ManualOverlay((1085, 560, 1560, 690), "大气入口", min_font_size=18, max_font_size=30),
        ManualOverlay((1085, 715, 1560, 825), "系统显示", min_font_size=18, max_font_size=30),
        ManualOverlay((1050, 790, 1570, 955), "系统控制\n旋开后可接入数据链路", min_font_size=15, max_font_size=24),
        ManualOverlay((1075, 935, 1565, 1080), "滤芯抽屉", min_font_size=18, max_font_size=30),
        ManualOverlay((835, 1090, 1565, 1210), "“净化后”大气排出口", min_font_size=17, max_font_size=28),
        ManualOverlay((90, 970, 520, 1085), "CO2 排出口", min_font_size=18, max_font_size=30),
        ManualOverlay((90, 1085, 630, 1290), "CO2 罐体\n请确保设备运行期间\n已安装合适的罐体", min_font_size=15, max_font_size=24),
        ManualOverlay((860, 1950, 1570, 2035), "KANG 对未能维护或正确操作本 CO2 过滤装置不承担责任", padding=8, min_font_size=10, max_font_size=16),
    ],
    "Coming Soon": [
        ManualOverlay((250, 350, 540, 490), "更多文档\n即将推出", min_font_size=24, max_font_size=44),
    ],
    "Intentionally Blank": [
        ManualOverlay((180, 330, 610, 560), "本页有意留白。", min_font_size=24, max_font_size=42),
    ],
}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Render translated manual PNGs from source PDFs and images.")
    parser.add_argument("--input-root", type=Path, required=True, help="Source manuals root, usually StreamingAssets/images/manuals.")
    parser.add_argument("--output-root", type=Path, required=True, help="Output manuals root, usually workspace/mod-images/manuals.")
    parser.add_argument("--page", default=None, help="Optional manual folder name. Omit to render all supported manuals.")
    parser.add_argument("--config-path", default=None, help="Optional config.ini path used to read DeepSeek settings.")
    parser.add_argument("--cache-path", default=None, help="Optional JSON cache path for manual translations.")
    parser.add_argument("--font-path", default=None, help="Optional font path for CJK text.")
    parser.add_argument("--padding", type=int, default=6, help="Extra pixels to clear around each text block.")
    parser.add_argument("--min-font-size", type=int, default=8, help="Minimum font size.")
    parser.add_argument("--max-font-size", type=int, default=96, help="Maximum font size.")
    parser.add_argument("--batch-size", type=int, default=DEFAULT_BATCH_SIZE, help="Translation batch size.")
    return parser.parse_args()


def extract_pdf_page_plans(input_root: Path, output_root: Path, page_filter: str | None) -> list[PdfPagePlan]:
    plans: list[PdfPagePlan] = []
    for folder in sorted(path for path in input_root.iterdir() if path.is_dir()):
        if page_filter and folder.name != page_filter:
            continue

        pdf_paths = sorted(folder.glob("*.pdf"))
        if not pdf_paths:
            continue

        pdf_path = pdf_paths[0]
        document = fitz.open(pdf_path)
        png_paths = sorted(folder.glob("*.png"))
        page_count = min(document.page_count, len(png_paths))
        for page_index in range(page_count):
            page = document[page_index]
            blocks = []
            for block in page.get_text("dict")["blocks"]:
                if block.get("type") != 0:
                    continue

                if should_split_block_into_lines(block["lines"]):
                    for line in block["lines"]:
                        text = "".join(span["text"] for span in line["spans"])
                        line_text = text.strip()
                        if not line_text:
                            continue

                        blocks.append((tuple(line["bbox"]), line_text))
                    continue

                lines = []
                for line in block["lines"]:
                    text = "".join(span["text"] for span in line["spans"])
                    if text:
                        lines.append(text)

                block_text = "\n".join(lines).strip()
                if not block_text:
                    continue

                blocks.append((tuple(block["bbox"]), block_text))

            blocks = merge_pdf_blocks(blocks)

            image_path = png_paths[page_index]
            output_path = output_root / folder.name / image_path.name
            plans.append(
                PdfPagePlan(
                    folder_name=folder.name,
                    image_path=image_path,
                    output_path=output_path,
                    pdf_size=(page.rect.width, page.rect.height),
                    blocks=tuple(blocks),
                )
            )

    return plans


def resolve_config_path(args: argparse.Namespace) -> Path:
    if args.config_path:
        return Path(args.config_path)

    game_root_path = args.input_root.parents[3]
    return game_root_path / "OstranautsTranslator" / "config.ini"


def resolve_cache_path(args: argparse.Namespace) -> Path:
    if args.cache_path:
        return Path(args.cache_path)

    workspace_root = args.output_root.parents[1]
    return workspace_root / "reference" / DEFAULT_CACHE_FILE_NAME


def load_translation_cache(cache_path: Path) -> dict[str, str]:
    if not cache_path.exists():
        return {}

    return json.loads(cache_path.read_text(encoding="utf-8"))


def save_translation_cache(cache_path: Path, cache: dict[str, str]) -> None:
    cache_path.parent.mkdir(parents=True, exist_ok=True)
    cache_path.write_text(json.dumps(cache, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def load_translator(config_path: Path) -> DeepSeekTranslator:
    parser = configparser.ConfigParser()
    parser.read(config_path, encoding="utf-8")
    api_key = os.environ.get("DEEPSEEK_API_KEY") or parser.get("LLMTranslate", "ApiKey", fallback="")
    if not api_key:
        raise ValueError(f"No DeepSeek API key was found in '{config_path}'.")

    model = parser.get("LLMTranslate", "Model", fallback="deepseek-v4-flash")
    proxy = os.environ.get("HTTPS_PROXY") or os.environ.get("HTTP_PROXY") or None
    return DeepSeekTranslator(
        api_key=api_key,
        model=model,
        source_language=DEFAULT_SOURCE_LANGUAGE,
        target_language=DEFAULT_TARGET_LANGUAGE,
        proxy=proxy,
        timeout_seconds=120,
        system_prompt=MANUAL_TRANSLATION_SYSTEM_PROMPT,
    )


def is_translatable_text(value: str) -> bool:
    return bool(TEXT_LIKE_PATTERN.search(value))


def collect_missing_texts(plans: list[PdfPagePlan], cache: dict[str, str]) -> list[str]:
    missing = []
    seen = set(cache)
    for plan in plans:
        overrides = PAGE_TRANSLATION_OVERRIDES.get(plan.folder_name, {})
        for _bbox, source_text in plan.blocks:
            if source_text in overrides:
                continue
            if not is_translatable_text(source_text):
                if source_text not in seen:
                    cache[source_text] = source_text
                    seen.add(source_text)
                continue
            if source_text in seen:
                continue
            missing.append(source_text)
            seen.add(source_text)
    return missing


def translate_batch_robust(translator: DeepSeekTranslator, texts: list[str]) -> list[str]:
    if not texts:
        return []

    try:
        return translator.translate_batch(texts)
    except RuntimeError:
        if len(texts) == 1:
            return texts

    midpoint = len(texts) // 2
    left = translate_batch_robust(translator, texts[:midpoint])
    right = translate_batch_robust(translator, texts[midpoint:])
    return left + right


def fill_translation_cache(plans: list[PdfPagePlan], cache: dict[str, str], translator: DeepSeekTranslator, batch_size: int, cache_path: Path) -> None:
    missing = collect_missing_texts(plans, cache)
    if not missing:
        return

    for start in range(0, len(missing), batch_size):
        batch = missing[start : start + batch_size]
        translations = translate_batch_robust(translator, batch)
        for source_text, translated_text in zip(batch, translations, strict=True):
            cache[source_text] = translated_text.strip() or source_text
        save_translation_cache(cache_path, cache)
        print(f"Translated manual blocks: {min(len(missing), start + len(batch))}/{len(missing)}")


def inset_bbox(bbox: tuple[int, int, int, int], horizontal_inset: int, vertical_inset: int) -> tuple[int, int, int, int]:
    left, top, right, bottom = bbox
    return (
        min(right - 1, left + horizontal_inset),
        min(bottom - 1, top + vertical_inset),
        max(left + 1, right - horizontal_inset),
        max(top + 1, bottom - vertical_inset),
    )


def measure_manual_lines(
    draw: ImageDraw.ImageDraw,
    lines: list[str],
    font: object,
    stroke_width: int,
    line_gap_ratio: float,
) -> tuple[list[tuple[int, int]], int]:
    metrics: list[tuple[int, int]] = []
    use_relaxed_multiline_height = len(lines) > 1
    for line in lines:
        line_bbox = draw.textbbox((0, 0), line or "Ay", font=font, stroke_width=stroke_width)
        glyph_height = line_bbox[3] - line_bbox[1]
        line_height = get_effective_line_height(font, glyph_height, stroke_width) if use_relaxed_multiline_height else glyph_height
        metrics.append((line_bbox[2] - line_bbox[0], line_height))

    min_gap = 3 if use_relaxed_multiline_height else 2
    line_gap = max(min_gap, int(getattr(font, "size", 12) * line_gap_ratio))
    total_height = sum(height for _width, height in metrics) + line_gap * max(0, len(metrics) - 1)
    return metrics, total_height


def fit_single_line_text_to_box(
    draw: ImageDraw.ImageDraw,
    text: str,
    font_path: Path,
    max_width: int,
    max_height: int,
    min_font_size: int,
    max_font_size: int,
    stroke_width: int,
) -> tuple[object, list[str]]:
    from PIL import ImageFont

    best_font: object | None = None
    for font_size in range(max_font_size, min_font_size - 1, -1):
        font = ImageFont.truetype(str(font_path), font_size)
        line_bbox = draw.textbbox((0, 0), text or "Ay", font=font, stroke_width=stroke_width)
        width = line_bbox[2] - line_bbox[0]
        height = line_bbox[3] - line_bbox[1]
        best_font = font
        if width <= max_width and height <= max_height:
            return font, [text]

    if best_font is None:
        raise RuntimeError("No usable font could be created for manuals.")

    return best_font, [text]


def render_manual_lines(
    draw: ImageDraw.ImageDraw,
    lines: list[str],
    font: object,
    bbox: tuple[int, int, int, int],
    fill: tuple[int, int, int, int],
    stroke_fill: tuple[int, int, int, int],
    stroke_width: int,
    align_left: bool,
) -> None:
    left, top, right, bottom = bbox
    box_width = max(1, right - left)
    box_height = max(1, bottom - top)
    line_gap_ratio = PDF_PARAGRAPH_LINE_GAP_RATIO if len(lines) > 1 else PDF_SINGLE_LINE_GAP_RATIO
    metrics, total_height = measure_manual_lines(draw, lines, font, stroke_width, line_gap_ratio)
    min_gap = 3 if len(lines) > 1 else 2
    line_gap = max(min_gap, int(getattr(font, "size", 12) * line_gap_ratio))
    cursor_y = top + max(0, (box_height - total_height) / 2)

    for line, (line_width, line_height) in zip(lines, metrics, strict=True):
        cursor_x = left if align_left else left + max(0, (box_width - line_width) / 2)
        draw.text(
            (cursor_x, cursor_y),
            line,
            font=font,
            fill=fill,
            stroke_width=stroke_width,
            stroke_fill=stroke_fill,
        )
        cursor_y += line_height + line_gap


def render_overlays(image: Image.Image, overlays: list[ManualOverlay], font_path: Path) -> None:
    draw = ImageDraw.Draw(image)

    for overlay in overlays:
        padded_bbox = expand_bbox(overlay.bbox, overlay.padding, image.width, image.height)
        background_color = sample_fill_color(image, padded_bbox)
        draw.rectangle(padded_bbox, fill=background_color)

        text_fill, stroke_fill = get_contrasting_colors(background_color)
        if overlay.text_fill is not None:
            text_fill = overlay.text_fill
        if overlay.stroke_fill is not None:
            stroke_fill = overlay.stroke_fill

        font, lines = fit_text_to_box(
            draw=draw,
            text=normalize_render_text(overlay.translated_text),
            font_path=font_path,
            max_width=max(1, padded_bbox[2] - padded_bbox[0]),
            max_height=max(1, padded_bbox[3] - padded_bbox[1]),
            min_font_size=overlay.min_font_size,
            max_font_size=overlay.max_font_size,
            stroke_width=1,
        )

        if overlay.align_left:
            lines = wrap_text(
                draw=draw,
                text=normalize_render_text(overlay.translated_text),
                font=font,
                max_width=max(1, padded_bbox[2] - padded_bbox[0]),
                stroke_width=1,
            )
            render_manual_lines(
                draw=draw,
                lines=lines,
                font=font,
                bbox=padded_bbox,
                fill=text_fill,
                stroke_fill=stroke_fill,
                stroke_width=1,
                align_left=True,
            )
            continue

        render_lines(
            draw=draw,
            lines=lines,
            font=font,
            bbox=padded_bbox,
            fill=text_fill,
            stroke_fill=stroke_fill,
            stroke_width=1,
        )


def render_composed_page(
    source_image: Image.Image,
    overlays: list[ManualOverlay],
    font_path: Path,
    preserve_regions: tuple[tuple[int, int, int, int], ...],
) -> Image.Image:
    background_sample_bbox = (0, 0, min(80, source_image.width), min(80, source_image.height))
    background_color = sample_fill_color(source_image, background_sample_bbox)
    composed = Image.new("RGBA", source_image.size, background_color)

    for left, top, right, bottom in preserve_regions:
        region = source_image.crop((left, top, right, bottom))
        composed.paste(region, (left, top))

    render_overlays(composed, overlays, font_path)
    return composed


def render_pdf_page(
    plan: PdfPagePlan,
    cache: dict[str, str],
    font_path: Path,
    padding: int,
    min_font_size: int,
    max_font_size: int,
) -> None:
    image = Image.open(plan.image_path).convert("RGBA")
    manual_overlays = PAGE_IMAGE_OVERLAY_OVERRIDES.get((plan.folder_name, plan.image_path.name))
    if manual_overlays:
        preserve_regions = PAGE_IMAGE_PRESERVE_REGIONS.get((plan.folder_name, plan.image_path.name))
        if preserve_regions:
            image = render_composed_page(image, manual_overlays, font_path, preserve_regions)
        else:
            render_overlays(image, manual_overlays, font_path)
        plan.output_path.parent.mkdir(parents=True, exist_ok=True)
        image.save(plan.output_path)
        print(f"Rendered {plan.folder_name}/{plan.image_path.name} -> {plan.output_path}")
        return

    draw = ImageDraw.Draw(image)
    scale_x = image.width / plan.pdf_size[0]
    scale_y = image.height / plan.pdf_size[1]
    overrides = PAGE_TRANSLATION_OVERRIDES.get(plan.folder_name, {})
    page_overrides = PAGE_IMAGE_TRANSLATION_OVERRIDES.get((plan.folder_name, plan.image_path.name), {})

    for bbox, source_text in plan.blocks:
        translation = page_overrides.get(source_text) or overrides.get(source_text) or cache.get(source_text)
        if not translation:
            continue

        translation = normalize_render_text(translation)

        left, top, right, bottom = bbox
        scaled_bbox = (
            int(round(left * scale_x)),
            int(round(top * scale_y)),
            int(round(right * scale_x)),
            int(round(bottom * scale_y)),
        )
        padded_bbox = expand_bbox(scaled_bbox, padding, image.width, image.height)
        background_color = sample_fill_color(image, padded_bbox)
        draw.rectangle(padded_bbox, fill=background_color)

        box_width = max(1, padded_bbox[2] - padded_bbox[0])
        box_height = max(1, padded_bbox[3] - padded_bbox[1])
        text_bbox = inset_bbox(
            padded_bbox,
            horizontal_inset=max(PDF_TEXTBOX_MIN_HORIZONTAL_INSET, int(box_width * PDF_TEXTBOX_HORIZONTAL_INSET_RATIO)),
            vertical_inset=max(PDF_TEXTBOX_MIN_VERTICAL_INSET, int(box_height * PDF_TEXTBOX_VERTICAL_INSET_RATIO)),
        )
        capped_max_font_size = max(min_font_size, int(max_font_size * PDF_TEXTBOX_MAX_FONT_SCALE))
        original_line_count = source_text.count("\n") + 1
        prefer_single_line = original_line_count == 1 and box_height <= PDF_SINGLE_LINE_HEIGHT_THRESHOLD
        align_left = original_line_count > 1

        text_fill, stroke_fill = get_contrasting_colors(background_color)
        if prefer_single_line:
            font, lines = fit_single_line_text_to_box(
                draw=draw,
                text=translation,
                font_path=font_path,
                max_width=max(1, text_bbox[2] - text_bbox[0]),
                max_height=max(1, text_bbox[3] - text_bbox[1]),
                min_font_size=PDF_SINGLE_LINE_MIN_FONT_SIZE,
                max_font_size=capped_max_font_size,
                stroke_width=1,
            )
        else:
            font, lines = fit_text_to_box(
                draw=draw,
                text=translation,
                font_path=font_path,
                max_width=max(1, text_bbox[2] - text_bbox[0]),
                max_height=max(1, text_bbox[3] - text_bbox[1]),
                min_font_size=min_font_size,
                max_font_size=capped_max_font_size,
                stroke_width=1,
            )

            if align_left:
                lines = wrap_text(
                    draw=draw,
                    text=translation,
                    font=font,
                    max_width=max(1, text_bbox[2] - text_bbox[0]),
                    stroke_width=1,
                )

        render_manual_lines(
            draw=draw,
            lines=lines,
            font=font,
            bbox=text_bbox,
            fill=text_fill,
            stroke_fill=stroke_fill,
            stroke_width=1,
            align_left=align_left,
        )

    plan.output_path.parent.mkdir(parents=True, exist_ok=True)
    image.save(plan.output_path)
    print(f"Rendered {plan.folder_name}/{plan.image_path.name} -> {plan.output_path}")


def normalize_render_text(text: str) -> str:
    return (
        text.replace("⚠", "△")
        .replace("►", ">")
        .replace("❏", "□")
    )


def render_non_pdf_pages(input_root: Path, output_root: Path, font_path: Path, page_filter: str | None) -> None:
    for folder_name, overlays in NON_PDF_PAGE_OVERRIDES.items():
        if page_filter and folder_name != page_filter:
            continue

        image_path = input_root / folder_name / "000.png"
        if not image_path.exists():
            continue

        output_path = output_root / folder_name / "000.png"
        image = Image.open(image_path).convert("RGBA")
        render_overlays(image, overlays, font_path)

        output_path.parent.mkdir(parents=True, exist_ok=True)
        image.save(output_path)
        print(f"Rendered {folder_name}/000.png -> {output_path}")


def main() -> int:
    args = parse_args()
    font_path = resolve_font_path(args.font_path)
    pdf_plans = extract_pdf_page_plans(args.input_root, args.output_root, args.page)
    cache_path = resolve_cache_path(args)
    cache = load_translation_cache(cache_path)

    if pdf_plans:
        translator = load_translator(resolve_config_path(args))
        fill_translation_cache(pdf_plans, cache, translator, args.batch_size, cache_path)
        save_translation_cache(cache_path, cache)

    for plan in pdf_plans:
        render_pdf_page(
            plan=plan,
            cache=cache,
            font_path=font_path,
            padding=args.padding,
            min_font_size=args.min_font_size,
            max_font_size=args.max_font_size,
        )

    render_non_pdf_pages(
        input_root=args.input_root,
        output_root=args.output_root,
        font_path=font_path,
        page_filter=args.page,
    )

    return 0


if __name__ == "__main__":
    raise SystemExit(main())