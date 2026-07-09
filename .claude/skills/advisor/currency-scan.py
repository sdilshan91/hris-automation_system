#!/usr/bin/env python3
"""Deterministic dependency-currency scanner for the /advisor skill.
Runs dotnet + npm currency/vuln checks, normalizes to one record shape, emits JSON.
Report-only: shells out to read-only listing commands; writes nothing. Fails open
(a missing tool becomes a 'gaps' entry, never an error)."""
import sys, os, json, subprocess, shutil

def parse_dotnet(json_text, kind):
    out = []
    try:
        data = json.loads(json_text or "{}")
    except ValueError:
        return out
    for proj in data.get("projects", []):
        for fw in proj.get("frameworks", []) or []:
            for pkg in fw.get("topLevelPackages", []) or []:
                rec = {"ecosystem": "dotnet", "package": pkg.get("id", ""),
                       "current": pkg.get("resolvedVersion", ""),
                       "latest": pkg.get("latestVersion", ""),
                       "kind": kind, "severity": "", "detail": ""}
                vulns = pkg.get("vulnerabilities") or []
                if vulns:
                    rec["severity"] = vulns[0].get("severity", "")
                    rec["detail"] = vulns[0].get("advisoryurl", "")
                if pkg.get("deprecationReasons"):
                    rec["detail"] = ",".join(pkg["deprecationReasons"])
                out.append(rec)
    return out

def parse_npm_outdated(json_text):
    out = []
    try:
        data = json.loads(json_text or "{}")
    except ValueError:
        return out
    for name, info in (data or {}).items():
        out.append({"ecosystem": "npm", "package": name,
                    "current": info.get("current", ""), "latest": info.get("latest", ""),
                    "kind": "outdated", "severity": "", "detail": ""})
    return out

def parse_npm_audit(json_text):
    out = []
    try:
        data = json.loads(json_text or "{}")
    except ValueError:
        return out
    for name, info in (data.get("vulnerabilities") or {}).items():
        via = info.get("via") or []
        first = next((v for v in via if isinstance(v, dict)), {})
        detail = (first.get("title", "") + " " + first.get("url", "")).strip()
        out.append({"ecosystem": "npm", "package": info.get("name", name),
                    "current": "", "latest": "", "kind": "vulnerable",
                    "severity": info.get("severity", ""), "detail": detail})
    return out

def _run(cmd, cwd):
    try:
        exe = shutil.which(cmd[0]) or cmd[0]
        cmd = [exe, *cmd[1:]]
        p = subprocess.run(cmd, cwd=cwd, capture_output=True, text=True, timeout=180)
        return p.stdout
    except Exception:
        return None

def main():
    root = sys.argv[1] if len(sys.argv) > 1 else "."
    be = os.path.join(root, "src", "backend")
    fe = os.path.join(root, "src", "frontend")
    dotnet, npm, gaps, ran = [], [], [], {}
    for kind, flag in (("outdated", "--outdated"), ("vulnerable", "--vulnerable"),
                       ("deprecated", "--deprecated")):
        out = _run(["dotnet", "list", "package", flag, "--format", "json"], be)
        ran[f"dotnet-{kind}"] = out is not None
        if out is None:
            gaps.append(f"dotnet list package {flag} unavailable (SDK <8 or dotnet missing)")
        else:
            dotnet += parse_dotnet(out, kind)
    o = _run(["npm", "outdated", "--json"], fe); ran["npm-outdated"] = o is not None
    npm += parse_npm_outdated(o) if o else []
    a = _run(["npm", "audit", "--json"], fe); ran["npm-audit"] = a is not None
    npm += parse_npm_audit(a) if a else []
    if o is None and a is None:
        gaps.append("npm outdated/audit unavailable (npm missing or no node_modules)")
    print(json.dumps({"dotnet": dotnet, "npm": npm, "tools_run": ran, "gaps": gaps}, indent=2))

if __name__ == "__main__":
    main()
