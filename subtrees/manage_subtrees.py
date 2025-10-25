#!/usr/bin/env python3
# -*- coding: utf-8 -*-

import os
import sys
import subprocess
import argparse
from pathlib import Path
from configparser import ConfigParser

REPOS = [
    {"name": "TMPE", "url": "https://github.com/bonsaibauer/TMPE"},
    {"name": "CSM",  "url": "https://github.com/bonsaibauer/CSM"},
]
BASE_PREFIX = Path("subtrees")

# ---- Optionale Farbausgabe (PowerShell/Terminal) ----
try:
    from colorama import init as colorama_init, Fore, Style
    colorama_init()
    GREEN = Fore.GREEN; YELLOW = Fore.YELLOW; RED = Fore.RED; CYAN = Fore.CYAN; RESET = Style.RESET_ALL
except Exception:
    GREEN = YELLOW = RED = CYAN = RESET = ""

# ----------------- Hilfsfunktionen -----------------

def run(cmd, cwd=None, check=True, capture_output=True, dry_run=False):
    cmd_str = " ".join(cmd)
    if dry_run:
        print(f"{CYAN}[DRY-RUN]{RESET} {cmd_str}")
        return subprocess.CompletedProcess(cmd, 0, "", "")
    print(f"{CYAN}$ {cmd_str}{RESET}")
    result = subprocess.run(
        cmd, cwd=cwd, check=False,
        stdout=subprocess.PIPE if capture_output else None,
        stderr=subprocess.PIPE if capture_output else None,
        text=True
    )
    if check and result.returncode != 0:
        print(f"{RED}Fehler ({result.returncode}):{RESET}\n{(result.stderr or '').strip()}")
        sys.exit(result.returncode)
    if capture_output and result.stdout and result.stdout.strip():
        print(result.stdout.strip())
    return result

def ensure_git_root_and_chdir():
    res = run(["git", "rev-parse", "--show-toplevel"], check=True, capture_output=True)
    root = (res.stdout or "").strip()
    if not root:
        print(f"{RED}Konnte das Git-Root nicht ermitteln.{RESET}")
        sys.exit(1)
    cur = os.getcwd()
    if os.path.normcase(os.path.abspath(cur)) != os.path.normcase(os.path.abspath(root)):
        print(f"{YELLOW}Wechsle ins Repo-Root:{RESET} {root}")
        os.chdir(root)
    return Path(root)

def get_default_branch(repo_url, dry_run=False):
    # HEAD-Symref abfragen
    try:
        res = run(["git", "ls-remote", "--symref", repo_url, "HEAD"], check=False, dry_run=dry_run)
        txt = (res.stdout or "") + (res.stderr or "")
        for line in txt.splitlines():
            line = line.strip()
            if line.startswith("ref:") and "HEAD" in line:
                parts = line.split()
                if len(parts) >= 2 and parts[1].startswith("refs/heads/"):
                    return parts[1].split("refs/heads/")[1]
    except Exception:
        pass
    # Fallbacks prüfen
    for candidate in ("main", "master"):
        try:
            res = run(["git", "ls-remote", repo_url, candidate], check=False, dry_run=dry_run)
            if res.stdout and res.stdout.strip():
                return candidate
        except Exception:
            continue
    return "main"

def subtree_exists(prefix_path: Path):
    return prefix_path.exists() and any(prefix_path.iterdir())

def subtree_add(prefix: Path, url: str, branch: str, squash: bool, dry_run: bool):
    prefix_str = prefix.as_posix()
    args = ["git", "subtree", "add", f"--prefix={prefix_str}", url, branch]
    if squash:
        args.append("--squash")
    print(f"{YELLOW}==> ADD subtree: {prefix_str} ({url}@{branch}){RESET}")
    run(args, check=True, dry_run=dry_run)

def subtree_pull(prefix: Path, url: str, branch: str, squash: bool, dry_run: bool):
    prefix_str = prefix.as_posix()
    args = ["git", "subtree", "pull", f"--prefix={prefix_str}", url, branch]
    if squash:
        args.append("--squash")
    print(f"{YELLOW}==> PULL subtree: {prefix_str} ({url}@{branch}){RESET}")
    run(args, check=True, dry_run=dry_run)

# --------- Submodule (.gitmodules) einlesen & URL auflösen ---------

def parse_gitmodules(dotgitmodules_path: Path):
    """Liest .gitmodules und gibt Liste von {path, url, branch?} zurück."""
    if not dotgitmodules_path.exists():
        return []
    cfg = ConfigParser()
    from io import StringIO
    cfg.read_file(StringIO(dotgitmodules_path.read_text(encoding="utf-8")))
    mods = []
    for section in cfg.sections():
        if not section.lower().startswith("submodule"):
            continue
        path = cfg.get(section, "path", fallback="").strip()
        url = cfg.get(section, "url", fallback="").strip()
        branch = cfg.get(section, "branch", fallback="").strip()
        if path and url:
            mods.append({"path": path, "url": url, "branch": branch})
    return mods

def parse_github_owner_repo(repo_url: str):
    """
    Extrahiert ('github.com', 'owner', 'repo') aus HTTPS oder SSH GitHub-URLs.
    Rückgabe (host, owner, repo) oder (None, None, None).
    """
    url = repo_url.strip()
    host = owner = repo = None
    if url.startswith("git@"):
        # git@github.com:owner/repo(.git)
        try:
            _, right = url.split("@", 1)
            host_part, path_part = right.split(":", 1)
            host = host_part
            parts = path_part.strip("/").split("/")
            if len(parts) >= 2:
                owner = parts[0]
                repo = parts[1]
        except Exception:
            return None, None, None
    else:
        # https://github.com/owner/repo(.git)
        try:
            from urllib.parse import urlparse
            p = urlparse(url)
            host = p.netloc
            parts = p.path.strip("/").split("/")
            if len(parts) >= 2:
                owner = parts[0]
                repo = parts[1]
        except Exception:
            return None, None, None
    if repo and repo.endswith(".git"):
        repo = repo[:-4]
    if host and owner and repo:
        return host, owner, repo
    return None, None, None

def resolve_submodule_url(parent_repo_url: str, sub_url: str) -> str:
    """
    Löst relative Submodule-URLs auf Basis der Eltern-Repo-URL auf (GitHub).
    - '../other'  -> gleicher Owner, Repo 'other'
    - './lib/foo' -> gleicher Owner, Repo 'foo' in 'lib' (Git ignoriert Zwischenpfade, wichtig ist der Repo-Name)
    - 'owner/name' (ohne Host) -> 'https://github.com/owner/name'
    SSH-Formen bleiben SSH; relative werden als HTTPS zu GitHub aufgelöst.
    Unbekanntes Schema -> unverändert zurückgeben.
    """
    u = sub_url.strip()
    if "://" in u or u.startswith("git@"):
        return u  # bereits absolute URL
    host, owner, _ = parse_github_owner_repo(parent_repo_url)
    if not host or host.lower() != "github.com" or not owner:
        return u  # wir lösen nur GitHub-Relative sicher auf

    # '../foo' oder './foo' oder 'foo' oder 'bar/baz'
    parts = [p for p in u.split("/") if p not in ("", ".")]
    if not parts:
        return u
    # wenn nur 'name' → gleicher Owner
    if len(parts) == 1:
        repo = parts[0]
        return f"https://github.com/{owner}/{repo}"
    # wenn 'owner/name'
    if len(parts) == 2 and parts[0] != "..":
        return f"https://github.com/{parts[0]}/{parts[1]}"
    # '../name' → gleicher Owner, Repo 'name'
    if parts[0] == ".." and len(parts) >= 2:
        repo = parts[-1]
        return f"https://github.com/{owner}/{repo}"
    # Fallback: letzter Pfadteil als Repo-Name unter gleichem Owner
    repo = parts[-1]
    return f"https://github.com/{owner}/{repo}"

# ------------- Rekursives Vendorn der Submodule als Subtrees -------------

def vendor_submodules_recursively(root_prefix: Path, parent_repo_url: str, squash: bool, dry_run: bool, visited=None):
    """
    Sucht unter 'root_prefix' nach .gitmodules und bindet JEDES Submodule
    als eigenen Subtree an den entsprechenden relativen Pfad ein (rekursiv).
    """
    if visited is None:
        visited = set()

    gm = root_prefix / ".gitmodules"
    modules = parse_gitmodules(gm)
    if not modules:
        print(f"{GREEN}Keine .gitmodules in {root_prefix.as_posix()} gefunden.{RESET}")
        return

    print(f"{YELLOW}Gefundene Submodule in {root_prefix.as_posix()}:{RESET}")
    for m in modules:
        print(f"  - {m['path']}  ({m['url']})" + (f" [branch={m['branch']}]" if m.get("branch") else ""))

    for m in modules:
        rel_path = m["path"].replace("\\", "/").strip("/")
        sub_path = (root_prefix / rel_path)
        # URL ggf. relativ → absolut zu GitHub auflösen
        sub_url = resolve_submodule_url(parent_repo_url, m["url"])
        # Branch: .gitmodules > Default-Branch
        branch = m.get("branch") or get_default_branch(sub_url, dry_run=dry_run)

        key = (str(sub_path.resolve()), sub_url)
        if key in visited:
            continue
        visited.add(key)

        sub_path.parent.mkdir(parents=True, exist_ok=True)
        if subtree_exists(sub_path):
            subtree_pull(sub_path, sub_url, branch, squash, dry_run)
        else:
            subtree_add(sub_path, sub_url, branch, squash, dry_run)

        # Rekursion in das nun eingebundene Subtree-Verzeichnis
        vendor_submodules_recursively(sub_path, sub_url, squash=squash, dry_run=dry_run, visited=visited)

# ----------------------------- main --------------------------------

def main():
    parser = argparse.ArgumentParser(description="Subtrees hinzufügen/refreshen (inkl. Submodule als Subtrees).")
    parser.add_argument("--no-squash", action="store_true", help="Nicht squashen (komplette Historie behalten).")
    parser.add_argument("--dry-run", action="store_true", help="Nur anzeigen, keine git-Befehle ausführen.")
    args = parser.parse_args()

    squash = not args.no_squash
    dry_run = args.dry_run

    ensure_git_root_and_chdir()
    BASE_PREFIX.mkdir(parents=True, exist_ok=True)

    print(f"{GREEN}Starte Subtree-Update unter '{BASE_PREFIX.as_posix()}' ...{RESET}")
    for repo in REPOS:
        name = repo["name"]
        url  = repo["url"]
        prefix = BASE_PREFIX / name

        branch = get_default_branch(url, dry_run=dry_run)
        print(f"{GREEN}Repository:{RESET} {name}  {CYAN}{url}{RESET}  (Branch: {branch})")

        if subtree_exists(prefix):
            subtree_pull(prefix, url, branch, squash, dry_run)
        else:
            subtree_add(prefix, url, branch, squash, dry_run)

        # WICHTIG: Submodule in diesem Subtree ebenfalls als Subtrees vendorn (rekursiv)
        vendor_submodules_recursively(prefix, url, squash=squash, dry_run=dry_run)

    print(f"{GREEN}Fertig.{RESET}")

if __name__ == "__main__":
    main()
