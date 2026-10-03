# NPC spawn mod requirements

GVK-Derelicts does not add MES mod-requirement tags (required/excluded mod lists) to its NPC spawn conditions.

## Why this is out of scope

GVK-Derelicts is built only for the GV: Deserts of Kharak server, where the full mod collection is always loaded together. Every modded block used by an NPC prefab is guaranteed to exist, and every spawn already targets the Kharak planet, so a "missing blocks or planets" case cannot happen in production.

Adding mod-requirement tags to every spawn condition would mean keeping a list of Workshop IDs in sync with the collection by hand. That is ongoing maintenance with no benefit on the live server, and a stale list would silently stop NPCs from spawning, which is worse than the problem it guards against.

If GVK-Derelicts is ever reused on another server or loaded without its dependencies, revisit this.

## Prior requests

- #563: "Add mod requirements to NPCs" (originally GVK-Derelicts #16)
