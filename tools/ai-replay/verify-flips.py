# -*- coding: utf-8 -*-
"""Отчёт о несогласованности verify-part: одинаковый вход -> разный вердикт.

Использование:
    python verify-flips.py C:\\AiService\\request_logs [--months 2026-08 2026-09]

Группирует verify_*.json по строгому ключу (деталь + установка + заказ +
все комментарии + список аномалий + версия промпта) и печатает группы,
где модель отвечала и true, и false. Группы с разными числами (КПД,
машинное, кол-во, история) помечаются отдельно — там вердикт мог
поменяться законно вслед за правками мастера.

Только чтение логов, ничего не меняет.
"""
import glob
import json
import os
import sys
from collections import defaultdict


def load_verify(path):
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def key_of(doc):
    p = doc["request"]["part"]
    anom = tuple(
        sorted(
            (a.get("field"), a.get("description"))
            for a in doc["request"].get("anomalies", [])
        )
    )
    return (
        p.get("partName"),
        p.get("setup"),
        p.get("order"),
        p.get("masterSetupComment"),
        p.get("masterMachiningComment"),
        p.get("masterSetupDetail") or "",
        p.get("masterMachiningDetail") or "",
        p.get("masterComment") or "",
        anom,
        doc.get("promptVersion"),
    )


def numbers_of(doc):
    p = doc["request"]["part"]
    h = p.get("partsHistory") or {}
    return (
        p.get("setupRatio"),
        p.get("productionRatio"),
        p.get("machiningTime"),
        p.get("finishedCount"),
        h.get("recordsFound", 0),
    )


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 1
    log_dir, months = argv[1], argv[2:]
    files = []
    for m in months or sorted(os.listdir(log_dir)):
        files += glob.glob(os.path.join(log_dir, m, "verify_*.json"))
    print(f"verify-файлов: {len(files)}")

    groups = defaultdict(list)
    auto = 0
    for f in files:
        try:
            d = load_verify(f)
        except (OSError, json.JSONDecodeError) as e:
            print(f"WARN пропуск {f}: {e}", file=sys.stderr)
            continue
        if d.get("promptVersion") == "auto-approved":
            auto += 1
            continue
        groups[key_of(d)].append((f, d))
    print(f"auto-approved (без модели): {auto}")
    print(f"групп вызовов модели: {len(groups)}")

    div = {
        k: v for k, v in groups.items() if len({x[1]["response"].get("ok") for x in v}) > 1
    }
    print(f"расходящихся групп: {len(div)}")
    for n, (k, hits) in enumerate(
        sorted(div.items(), key=lambda kv: kv[0][0] or ""), 1
    ):
        same_numbers = len({numbers_of(d) for _, d in hits}) == 1
        print("=" * 100)
        print(f"#{n} part={k[0]} | уст={k[1]} | заказ={k[2]} | {k[9]}")
        print(f"  наладка: {k[3]} / {k[5][:60]}")
        print(f"  изгот.:  {k[4]} / {k[6][:60]}")
        print(f"  аномалий: {len(k[8])} | числа одинаковые: {same_numbers}")
        for f, d in sorted(hits, key=lambda x: x[0]):
            p = d["request"]["part"]
            print(
                f"   {os.path.basename(f)[-16:-5]} "
                f"ok={d['response'].get('ok')} "
                f"sr={p.get('setupRatio')} pr={p.get('productionRatio')} "
                f"mTime={p.get('machiningTime')} fin={p.get('finishedCount')} "
                f"remark={(d['response'].get('remark') or '')[:80]}"
            )
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
