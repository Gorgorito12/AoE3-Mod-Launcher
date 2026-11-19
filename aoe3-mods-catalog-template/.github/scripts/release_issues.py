"""Keeps ONE issue per mod in step with check_releases.py's report. Run by release-check.yml.

    python .github/scripts/release_issues.py report.json --repo owner/name [--dry-run]

Rules, each a reason a modder is not pestered:
  - an INCONCLUSIVE mod (GitHub 5xx, rate limit, timeout) opens nothing and closes nothing;
  - an issue is opened only when the mod has findings, edited only when they CHANGE, and closed
    when the mod is clean again;
  - every @ in the body is put in backticks, so nobody is notified by a modder's text.
--dry-run prints the gh commands instead of running them (and reads no existing issue).
"""
import argparse
import json
import subprocess
import sys

TITLE = "Release check: {id}"


def body_for(verdict):
    lines = [f"- **error**: {e}" for e in verdict["errors"]] + [f"- warning: {w}" for w in verdict["warnings"]]
    if not lines:
        return None
    return ("The launcher cannot install this mod as the catalog describes it:\n\n"
            + "\n".join(lines).replace("@", "`@`")
            + "\n\nThis issue updates itself each day and closes when the mod is clean.")


def plan(report, open_issues):
    """What to do for each mod. open_issues: title -> {number, body}. Returns (action, ...) tuples."""
    actions = []
    for v in report:
        if v.get("inconclusive"):
            actions.append(("skip", v["id"]))
            continue
        title = TITLE.format(id=v["id"])
        issue = open_issues.get(title)
        body = body_for(v)
        if body is None:
            if issue:
                actions.append(("close", issue["number"]))
            continue
        if issue is None:
            actions.append(("create", title, body))
        elif issue["body"].strip() != body.strip():
            actions.append(("edit", issue["number"], body))
    return actions


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("report")
    ap.add_argument("--repo", required=True)
    ap.add_argument("--dry-run", action="store_true")
    a = ap.parse_args()
    report = json.load(open(a.report, encoding="utf-8"))

    def gh(*args):
        cmd = ["gh", *args, "--repo", a.repo]
        if a.dry_run:
            print("DRY RUN:", " ".join(cmd[:4]), "...")
            return "[]"
        return subprocess.run(cmd, capture_output=True, text=True, check=True).stdout

    open_issues = {}
    if not a.dry_run:
        for i in json.loads(gh("issue", "list", "--state", "open", "--search", 'in:title "Release check:"',
                               "--limit", "200", "--json", "number,title,body")):
            open_issues[i["title"]] = i

    for act in plan(report, open_issues):
        if act[0] == "skip":
            print(f"{act[1]}: inconclusive, leaving any issue as it is")
        elif act[0] == "close":
            gh("issue", "close", str(act[1]), "--comment", "Clean again.")
        elif act[0] == "create":
            gh("issue", "create", "--title", act[1], "--body", act[2])
        elif act[0] == "edit":
            gh("issue", "edit", str(act[1]), "--body", act[2])
    return 0


if __name__ == "__main__":
    sys.exit(main())
