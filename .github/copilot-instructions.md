# Repository Instructions

- Read `AGENTS.md` before making changes. It is the canonical agent instruction file for this repository.
- `CLAUDE.md` is a symlink to `AGENTS.md`; do not replace it with copied content.
- Use `scripts/cloud-setup.sh` to prepare cloud environments.
- Build and test from the repository root with `bash scripts/cloud-setup.sh --test`.
- Keep changes small and stage only files related to the task.
- Do not add real customer names, internal project codes, or real repository slugs to committed examples, tests, documentation, or commit messages. Use Northwind placeholders where examples are needed.
