#!/usr/bin/env python3
"""Append a small, sanitized failure summary from a portable test run."""

from __future__ import annotations

import html
import pathlib
import re
import sys
import xml.etree.ElementTree as ET


def append_summary(result_dir: pathlib.Path, project: str, summary_path: pathlib.Path) -> None:
    report = result_dir / "results.trx"
    summary = [f"### Portable test failure: `{html.escape(project)}`", ""]

    if report.is_file():
        root = ET.parse(report).getroot()
        counters = root.find(".//{*}Counters")
        if counters is not None:
            summary.append(
                f"Executed {counters.get('total', 'unknown')}; "
                f"passed {counters.get('passed', 'unknown')}; "
                f"failed {counters.get('failed', 'unknown')}; "
                f"skipped {counters.get('notExecuted', 'unknown')}."
            )
        summary.append("")

        failures = [
            result
            for result in root.findall(".//{*}UnitTestResult")
            if result.get("outcome", "").casefold() == "failed"
        ]
        for result in failures[:20]:
            name = " ".join((result.get("testName") or "Unknown test").split())
            message = result.findtext(".//{*}ErrorInfo/{*}Message") or "No failure message was recorded."
            message = " ".join(message.split())[:400]
            summary.append(f"- `{html.escape(name)}`: {html.escape(message)}")
        if len(failures) > 20:
            summary.append(f"- {len(failures) - 20} additional failures are in the uploaded test artifact.")
        if not failures:
            summary.append("The TRX file contains no failed test cases; inspect the project console output.")
    else:
        summary.append("No TRX report was produced.")
        console = result_dir / "console.log"
        if console.is_file():
            errors = [
                " ".join(line.split())[:400]
                for line in console.read_text(encoding="utf-8", errors="replace").splitlines()
                if re.search(r"error|failed|exception", line, re.IGNORECASE)
            ]
            summary.extend(f"- {html.escape(line)}" for line in errors[-15:])

    with summary_path.open("a", encoding="utf-8") as output:
        output.write("\n".join(summary) + "\n")


if __name__ == "__main__":
    if len(sys.argv) != 4:
        raise SystemExit("usage: summarize-portable-tests.py RESULT_DIR PROJECT SUMMARY_PATH")
    append_summary(pathlib.Path(sys.argv[1]), sys.argv[2], pathlib.Path(sys.argv[3]))
