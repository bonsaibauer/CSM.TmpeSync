# CSM.TmpeSync × CSM – Subtree Setup & Workflow

This guide describes how to embed the **CSM** repository as a **git subtree** inside **CSM.TmpeSync**. All files live directly inside your repository (no submodule pointer), while the subtree remains connected to the upstream project so you can **pull updates** and **push contributions back**.

> 💡 Replace branch names as needed (for example `master` or `main`).
> Example URLs:
> - TmpeSync: `https://github.com/bonsaibauer/CSM.TmpeSync.git`
> - CSM: `https://github.com/bonsaibauer/CSM.git`

---

## Prerequisites

- **Git** (2.35 or newer recommended)
- Access via **SSH** *or* **HTTPS + Personal Access Token (PAT)** if you want to push changes back to the CSM repository
- A clean working tree (`git status` must not show uncommitted changes)

---

## Initial setup (local clone)

```bash
# 1) Clone the repository
git clone https://github.com/bonsaibauer/CSM.TmpeSync.git
cd CSM.TmpeSync

# 2) (Only if the legacy submodule still exists – preparation)
git submodule update --init --recursive || true
```

> If the project still contains a CSM submodule (for example under `submodules/CSM`), run the **migration** described below. If the subtree is already in place, jump straight to **Fetching upstream updates**.

---

## Submodule → Subtree (one-time migration)

> Target directory (adjust as needed): `submodules/CSM`

```bash
# 0) Confirm that everything is committed
git status

# 1) Remove the submodule cleanly (if present)
git submodule deinit -f submodules/CSM || true
git rm -f submodules/CSM || true
rm -rf .git/modules/submodules/CSM || true
# Remove .gitmodules if it becomes empty
git ls-files .gitmodules --error-unmatch && git rm -f .gitmodules || true
git commit -m "chore: remove CSM submodule (prepare subtree)"

# 2) Add the upstream remote (points to the CSM repository)
git remote add csm-upstream https://github.com/bonsaibauer/CSM.git

# 3) Add CSM as subtree
#    --prefix = target folder in this repository
#    --squash = compresses the foreign history into a single commit (cleaner main history)
git subtree add --prefix=submodules/CSM csm-upstream BRANCH --squash
# Example: git subtree add --prefix=submodules/CSM csm-upstream master --squash

# 4) Commit and push the changes (TmpeSync repository)
git commit --allow-empty -m "chore: add CSM as subtree under submodules/CSM"
git push
```

---

## Fetching upstream updates (recurring)

```bash
# 1) Fetch the upstream repository
git fetch csm-upstream

# 2) Update the subtree
git subtree pull --prefix=submodules/CSM csm-upstream BRANCH --squash
# Example: git subtree pull --prefix=submodules/CSM csm-upstream master --squash

# 3) Resolve conflicts (if any) inside submodules/CSM, then:
git add submodules/CSM
git commit -m "chore: merge upstream CSM into subtree (squashed)"
git push
```

---

## Sending your changes back to the CSM repository

### Option A – direct push (you have write access to `bonsaibauer/CSM`)
```bash
git subtree push --prefix=submodules/CSM csm-upstream BRANCH
# Example: git subtree push --prefix=submodules/CSM csm-upstream master
```

### Option B – feature branch via fork + pull request

```bash
# 1) Extract only the changes from submodules/CSM
git subtree split --prefix=submodules/CSM -b csm-changes

# 2) Push to your fork (remote name 'myfork' as an example)
git remote add myfork git@github.com:YOURUSER/CSM.git  # or HTTPS URL
git push myfork csm-changes:my-feature-branch

# 3) Open a pull request from 'my-feature-branch' against bonsaibauer/CSM on GitHub
```

---

## Frequently used Git commands & commit examples

**Status & diff**
```bash
git status
git diff          # unstaged
git diff --staged # staged
```

**Commit changes in the TmpeSync repository**
```bash
git add .
git commit -m "fix: short description of the fix"
git push
```

**Commit only subtree changes**
```bash
git add submodules/CSM
git commit -m "feat(CSM): describe the subtree change"
git push
```

**Create or switch branches**
```bash
git checkout -b feature/something
# ...work...
git push -u origin feature/something
```

---

## Tips & pitfalls

- **Branch names:** Replace `BRANCH` with the actual default branch of the CSM repository (often `master` or `main`).
- **`--squash` vs. full history:**
  - `--squash` keeps the main history compact.
  - Without `--squash` you see the complete CSM history in your repository (useful for `git blame`, but larger).
- **Conflicts during pulls:** Resolve them inside `submodules/CSM`, then commit normally.
- **Authentication:**
  - **SSH** avoids managing tokens.
  - For **HTTPS pushes** you need a PAT with the `repo` scope.
- **CI/build scripts:** The path remains **`submodules/CSM`**, so existing references usually do not require changes.

---

## TL;DR (cheat sheet)

```bash
# Configure the remote (one-time)
git remote add csm-upstream https://github.com/bonsaibauer/CSM.git

# Add the subtree (one-time)
git subtree add --prefix=submodules/CSM csm-upstream BRANCH --squash

# Pull upstream updates (regularly)
git fetch csm-upstream
git subtree pull --prefix=submodules/CSM csm-upstream BRANCH --squash

# Push your changes back
git subtree push --prefix=submodules/CSM csm-upstream BRANCH
# or via split + PR:
git subtree split --prefix=submodules/CSM -b csm-changes
git push myfork csm-changes:my-feature-branch
```

---

## Support

Let me know whether you prefer **SSH** or **HTTPS** and which **branch** is correct in `CSM`. I am happy to adjust the commands for your setup.
