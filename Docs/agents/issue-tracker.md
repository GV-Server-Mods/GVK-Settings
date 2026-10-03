# Issue tracker: GitHub

Issues and specs for the **entire GV: Deserts of Kharak server** live as GitHub issues in **`GV-Server-Mods/GVK-Settings`**, the hub tracker. Use the `gh` CLI for all operations.

## Hub tracker rules

- This one repo tracks every GVK concern: core settings, WeaponCore, NPCs/MES, Torch plugins, server config, economy, season checklists, and every other GVK mod repo.
- **Never create issues in other Kharak-only repos** (every `GVK-*` repo and `GVK_ToolCore_Tools`). If one turns up there, move it with `gh issue transfer <n> GV-Server-Mods/GVK-Settings -R GV-Server-Mods/<repo>`.
- **Shared and independent repos keep their own trackers**: the `GV-*` repos, `PhysicsOptimizations`, `GridDefender`, `TorchRemoteCleanupPlugin`, `SpecCores_BeaconLimits`, `se-dev-mes`, and `Modular-Encounters-Systems`. A bug in one of them that is Kharak-specific still goes in the hub, with that repo as its Component.
- **Always pass `-R GV-Server-Mods/GVK-Settings`** to `gh issue` commands, so they hit the hub even when run from another repo's clone.
- Commits and PRs in other repos reference hub issues by full path: `Fixes GV-Server-Mods/GVK-Settings#123`.
- An old issue number from GVK-Derelicts, GVK-Weapons-Pack, GVK-Character, GVK-Suspension, or GVK-Spec-Cores-Addon maps to its hub number in `Docs/agents/issue-migration.md`.
- The code for an issue may live in a different repo. Use the issue's **Component** field (see `Docs/agents/triage-labels.md`) to find it, and work in that repo's clone.

## Conventions

- **Create an issue**: `gh issue create -R GV-Server-Mods/GVK-Settings --title "..." --body "..." --label "area:..."`. Use a heredoc for multi-line bodies. Then add it to the board and set its Component (below).
- **Read an issue**: `gh issue view <number> -R GV-Server-Mods/GVK-Settings --comments`, filtering comments by `jq` and also fetching labels and milestone.
- **List issues**: `gh issue list -R GV-Server-Mods/GVK-Settings --state open --json number,title,body,labels,milestone,comments --jq '[.[] | {number, title, body, labels: [.labels[].name], milestone: .milestone.title, comments: [.comments[].body]}]'` with appropriate `--label`, `--milestone`, and `--state` filters.
- **Comment on an issue**: `gh issue comment <number> -R GV-Server-Mods/GVK-Settings --body "..."`
- **Apply / remove labels**: `gh issue edit <number> -R GV-Server-Mods/GVK-Settings --add-label "..."` / `--remove-label "..."`
- **Set season milestone**: `gh issue edit <number> -R GV-Server-Mods/GVK-Settings --milestone "Season 11"`
- **Close**: `gh issue close <number> -R GV-Server-Mods/GVK-Settings --comment "..."`

## Project board: "GVK Server"

Org project #1 (`gh project ... 1 --owner GV-Server-Mods`), linked to GVK-Settings. Every open hub issue should be on it with its **Component** set.

- Project ID: `PVT_kwDOBcj8o84AAaCa`
- Component field ID: `PVTSSF_lADOBcj8o84AAaCazhkNZjM` (single select)
- **Add issue to board**: `gh project item-add 1 --owner GV-Server-Mods --url <issue-url> --format json --jq .id` → item ID
- **Look up Component option IDs**: `gh project field-list 1 --owner GV-Server-Mods --format json --jq '.fields[] | select(.name=="Component") | .options'`
- **Set Component**: `gh project item-edit --project-id PVT_kwDOBcj8o84AAaCa --id <item-id> --field-id PVTSSF_lADOBcj8o84AAaCazhkNZjM --single-select-option-id <option-id>`
- **New mod/plugin repo**: add it as a Component option in the board's field settings (web UI) and to the table in `Docs/agents/triage-labels.md`.

## Seasons

Each server season is a **milestone** (e.g. `Season 11`) on GVK-Settings. The season's checklist is one parent issue (e.g. #494 "Season 11 Changes") with each task as a **sub-issue**, all in that milestone.

## Pull requests as a triage surface

**PRs as a request surface: no.** _(Set to `yes` if this repo treats external PRs as feature requests; `/triage` reads this flag.)_

When set to `yes`, PRs run through the same labels and states as issues, using the `gh pr` equivalents:

- **Read a PR**: `gh pr view <number> --comments` and `gh pr diff <number>` for the diff.
- **List external PRs for triage**: `gh pr list --state open --json number,title,body,labels,author,authorAssociation,comments` then keep only `authorAssociation` of `CONTRIBUTOR`, `FIRST_TIME_CONTRIBUTOR`, or `NONE` (drop `OWNER`/`MEMBER`/`COLLABORATOR`).
- **Comment / label / close**: `gh pr comment`, `gh pr edit --add-label`/`--remove-label`, `gh pr close`.

GitHub shares one number space across issues and PRs, so a bare `#42` may be either: resolve with `gh pr view 42` and fall back to `gh issue view 42`.

## When a skill says "publish to the issue tracker"

Create a GitHub issue.

## When a skill says "fetch the relevant ticket"

Run `gh issue view <number> --comments`.

## Wayfinding operations

Used by `/wayfinder`. The **map** is a single issue with **child** issues as tickets.

- **Map**: a single issue labelled `wayfinder:map`, holding the Notes / Decisions-so-far / Fog body. `gh issue create --label wayfinder:map`.
- **Child ticket**: an issue linked to the map as a GitHub sub-issue (`gh api` on the sub-issues endpoint). Where sub-issues aren't enabled, add the child to a task list in the map body and put `Part of #<map>` at the top of the child body. Labels: `wayfinder:<type>` (`research`/`prototype`/`grilling`/`task`). Once claimed, the ticket is assigned to the driving dev.
- **Blocking**: GitHub's **native issue dependencies**, the canonical, UI-visible representation. Add an edge with `gh api --method POST repos/<owner>/<repo>/issues/<child>/dependencies/blocked_by -F issue_id=<blocker-db-id>`, where `<blocker-db-id>` is the blocker's numeric **database id** (`gh api repos/<owner>/<repo>/issues/<n> --jq .id`, _not_ the `#number` or `node_id`). GitHub reports `issue_dependencies_summary.blocked_by` (open blockers only, the live gate). Where dependencies aren't available, fall back to a `Blocked by: #<n>, #<n>` line at the top of the child body. A ticket is unblocked when every blocker is closed.
- **Frontier query**: list the map's open children (`gh issue list --state open`, scoped to the map's sub-issues / task list), drop any with an open blocker (`issue_dependencies_summary.blocked_by > 0`, or an open issue in the `Blocked by` line) or an assignee; first in map order wins.
- **Claim**: `gh issue edit <n> --add-assignee @me`, the session's first write.
- **Resolve**: `gh issue comment <n> --body "<answer>"`, then `gh issue close <n>`, then append a context pointer (gist + link) to the map's Decisions-so-far.
