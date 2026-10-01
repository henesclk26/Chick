# Chick: direct Astra workflow

## Project identity

The intended Unity project is `D:/Yeni klasör/Chick`. Do not edit the similarly named Desktop copy. Before Unity MCP mutations, confirm the active Editor points to this project. The intended map contains the `Farm Reference Map` hierarchy object.

## Execution

- Use `gpt-6-astra` directly for understanding, implementation, review, and verification. Communicate with the user in Turkish.
- The user has removed the previous coordinator/worker arrangement. Do not spawn workers or delegate tasks to subagents unless the user explicitly requests delegation again.
- Keep the project model defaults in `.codex/config.toml`: Astra is selected and subagents are disabled.

## Working practices

- Preserve unrelated changes and verify the result before reporting completion.
- Use applicable Unity skills and coordinate Editor state changes, scene saves, asset imports, and play-mode transitions directly.
- Preview-only requests do not authorize adding assets to the game. Do not expand the user's requested scope or create unrelated conversations.
