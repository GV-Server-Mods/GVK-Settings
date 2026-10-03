# Triage Labels

The skills speak in terms of five canonical triage roles. This file maps those roles to the actual label strings used in this repo's issue tracker.

| Label in mattpocock/skills | Label in our tracker | Meaning                                  |
| -------------------------- | -------------------- | ---------------------------------------- |
| `needs-triage`             | `needs-triage`       | Maintainer needs to evaluate this issue  |
| `needs-info`               | `needs-info`         | Waiting on reporter for more information |
| `ready-for-agent`          | `ready-for-agent`    | Fully specified, ready for an AFK agent  |
| `ready-for-human`          | `ready-for-human`    | Requires human implementation            |
| `wontfix`                  | `wontfix`            | Will not be actioned                     |

## Category labels

| Label in mattpocock/skills | Label in our tracker | Meaning                                  |
| -------------------------- | -------------------- | ---------------------------------------- |
| `bug`                      | `bug`                | Something is broken                      |
| `enhancement`              | `enhancement`        | New feature or improvement               |

When a skill mentions a role (e.g. "apply the AFK-ready triage label"), use the corresponding label string from this table.

Edit the right-hand column to match whatever vocabulary you actually use.

## Area labels

Every triaged issue also carries exactly **one** `area:*` label, describing the kind of work:

| Label                | Covers                                                               |
| -------------------- | -------------------------------------------------------------------- |
| `area:settings`      | Core GVK-Settings SBC, ModAdjuster, and session component scripts     |
| `area:weapons`       | WeaponCore and GVK-Weapons-Pack                                       |
| `area:npc`           | MES, derelicts, siege NPCs, encounters                                |
| `area:plugin`        | Torch server plugins                                                  |
| `area:server-config` | Torch / dedicated server config with no repo (default `ready-for-human`: needs server access) |
| `area:economy`       | Stores, contracts, scrap, credits                                     |
| `area:mods`          | Any other GVK mod repo                                                |

Do not create per-mod or per-plugin labels. The specific repo goes in the board's **Component** field instead.

## Component field

Triage adds each issue to the "GVK Server" board and sets **Component** to the repo that holds the code (e.g. `GVK-Weapons-Pack`, `GridDefender`, `Modular-Encounters-Systems`). Options are named after the `GV-Server-Mods` repos, plus `Server config (no repo)` and `Third-party mod`. Commands are in `Docs/agents/issue-tracker.md`.

## Season milestone

If an issue must land for an upcoming season, set its milestone (e.g. `Season 11`). Seasons use milestones, never labels.
