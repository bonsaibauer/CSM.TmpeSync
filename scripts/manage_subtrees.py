#!/usr/bin/env python3
# -*- coding: utf-8 -*-

import os, sys, subprocess, argparse, json, re
from pathlib import Path
from configparser import ConfigParser
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen

# === Repos (echte) – automatische Release-Refs ===
REPOS = [
    {"name": "TMPE", "url": "https://github.com/CitiesSkylinesMods/TMPE", "use_latest_release": True},
    {"name": "CSM",  "url": "https://github.com/CitiesSkylinesMultiplayer/CSM", "use_latest_release": True},
    {"name": "CitiesHarmony", "url": "https://github.com/boformer/CitiesHarmony", "use_latest_release": True},
]
SELF_REPO_URL = "https://github.com/bonsaibauer/CSM.TmpeSync"
BASE_PREFIX = Path("subtrees")
MOD_METADATA_PATH = Path("src/CSM.TmpeSync/Mod/ModMetadata.cs")

# === Farbausgabe ===
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
        print(f"{RED}Fehler ({r.returncode}):{RESET}\n{(r.stderr or '').strip()}")
        sys.exit(r.returncode)
    if capture_output and r.stdout and r.stdout.strip():
        print(r.stdout.strip())
    return r

def ensure_git_root_and_chdir():
    r = run(["git", "rev-parse", "--show-toplevel"], check=True, capture_output=True)
    root = (r.stdout or "").strip()
    cur = os.getcwd()
    if os.path.normcase(os.path.abspath(cur)) != os.path.normcase(os.path.abspath(root)):
        print(f"{YELLOW}Wechsle ins Repo-Root:{RESET} {root}")
        os.chdir(root)
    return Path(root)

# === Repo-Cleanliness ===
def repo_is_clean():
    run(["git", "update-index", "-q", "--refresh"], check=False, capture_output=False)
    wt = subprocess.run(["git", "diff", "--no-ext-diff", "--quiet", "--exit-code"]).returncode == 0
    idx = subprocess.run(["git", "diff", "--cached", "--no-ext-diff", "--quiet", "--exit-code"]).returncode == 0
    r = run(["git", "ls-files", "--others", "--exclude-standard"], check=False)
    untracked_empty = (r.stdout.strip() == "") if r.stdout is not None else True
    return wt and idx and untracked_empty

def commit_repo_if_dirty(message: str, dry_run: bool) -> bool:
    """Repo-weit committen, wenn irgendetwas offen ist. True = es wurde committet."""
    if repo_is_clean():
        print(f"{GREEN}Repo ist clean – kein Commit nötig.{RESET}")
        return False
    print(f"{YELLOW}Repo ist dirty – committe ALLES …{RESET}")
    run(["git", "add", "-A"], dry_run=dry_run)
    run(["git", "commit", "-m", message], dry_run=dry_run)
    return True

def ensure_clean_for_subtree(step_desc: str, dry_run: bool):
    """
    Vor einem git subtree add/pull sicherstellen, dass der Working Tree clean ist.
    Macht ggf. einen Fallback-Commit.
    """
    commit_repo_if_dirty(f"chore(subtree): prepare {step_desc}", dry_run)

def maybe_auto_stash(enable: bool):
    if not enable:
        if not repo_is_clean():
            print(f"{RED}Working Tree ist nicht clean.{RESET} Entweder committen/stashen oder nutze --auto-stash.")
            sys.exit(1)
        return (False, "")
    name = "auto-stash-before-subtrees"
    print(f"{YELLOW}Auto-Stash aktiv.{RESET} Stashe lokale Änderungen ({name}) …")
    run(["git", "stash", "push", "-u", "-m", name], check=True)
    return (True, name)

def maybe_auto_stash_pop(did_stash: bool):
    if did_stash:
        print(f"{YELLOW}Stelle Stash wieder her …{RESET}")
        subprocess.run(["git", "stash", "pop"], check=False)

# === Commit Helper (Prefix + Fallback) ===
def commit_prefix_if_needed(prefix: Path, message: str, dry_run: bool) -> bool:
    px = prefix.as_posix()
    run(["git", "add", "-A", "--", px], dry_run=dry_run)
    diff_rc = subprocess.run(["git", "diff", "--cached", "--quiet", "--", px]).returncode
    if diff_rc == 1:
        print(f"{YELLOW}Committe Änderungen unter {px} …{RESET}")
        run(["git", "commit", "-m", message], dry_run=dry_run)
        return True
    else:
        print(f"{GREEN}Nichts zu committen unter {px}.{RESET}")
        return False


def update_mod_metadata_file(release_refs: dict[str, str], dry_run: bool) -> None:
    """Persist the latest dependency release tags for use inside the mod."""
    MOD_METADATA_PATH.parent.mkdir(parents=True, exist_ok=True)

    existing_version = "0.1.0.0"
    current_text = ""
    if MOD_METADATA_PATH.exists():
        try:
            current_text = MOD_METADATA_PATH.read_text(encoding="utf-8")
        except Exception:
            current_text = ""
        m = re.search(r'NewVersion\s*=\s*"([^"]*)"', current_text)
        if m:
            existing_version = m.group(1)

    entries = [
        ("CSM.TmpeSync", "LatestCsmTmpeSyncReleaseTag", "CSM TM:PE Sync"),
        ("TMPE", "LatestTmpeReleaseTag", "Traffic Manager: President Edition"),
        ("CSM", "LatestCsmReleaseTag", "Cities: Skylines Multiplayer"),
        ("CitiesHarmony", "LatestCitiesHarmonyReleaseTag", "Cities Harmony"),
    ]

    def escape(value: str) -> str:
        return value.replace("\\", "\\\\").replace('"', '\\"')

    def extract_existing(const_name: str) -> str:
        if not current_text:
            return ""
        m_local = re.search(rf"{const_name}\\s*=\\s*\"([^\"]*)\"", current_text)
        return m_local.group(1) if m_local else ""

    lines = [
        "// <auto-generated>",
        "// This file is generated by scripts/manage_subtrees.py. Do not edit manually.",
        "// </auto-generated>",
        "",
        "namespace CSM.TmpeSync.Mod",
        "{",
        "    internal static class ModMetadata",
        "    {",
        "        /// <summary>",
        "        /// Current version of the CSM TM:PE Sync mod. Update this value when publishing new builds.",
        "        /// </summary>",
        f'        internal const string NewVersion = "{escape(existing_version)}";',
        "",
    ]

    for name, const_name, description in entries:
        current_value = extract_existing(const_name)
        value = release_refs.get(name, "") or current_value
        lines.extend([
            "        /// <summary>",
            f"        /// Latest release tag for {description}.",
            "        /// </summary>",
            f'        internal const string {const_name} = "{escape(value)}";',
            "",
        ])

    lines.append("    }")
    lines.append("}")
    lines.append("")

    new_content = "\n".join(lines)

    if MOD_METADATA_PATH.exists() and current_text == new_content:
        print(f"{GREEN}ModMetadata.cs ist bereits aktuell.{RESET}")
        return

    if dry_run:
        print(f"{CYAN}[DRY-RUN]{RESET} Aktualisiere {MOD_METADATA_PATH.as_posix()} mit neuen Release-Tags.")
        return

    MOD_METADATA_PATH.write_text(new_content, encoding="utf-8")
    print(f"{GREEN}ModMetadata.cs aktualisiert.{RESET}")

    commit_prefix_if_needed(MOD_METADATA_PATH.parent, "chore: update dependency release tags", dry_run)

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

def get_latest_release_tag(repo_url: str, dry_run: bool = False) -> str:
    host, owner, repo = parse_github_owner_repo(repo_url)
    if not host or host.lower() != "github.com" or not owner or not repo:
        return ""
    api_url = f"https://api.github.com/repos/{owner}/{repo}/releases/latest"
    print(f"{CYAN}Prüfe neuesten Release für {owner}/{repo} …{RESET}")
    req = Request(api_url, headers={"Accept": "application/vnd.github+json"})
    try:
        with urlopen(req) as resp:
            if resp.status != 200:
                return ""
            payload = resp.read().decode("utf-8")
    except HTTPError as err:
        if err.code == 404:
            print(f"{YELLOW}Keine Releases für {owner}/{repo} gefunden.{RESET}")
            return ""
        print(f"{RED}Fehler beim Abruf der Releases ({err.code}).{RESET}")
        return ""
    except URLError:
        print(f"{RED}Netzwerkfehler beim Abruf der Releases.{RESET}")
        return ""
    except Exception as exc:
        print(f"{RED}Unerwarteter Fehler beim Abruf der Releases: {exc}{RESET}")
        return ""
    try:
        data = json.loads(payload)
        tag = data.get("tag_name", "").strip()
        if tag:
            print(f"{GREEN}Neuester Release-Tag: {tag}{RESET}")
        else:
            print(f"{YELLOW}Antwort enthielt keinen tag_name.{RESET}")
        return tag
    except json.JSONDecodeError:
        print(f"{RED}Konnte API-Antwort nicht lesen.{RESET}")
        return ""

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

# === .gitmodules lesen ===
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

# === Rekursiv: Submodule als Subtrees einbinden ===
def is_empty_dir(p: Path) -> bool:
    return p.exists() and p.is_dir() and not any(p.iterdir())

def vendor_submodules_recursively(root_prefix: Path, parent_repo_url: str, squash: bool, dry_run: bool, visited=None):
    if visited is None: visited = set()
    gm = root_prefix / ".gitmodules"
    mods = parse_gitmodules(gm)
    if not mods:
        print(f"{GREEN}Keine .gitmodules in {root_prefix.as_posix()} gefunden.{RESET}")
        return

    print(f"{YELLOW}Gefundene Submodule in {root_prefix.as_posix()}:{RESET}")
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

        # leere Ordner vor ADD entfernen -> das macht den Tree dirty:
        if sub_path.exists() and is_empty_dir(sub_path):
            print(f"{YELLOW}Leerer Ordner vorhanden, entferne vor Subtree-ADD:{RESET} {sub_path.as_posix()}")
            if not dry_run:
                sub_path.rmdir()
            # direkt danach wieder clean machen:
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

        # Rekursion in Submodule
        vendor_submodules_recursively(sub_path, sub_url, squash=squash, dry_run=dry_run, visited=visited)

# === Main ===
def main():
    ap = argparse.ArgumentParser(description="Subtrees hinzufügen/refreshen (inkl. Submodule als Subtrees).")
    ap.add_argument("--no-squash", action="store_true", help="Nicht squashen (volle Historie).")
    ap.add_argument("--dry-run", action="store_true", help="Nur anzeigen, nicht ausführen.")
    ap.add_argument("--auto-stash", action="store_true", help="Vorher automatisch stashen und danach wiederherstellen.")
    args = ap.parse_args()

    squash = not args.no_squash
    dry_run = args.dry_run

    ensure_git_root_and_chdir()
    did_stash = False
    try:
        did_stash, _ = maybe_auto_stash(args.auto_stash)
        BASE_PREFIX.mkdir(parents=True, exist_ok=True)
        print(f"{GREEN}Starte Subtree-Update unter '{BASE_PREFIX.as_posix()}' ...{RESET}")
        release_refs: dict[str, str] = {}
        for repo in REPOS:
            name, url = repo["name"], repo["url"]
            release_tag = ""
            if repo.get("use_latest_release"):
                release_tag = get_latest_release_tag(url, dry_run=dry_run)
            branch = release_tag
            if not branch:
                branch = repo.get("branch") or get_default_branch(url, dry_run=dry_run)
            prefix = BASE_PREFIX / name

            release_refs[name] = release_tag

            print(f"{GREEN}Repository:{RESET} {name}  {CYAN}{url}{RESET}  (Ref: {branch})")

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

            # Submodule als Subtrees (rekursiv)
            vendor_submodules_recursively(prefix, url, squash=squash, dry_run=dry_run)

        if SELF_REPO_URL:
            release_refs["CSM.TmpeSync"] = get_latest_release_tag(SELF_REPO_URL, dry_run=dry_run)

        update_mod_metadata_file(release_refs, dry_run=dry_run)
        print(f"{GREEN}Fertig.{RESET}")
    finally:
        maybe_auto_stash_pop(did_stash)

if __name__ == "__main__":
    main()
