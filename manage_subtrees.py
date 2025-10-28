#!/usr/bin/env python3
# -*- coding: utf-8 -*-

import os, sys, subprocess, argparse, re
from pathlib import Path
from configparser import ConfigParser

# === Repositories (real) – branch pinned to 'master'+ ===
REPOS = [
    {"name": "TMPE", "url": "https://github.com/CitiesSkylinesMods/TMPE", "branch": "master"},
    {"name": "CSM",  "url": "https://github.com/CitiesSkylinesMultiplayer/CSM", "branch": "master"},
]
BASE_PREFIX = Path("subtrees")
MOD_METADATA_PATH = Path("src/CSM.TmpeSync/Mod/ModMetadata.cs")
DEPENDENCY_TAG_SOURCES = {
    "HarmonyBuildVersion": "https://github.com/CitiesHarmony/CitiesHarmony.git",
    "CsmBuildVersion": "https://github.com/CitiesSkylinesMultiplayer/CSM.git",
    "TmpeBuildVersion": "https://github.com/CitiesSkylinesMods/TMPE.git",
}
VERSION_TAG_PATTERN = re.compile(r"refs/tags/(?:v)?(?P<version>\d+(?:\.\d+){1,3})$")

# === Colored output ===
try:
    from colorama import init as colorama_init, Fore, Style
    colorama_init()
    GREEN = Fore.GREEN; YELLOW = Fore.YELLOW; RED = Fore.RED; CYAN = Fore.CYAN; RESET = Style.RESET_ALL
except Exception:
    GREEN = YELLOW = RED = CYAN = RESET = ""

# === Shell Helpers ===
def run(cmd, cwd=None, check=True, capture_output=True, dry_run=False):
    cmd_str = " ".join(cmd)
    if dry_run:
        print(f"{CYAN}[DRY-RUN]{RESET} {cmd_str}")
        return subprocess.CompletedProcess(cmd, 0, "", "")
    print(f"{CYAN}$ {cmd_str}{RESET}")
    r = subprocess.run(cmd, cwd=cwd, check=False,
                       stdout=subprocess.PIPE if capture_output else None,
                       stderr=subprocess.PIPE if capture_output else None,
                       text=True)
    if check and r.returncode != 0:
        print(f"{RED}Error ({r.returncode}):{RESET}\n{(r.stderr or '').strip()}")
        sys.exit(r.returncode)
    if capture_output and r.stdout and r.stdout.strip():
        print(r.stdout.strip())
    return r

def ensure_git_root_and_chdir():
    r = run(["git", "rev-parse", "--show-toplevel"], check=True, capture_output=True)
    root = (r.stdout or "").strip()
    cur = os.getcwd()
    if os.path.normcase(os.path.abspath(cur)) != os.path.normcase(os.path.abspath(root)):
        print(f"{YELLOW}Switching to repository root:{RESET} {root}")
        os.chdir(root)
    return Path(root)

# === Repository cleanliness ===
def repo_is_clean():
    run(["git", "update-index", "-q", "--refresh"], check=False, capture_output=False)
    wt = subprocess.run(["git", "diff", "--no-ext-diff", "--quiet", "--exit-code"]).returncode == 0
    idx = subprocess.run(["git", "diff", "--cached", "--no-ext-diff", "--quiet", "--exit-code"]).returncode == 0
    r = run(["git", "ls-files", "--others", "--exclude-standard"], check=False)
    untracked_empty = (r.stdout.strip() == "") if r.stdout is not None else True
    return wt and idx and untracked_empty

def commit_repo_if_dirty(message: str, dry_run: bool) -> bool:
    """Commit repository-wide when changes are pending. Returns True if a commit was created."""
    if repo_is_clean():
        print(f"{GREEN}Working tree is clean – no commit needed.{RESET}")
        return False
    print(f"{YELLOW}Working tree is dirty – committing everything…{RESET}")
    run(["git", "add", "-A"], dry_run=dry_run)
    run(["git", "commit", "-m", message], dry_run=dry_run)
    return True

def ensure_clean_for_subtree(step_desc: str, dry_run: bool):
    """Ensure the working tree is clean before running git subtree add/pull.
    Creates a fallback commit if required."""
    commit_repo_if_dirty(f"chore(subtree): prepare {step_desc}", dry_run)

def maybe_auto_stash(enable: bool):
    if not enable:
        if not repo_is_clean():
            print(f"{RED}Working tree is not clean.{RESET} Commit/stash or use --auto-stash.")
            sys.exit(1)
        return (False, "")
    name = "auto-stash-before-subtrees"
    print(f"{YELLOW}Auto-stash enabled.{RESET} Stashing local changes ({name})…")
    run(["git", "stash", "push", "-u", "-m", name], check=True)
    return (True, name)

def maybe_auto_stash_pop(did_stash: bool):
    if did_stash:
        print(f"{YELLOW}Restoring stash…{RESET}")
        subprocess.run(["git", "stash", "pop"], check=False)

# === Version Helpers ===
def normalize_version_token(token: str):
    if not token:
        return None
    value = token.strip()
    if not value:
        return None
    if value[0] in ("v", "V"):
        value = value[1:]
    if re.fullmatch(r"\d+(?:\.\d+){1,3}", value):
        return value
    return None

def parse_version_parts(token: str):
    normalized = normalize_version_token(token)
    if not normalized:
        return None
    parts = [int(p) for p in normalized.split(".")]
    if len(parts) > 4:
        parts = parts[:4]
    return parts

def fetch_latest_version_from_tags(repo_url: str, dry_run: bool):
    if dry_run:
        print(f"{YELLOW}[SKIP]{RESET} Dry run active – skipped version lookup for {repo_url}.")
        return None

    cmd = ["git", "ls-remote", "--tags", repo_url]
    print(f"{CYAN}$ {' '.join(cmd)}{RESET}")
    try:
        result = subprocess.run(cmd, check=False, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    except Exception as exc:
        print(f"{RED}Error running git ls-remote for {repo_url}:{RESET} {exc}")
        return None

    if result.returncode != 0 or not result.stdout:
        msg = (result.stderr or "").strip()
        if msg:
            print(f"{YELLOW}No tags found for {repo_url} or command failed:{RESET} {msg}")
        else:
            print(f"{YELLOW}No tags found for {repo_url} or command failed.{RESET}")
        return None

    best = None
    best_parts = None

    for line in result.stdout.splitlines():
        line = line.strip()
        if not line:
            continue
        try:
            _, ref = line.split()
        except ValueError:
            continue
        if ref.endswith("^{}"):
            ref = ref[:-3]
        match = VERSION_TAG_PATTERN.match(ref)
        if not match:
            continue
        parts = parse_version_parts(match.group("version"))
        if not parts:
            continue
        padded = tuple(parts + [0] * (4 - len(parts)))
        if (best is None) or (padded > best):
            best = padded
            best_parts = parts

    if best_parts:
        print(f"{GREEN}Found version for {repo_url}:{RESET} {'.'.join(str(p) for p in best_parts)}")
    else:
        print(f"{YELLOW}No matching version tags for {repo_url}.{RESET}")

    return best_parts

def gather_dependency_versions(dry_run: bool):
    versions = {}
    for field, repo_url in DEPENDENCY_TAG_SOURCES.items():
        parts = fetch_latest_version_from_tags(repo_url, dry_run=dry_run)
        if parts:
            versions[field] = parts
    return versions

def format_version_ctor(parts):
    padded = list(parts)
    while len(padded) < 4:
        padded.append(0)
    ctor = ", ".join(str(p) for p in padded)
    return f"new Version({ctor})"

def update_mod_metadata(dependency_versions, dry_run: bool):
    if not dependency_versions:
        return
    if not MOD_METADATA_PATH.exists():
        print(f"{YELLOW}ModMetadata.cs not found, skipping update.{RESET}")
        return

    original = MOD_METADATA_PATH.read_text(encoding="utf-8")
    updated = original
    changes = 0

    for field, parts in dependency_versions.items():
        ctor = format_version_ctor(parts)
        pattern = re.compile(rf"(internal\s+static\s+readonly\s+Version\s+{re.escape(field)}\s*=\s*)new\s+Version\([^;]+\);")
        updated, count = pattern.subn(rf"\1{ctor};", updated)
        if count:
            changes += count
        else:
            print(f"{YELLOW}Could not update field {field} in ModMetadata.cs.{RESET}")

    if changes and updated != original:
        if dry_run:
            print(f"{CYAN}[DRY-RUN]{RESET} Updated ModMetadata.cs (not written).")
        else:
            MOD_METADATA_PATH.write_text(updated, encoding="utf-8")
            print(f"{GREEN}Updated ModMetadata.cs ({changes} change(s)).{RESET}")

# === Commit Helper (Prefix + Fallback) ===
def commit_prefix_if_needed(prefix: Path, message: str, dry_run: bool) -> bool:
    px = prefix.as_posix()
    run(["git", "add", "-A", "--", px], dry_run=dry_run)
    diff_rc = subprocess.run(["git", "diff", "--cached", "--quiet", "--", px]).returncode
    if diff_rc == 1:
        print(f"{YELLOW}Committing changes under {px}…{RESET}")
        run(["git", "commit", "-m", message], dry_run=dry_run)
        return True
    else:
        print(f"{GREEN}Nothing to commit under {px}.{RESET}")
        return False

# === Git Basics ===
def get_default_branch(repo_url, dry_run=False):
    try:
        r = run(["git", "ls-remote", "--symref", repo_url, "HEAD"], check=False, dry_run=dry_run)
        txt = (r.stdout or "") + (r.stderr or "")
        for line in txt.splitlines():
            line = line.strip()
            if line.startswith("ref:") and "HEAD" in line:
                parts = line.split()
                if len(parts) >= 2 and parts[1].startswith("refs/heads/"):
                    return parts[1].split("refs/heads/")[1]
    except Exception:
        pass
    for c in ("main", "master"):
        r = run(["git", "ls-remote", repo_url, c], check=False, dry_run=dry_run)
        if r.stdout and r.stdout.strip():
            return c
    return "main"

def subtree_exists(p: Path):
    return p.exists() and any(p.iterdir())

def subtree_add(prefix: Path, url: str, branch: str, squash: bool, dry_run: bool):
    px = prefix.as_posix()
    ensure_clean_for_subtree(f"add {px}", dry_run)
    args = ["git", "subtree", "add", f"--prefix={px}", url, branch]
    if squash: args.append("--squash")
    print(f"{YELLOW}==> ADD subtree: {px} ({url}@{branch}){RESET}")
    run(args, check=True, dry_run=dry_run)

def subtree_pull(prefix: Path, url: str, branch: str, squash: bool, dry_run: bool):
    px = prefix.as_posix()
    ensure_clean_for_subtree(f"pull {px}", dry_run)
    args = ["git", "subtree", "pull", f"--prefix={px}", url, branch]
    if squash: args.append("--squash")
    print(f"{YELLOW}==> PULL subtree: {px} ({url}@{branch}){RESET}")
    run(args, check=True, dry_run=dry_run)

# === Read .gitmodules ===
def parse_gitmodules(p: Path):
    if not p.exists(): return []
    cfg = ConfigParser()
    from io import StringIO
    cfg.read_file(StringIO(p.read_text(encoding="utf-8")))
    out = []
    for s in cfg.sections():
        if not s.lower().startswith("submodule"): continue
        path = cfg.get(s, "path", fallback="").strip()
        url  = cfg.get(s, "url",  fallback="").strip()
        br   = cfg.get(s, "branch", fallback="").strip()
        if path and url: out.append({"path": path, "url": url, "branch": br})
    return out

def parse_github_owner_repo(url: str):
    url = url.strip()
    host = owner = repo = None
    if url.startswith("git@"):
        try:
            _, right = url.split("@", 1)
            host_part, path_part = right.split(":", 1)
            host = host_part
            parts = path_part.strip("/").split("/")
            if len(parts) >= 2:
                owner, repo = parts[0], parts[1]
        except Exception:
            return None, None, None
    else:
        try:
            from urllib.parse import urlparse
            p = urlparse(url)
            host = p.netloc
            parts = p.path.strip("/").split("/")
            if len(parts) >= 2:
                owner, repo = parts[0], parts[1]
        except Exception:
            return None, None, None
    if repo and repo.endswith(".git"): repo = repo[:-4]
    return (host, owner, repo) if (host and owner and repo) else (None, None, None)

def resolve_submodule_url(parent_url: str, sub_url: str) -> str:
    u = sub_url.strip()
    if "://" in u or u.startswith("git@"): return u
    host, owner, _ = parse_github_owner_repo(parent_url)
    if not host or host.lower() != "github.com" or not owner: return u
    parts = [p for p in u.split("/") if p not in ("", ".")]
    if not parts: return u
    if len(parts) == 1:
        return f"https://github.com/{owner}/{parts[0]}"
    if len(parts) == 2 and parts[0] != "..":
        return f"https://github.com/{parts[0]}/{parts[1]}"
    if parts[0] == ".." and len(parts) >= 2:
        return f"https://github.com/{owner}/{parts[-1]}"
    return f"https://github.com/{owner}/{parts[-1]}"

# === Recursively vendor submodules as subtrees ===
def is_empty_dir(p: Path) -> bool:
    return p.exists() and p.is_dir() and not any(p.iterdir())

def vendor_submodules_recursively(root_prefix: Path, parent_repo_url: str, squash: bool, dry_run: bool, visited=None):
    if visited is None: visited = set()
    gm = root_prefix / ".gitmodules"
    mods = parse_gitmodules(gm)
    if not mods:
        print(f"{GREEN}No .gitmodules found in {root_prefix.as_posix()}.{RESET}")
        return

    print(f"{YELLOW}Detected submodules in {root_prefix.as_posix()}:{RESET}")
    for m in mods:
        print(f"  - {m['path']} ({m['url']})" + (f" [branch={m['branch']}]"
              if m.get('branch') else ""))

    for m in mods:
        rel = m["path"].replace("\\", "/").strip("/")
        sub_path = root_prefix / rel
        sub_url  = resolve_submodule_url(parent_repo_url, m["url"])
        branch   = (m.get("branch") or get_default_branch(sub_url, dry_run=dry_run))

        key = (str(sub_path.resolve()), sub_url)
        if key in visited:
            continue
        visited.add(key)

        # Remove empty directories before add to avoid dirty trees
        if sub_path.exists() and is_empty_dir(sub_path):
            print(f"{YELLOW}Empty directory detected, removing before subtree add:{RESET} {sub_path.as_posix()}")
            if not dry_run:
                sub_path.rmdir()
            # Ensure cleanliness immediately afterwards
            ensure_clean_for_subtree(f"prepare {sub_path.as_posix()}", dry_run)

        sub_path.parent.mkdir(parents=True, exist_ok=True)

        if subtree_exists(sub_path):
            subtree_pull(sub_path, sub_url, branch, squash, dry_run)
            changed = commit_prefix_if_needed(sub_path,
                        f"chore(subtree): pull {sub_url} ({branch}) -> {sub_path.as_posix()}",
                        dry_run)
            if not changed:
                commit_repo_if_dirty(f"chore(subtree): finalize pull {sub_url} ({branch})", dry_run)
        else:
            subtree_add(sub_path, sub_url, branch, squash, dry_run)
            changed = commit_prefix_if_needed(sub_path,
                        f"chore(subtree): add {sub_url} ({branch}) -> {sub_path.as_posix()}",
                        dry_run)
            if not changed:
                commit_repo_if_dirty(f"chore(subtree): finalize add {sub_url} ({branch})", dry_run)

        # Recurse into submodules
        vendor_submodules_recursively(sub_path, sub_url, squash=squash, dry_run=dry_run, visited=visited)

# === Main ===
def main():
    ap = argparse.ArgumentParser(description="Add or refresh subtrees (including submodules as subtrees).")
    ap.add_argument("--no-squash", action="store_true", help="Do not squash (keep full history).")
    ap.add_argument("--dry-run", action="store_true", help="Show commands without executing them.")
    ap.add_argument("--auto-stash", action="store_true", help="Automatically stash before running and restore afterwards.")
    args = ap.parse_args()

    squash = not args.no_squash
    dry_run = args.dry_run

    ensure_git_root_and_chdir()
    did_stash = False
    try:
        did_stash, _ = maybe_auto_stash(args.auto_stash)
        BASE_PREFIX.mkdir(parents=True, exist_ok=True)
        print(f"{GREEN}Starting subtree update under '{BASE_PREFIX.as_posix()}'…{RESET}")
        for repo in REPOS:
            name, url = repo["name"], repo["url"]
            branch = repo.get("branch") or get_default_branch(url, dry_run=dry_run)
            prefix = BASE_PREFIX / name

            print(f"{GREEN}Repository:{RESET} {name}  {CYAN}{url}{RESET}  (Branch: {branch})")

            if subtree_exists(prefix):
                subtree_pull(prefix, url, branch, squash, dry_run)
                changed = commit_prefix_if_needed(prefix,
                            f"chore(subtree): pull {url} ({branch}) -> {prefix.as_posix()}",
                            dry_run)
                if not changed:
                    commit_repo_if_dirty(f"chore(subtree): finalize pull {url} ({branch})", dry_run)
            else:
                subtree_add(prefix, url, branch, squash, dry_run)
                changed = commit_prefix_if_needed(prefix,
                            f"chore(subtree): add {url} ({branch}) -> {prefix.as_posix()}",
                            dry_run)
                if not changed:
                    commit_repo_if_dirty(f"chore(subtree): finalize add {url} ({branch})", dry_run)

            # Submodules as subtrees (recursive)
            vendor_submodules_recursively(prefix, url, squash=squash, dry_run=dry_run)

        dependency_versions = gather_dependency_versions(dry_run)
        update_mod_metadata(dependency_versions, dry_run)

        print(f"{GREEN}Done.{RESET}")
    finally:
        maybe_auto_stash_pop(did_stash)

if __name__ == "__main__":
    main()
