# Chick: direct Astra workflow

## Project identity

The project is developed on several machines, each at a different disk location; never rely on an absolute path. The intended Unity project is this Git repository (the current working directory). Do not edit any similarly named copy outside it, such as a Desktop copy. Before Unity MCP mutations, confirm the active Editor's project path matches the working directory and that the open map contains the `Farm Reference Map` hierarchy object.

## Execution

- Use `gpt-6-astra` directly for understanding, implementation, review, and verification. Communicate with the user in Turkish.
- The user has removed the previous coordinator/worker arrangement. Do not spawn workers or delegate tasks to subagents unless the user explicitly requests delegation again.
- Keep the project model defaults in `.codex/config.toml`: Astra is selected and subagents are disabled.

## Working practices

- Preserve unrelated changes and verify the result before reporting completion.
- Use applicable Unity skills and coordinate Editor state changes, scene saves, asset imports, and play-mode transitions directly.
- Preview-only requests do not authorize adding assets to the game. Do not expand the user's requested scope or create unrelated conversations.

## Versioning and commits (AI agents)

Every commit an AI agent makes is a new game version. Do all of the following in that commit:

1. **Pick the version `v0.N`.** All commits share one build counter. Read recent commit subjects (`git log --format=%s -20`), take the highest number from either form: `v0.N` (AI commits) or the teammates' `v0.0.N` (ignore letter suffixes such as `v0.0.12a`). Use that number + 1. Example: after `v0.0.15` and `v0.16`, the next is `v0.17`.
2. **Update the in-game version** shown at the bottom left of the HUD (it reads `Application.version`):
   - Player Settings > Version (`bundleVersion` in `ProjectSettings/ProjectSettings.asset`) = `0.N`. When the Unity Editor is open, set it through the Editor (`PlayerSettings.bundleVersion`) and save, so the Editor does not write the old value back.
   - The placeholder text in `Assets/UI/GameVersion/GameVersionHUD.uxml` = `v0.N`.
3. **Write the commit message in Turkish:** the subject line is exactly `v0.N`, then a blank line, then `- ` bullet lines describing the changes in Turkish. Keep any attribution trailer your tool requires after the bullets.

```
v0.17

- Koyunlar akşam ağıla dönüyor
- Ana menüdeki buton hatası düzeltildi
```
