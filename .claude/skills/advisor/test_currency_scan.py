# .claude/skills/advisor/test_currency_scan.py
import json, importlib.util, os
spec = importlib.util.spec_from_file_location(
    "cs", os.path.join(os.path.dirname(__file__), "currency-scan.py"))
cs = importlib.util.module_from_spec(spec); spec.loader.exec_module(cs)

DOTNET_OUTDATED = json.dumps({"projects": [{"frameworks": [{"topLevelPackages": [
    {"id": "AutoMapper", "resolvedVersion": "13.0.1", "latestVersion": "15.1.1"}]}]}]})
DOTNET_VULN = json.dumps({"projects": [{"frameworks": [{"topLevelPackages": [
    {"id": "AutoMapper", "resolvedVersion": "13.0.1",
     "vulnerabilities": [{"severity": "High", "advisoryurl": "https://x/GHSA-rvv3"}]}]}]}]})
NPM_AUDIT = json.dumps({"vulnerabilities": {"lodash": {
    "name": "lodash", "severity": "high", "via": [{"title": "Proto pollution", "url": "https://y"}]}}})

def test_parse_dotnet_outdated():
    r = cs.parse_dotnet(DOTNET_OUTDATED, "outdated")
    assert r == [{"ecosystem": "dotnet", "package": "AutoMapper", "current": "13.0.1",
                  "latest": "15.1.1", "kind": "outdated", "severity": "", "detail": ""}]

def test_parse_dotnet_vulnerable():
    r = cs.parse_dotnet(DOTNET_VULN, "vulnerable")
    assert len(r) == 1 and r[0]["kind"] == "vulnerable" and r[0]["severity"] == "High"
    assert "GHSA-rvv3" in r[0]["detail"]

def test_parse_npm_audit():
    r = cs.parse_npm_audit(NPM_AUDIT)
    assert r == [{"ecosystem": "npm", "package": "lodash", "current": "", "latest": "",
                  "kind": "vulnerable", "severity": "high", "detail": "Proto pollution https://y"}]

if __name__ == "__main__":
    test_parse_dotnet_outdated(); test_parse_dotnet_vulnerable(); test_parse_npm_audit()
    print("ALL PASS")
