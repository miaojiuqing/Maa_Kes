"""不经过 MFA GUI，直接用 MaaFW 驱动模拟器调试 pipeline。

用法（在仓库根目录）：
    python tools/debug_autopush.py probe [节点名前缀]   # 对当前画面跑一遍所有匹配节点的识别，打印分数
    python tools/debug_autopush.py run [秒数]           # 运行「自动推图」，逐节点打印结果，超时自动停止
    python tools/debug_autopush.py shot <名字>          # 截一张 1280x720 图到 debug_out/

常用参数：
    --adb    adb.exe 路径（默认取环境变量 MAA_ADB，否则 adb）
    --addr   设备地址（默认 127.0.0.1:16384，MuMu 12）
    --lang   zh_cn | zh_tw，决定是否叠加 resource_TC（默认 zh_tw）
    --entry  run 的入口节点（默认 工具-自动推图-任务开始层）

注意：
  - probe 用的 Context.run_recognition 不会应用节点的 inverse，看分数时要自己反过来理解。
  - 运行前先停掉 MFA 里正在跑的任务，否则两边会抢同一台设备。
  - 节点失败时会自动截图到 debug_out/run_<时间>/，MaaFW 详细日志在 debug_out/maa/debug/maafw.log。
"""

import argparse
import os
import sys
import time
from pathlib import Path

import cv2
from maa.context import Context, ContextEventSink
from maa.controller import AdbController
from maa.custom_action import CustomAction
from maa.resource import Resource
from maa.tasker import Tasker
from maa.toolkit import Toolkit

REPO = Path(__file__).resolve().parent.parent
ASSETS = REPO / "assets"
OUT = REPO / "debug_out"

sys.stdout.reconfigure(encoding="utf-8", line_buffering=True)


def build(args):
    OUT.mkdir(exist_ok=True)
    Toolkit.init_option(str(OUT / "maa"))
    res = Resource()
    bundles = ["resource"] + (["resource_TC"] if args.lang == "zh_tw" else [])
    for b in bundles:
        if not res.post_bundle(str(ASSETS / b)).wait().succeeded:
            sys.exit(f"加载资源失败: {b}")
    ctrl = AdbController(args.adb, args.addr)
    if not ctrl.post_connection().wait().succeeded:
        sys.exit(f"连接设备失败: {args.addr}")
    tasker = Tasker()
    tasker.bind(res, ctrl)
    if not tasker.inited:
        sys.exit("Tasker 初始化失败")
    return res, ctrl, tasker


def probe(res, tasker, prefix):
    names = sorted(n for n in res.node_list if n.startswith(prefix))

    class Probe(CustomAction):
        def run(self, context: Context, argv):
            context.tasker.controller.post_screencap().wait()
            img = context.tasker.controller.cached_image
            for n in names:
                d = context.run_recognition(n, img)
                if d is None:
                    print(f"       {n}: (无识别)")
                    continue
                scores = [(r.score, list(r.box)) for r in (d.all_results or []) if getattr(r, "score", None) is not None]
                info = "best=%.3f box=%s" % max(scores) if scores else f"algo={d.algorithm}"
                print(f"  {'HIT ' if d.hit else '    '} {n}: {info}")
            return True

    res.register_custom_action("__probe", Probe())
    tasker.post_task("__probe", {"__probe": {"action": {"type": "Custom", "param": {"custom_action": "__probe"}}}}).wait()


def run(tasker, entry, seconds):
    shotdir = OUT / time.strftime("run_%Y%m%d_%H%M%S")
    shotdir.mkdir(parents=True, exist_ok=True)

    class Sink(ContextEventSink):
        def on_node_pipeline_node(self, context, noti_type, detail):
            nt = getattr(noti_type, "name", str(noti_type))
            if "Start" in nt:
                return
            focus = f" | {detail.focus}" if detail.focus and "Succeeded" in nt else ""
            print(f"{time.strftime('%H:%M:%S')} {nt} {detail.name}{focus}")
            if "Failed" in nt:
                try:
                    context.tasker.controller.post_screencap().wait()
                    cv2.imwrite(str(shotdir / f"{time.strftime('%H%M%S')}_FAIL.png"), context.tasker.controller.cached_image)
                except Exception as e:  # 截图失败不影响任务
                    print("截图失败:", e)

    tasker.add_context_sink(Sink())
    # 与 tasks/工具/自动推图.json 的默认选项一致
    override = {"工具-自动推图-关卡已结束-可直接下一关": {"enabled": False}}
    job = tasker.post_task(entry, override)
    t0 = time.time()
    while not job.done:
        if time.time() - t0 > seconds:
            print("超时，停止任务")
            tasker.post_stop().wait()
            break
        time.sleep(1)
    print(f"结束: succeeded={job.succeeded} 用时 {int(time.time() - t0)}s，失败截图在 {shotdir}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("cmd", choices=["probe", "run", "shot"])
    ap.add_argument("arg", nargs="?")
    ap.add_argument("--adb", default=os.environ.get("MAA_ADB", "adb"))
    ap.add_argument("--addr", default="127.0.0.1:16384")
    ap.add_argument("--lang", choices=["zh_cn", "zh_tw"], default="zh_tw")
    ap.add_argument("--entry", default="工具-自动推图-任务开始层")
    args = ap.parse_args()

    res, ctrl, tasker = build(args)
    if args.cmd == "probe":
        probe(res, tasker, args.arg or "工具-自动推图-")
    elif args.cmd == "run":
        run(tasker, args.entry, int(args.arg or 1800))
    elif args.cmd == "shot":
        ctrl.post_screencap().wait()
        p = OUT / f"{args.arg or 'shot'}.png"
        cv2.imwrite(str(p), ctrl.cached_image)
        print(p)


if __name__ == "__main__":
    main()
