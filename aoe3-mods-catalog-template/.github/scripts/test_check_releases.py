"""Tests for check_releases.py. Run: python .github/scripts/test_check_releases.py

The shared cases are the point: the same file is read by the launcher's CatalogCheckerParityTests,
so the catalog check and the launcher cannot disagree about which asset a release installs. The
mod-level cases are mostly REFUSALS - a finding that is wrong opens an issue against a modder.
"""
import json
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import check_releases as cr  # noqa: E402
import release_issues as ri  # noqa: E402

CASES = json.loads((Path(__file__).resolve().parent / "check_releases_cases.json").read_text(encoding="utf-8"))


def release(tag, assets, prerelease=False, draft=False):
    return {"tag_name": tag, "prerelease": prerelease, "draft": draft, "assets": [{"name": a} for a in assets]}


def manifest(**gh):
    install = gh.pop("install", {})
    return {
        "id": "m", "sourceRepo": "o/r", "approvedReleaseTag": gh.pop("tag", "1.0"),
        "install": install, "update": {"mechanism": "GitHubReleases", "github": gh},
    }


class SharedCases(unittest.TestCase):
    def test_every_case(self):
        for c in CASES["cases"]:
            with self.subTest(c["name"]):
                names, non = c["assets"], c.get("nonPayload")
                self.assertEqual(cr.pick_asset_index(names, non), c["pick"])
                self.assertEqual(cr.pick_payload_parts(names, non), c["parts"])
                if not c["parts"]:
                    self.assertEqual(cr.unusable_part_set(names) is not None, c["unusable"])


class ModChecks(unittest.TestCase):
    def test_THE_ONE_THAT_MATTERS_a_missing_approved_tag_is_an_error(self):
        v = cr.check_mod(manifest(tag="1.01"), [release("1.0.0", ["Mod.zip"])], True)
        self.assertTrue(any("1.01" in e for e in v.errors))

    def test_with_follow_latest_it_is_only_a_warning(self):
        v = cr.check_mod(manifest(tag="1.01", followLatest=True), [release("1.0.0", ["Mod.zip"])], True)
        self.assertEqual(v.errors, [])
        self.assertTrue(v.warnings)

    def test_a_healthy_mod_is_clean(self):
        self.assertTrue(cr.check_mod(manifest(), [release("1.0", ["Mod.zip"])], True).clean)

    def test_a_missing_repository(self):
        self.assertTrue(cr.check_mod(manifest(), None, False).errors)

    def test_a_patch_only_release_is_fine_when_a_baseline_exists(self):
        rels = [release("1.1", ["patch-1.0-to-1.1.zip", "patch-1.0-to-1.1.json"]), release("1.0", ["Mod.zip"])]
        self.assertTrue(cr.check_mod(manifest(tag="1.1", deltaPatches=True), rels, True).clean)

    def test_delta_with_no_baseline_anywhere(self):
        rels = [release("1.1", ["patch-1.0-to-1.1.zip", "patch-1.0-to-1.1.json"])]
        v = cr.check_mod(manifest(tag="1.1", deltaPatches=True), rels, True)
        self.assertTrue(any("baseline" in e for e in v.errors))

    def test_a_latest_release_without_payload(self):
        rels = [release("2.0", ["notes.txt"]), release("1.0", ["Mod.zip"])]
        v = cr.check_mod(manifest(followLatest=True), rels, True)
        self.assertTrue(any("2.0" in e for e in v.errors))

    def test_a_prerelease_is_not_the_latest(self):
        rels = [release("2.0-rc", ["notes.txt"], prerelease=True), release("1.0", ["Mod.zip"])]
        self.assertTrue(cr.check_mod(manifest(followLatest=True), rels, True).clean)

    def test_a_seed_on_no_release_is_an_error_and_off_target_a_warning(self):
        m = manifest(install={"userDataPayload": "userdata.zip"})
        self.assertTrue(cr.check_mod(m, [release("1.0", ["Mod.zip"])], True).errors)
        v = cr.check_mod(m, [release("1.1", ["Mod.zip", "userdata.zip"]), release("1.0", ["Mod.zip"])], True)
        self.assertEqual(v.errors, [])
        self.assertTrue(v.warnings)

    def test_an_external_host_skips_the_asset_rules(self):
        m = manifest(externalAssetUrlTemplate="https://x/{tag}.zip", externalAssetSha256="a" * 64)
        self.assertTrue(cr.check_mod(m, [release("1.0", [])], True).clean)


class IssueBookkeeping(unittest.TestCase):
    """The refusals are the point: a modder is never pestered by a flaky run or a repeat."""

    def v(self, errors=(), warnings=(), inconclusive=False):
        return {"id": "m", "errors": list(errors), "warnings": list(warnings), "inconclusive": inconclusive}

    def test_THE_ONE_THAT_MATTERS_an_inconclusive_run_touches_nothing(self):
        open_ = {"Release check: m": {"number": 4, "body": "x"}}
        self.assertEqual(ri.plan([self.v(inconclusive=True)], open_), [("skip", "m")])

    def test_a_new_finding_opens_one_issue(self):
        acts = ri.plan([self.v(errors=["tag 1.01 is missing"])], {})
        self.assertEqual(acts[0][0], "create")
        self.assertEqual(acts[0][1], "Release check: m")

    def test_the_same_findings_change_nothing(self):
        body = ri.body_for(self.v(errors=["e"]))
        self.assertEqual(ri.plan([self.v(errors=["e"])], {"Release check: m": {"number": 4, "body": body}}), [])

    def test_changed_findings_edit_the_issue(self):
        acts = ri.plan([self.v(errors=["new"])], {"Release check: m": {"number": 4, "body": "old"}})
        self.assertEqual(acts[0][:2], ("edit", 4))

    def test_a_clean_mod_closes_its_issue_and_opens_none(self):
        self.assertEqual(ri.plan([self.v()], {"Release check: m": {"number": 4, "body": "old"}}), [("close", 4)])
        self.assertEqual(ri.plan([self.v()], {}), [])

    def test_nobody_is_mentioned(self):
        self.assertNotIn(" @x", ri.body_for(self.v(errors=["ask @x"])))


if __name__ == "__main__":
    unittest.main()
