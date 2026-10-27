---
title: Audio engineering notes
covers:
  - Content/Data/GVK_Audio.sbc
---

# Audio engineering notes

When producing or modifying audio assets (`.sbc` SoundCategories and SoundRules):
- **Tooling**: Use *Yakitori Audio Converter* for fast conversion to `.xwm`.
  - Sampling Frequency: `44.1 kHz` | Channels: `1 Mono` | Codec: `Unsigned 8-bit`
- **D2 vs. D3 Space Engineers Audio Architecture**:
  - **D2 Sounds**: Non-directional stereo ambient sounds.
  - Start, Loop, and End sound files must all share the exact same `.wav` audio format.

> These notes are general Space Engineers audio know-how, not GVK rules. They belong in a generic skill (se-dev-mod) if the owner wants them shared.
