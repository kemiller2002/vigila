# Vigila known risks

| Risk | Likelihood | Impact | Mitigation | Owner |
|---|---|---|---|---|
| Process overhead exceeds decision value | Medium | High | Measure time and rework; use artifact thresholds | Unassigned |
| “Communication Engineering” is treated as validated before evidence exists | Medium | High | Keep boundary claims provisional and comparative | Unassigned |
| Sensitive communication data enters repository artifacts | Medium | High | Define data classes and use synthetic fixtures until reviewed | Unassigned |
| Documentation becomes detached from implementation | Medium | High | Link decisions to tests and refresh handoffs at milestones | Unassigned |
| Baseline is selected after results are known | Medium | Medium | Register baseline and measures before the first slice | Unassigned |

## Tracked debt (Echelon quality inventory, 2026-10-05)

| Debt | Why it exists | Impact | Next action | Tracking |
|---|---|---|---|---|
| Vigila owns its time types (`Vigila.Semantic.Time`: `Instant`, `WhenValue`, `Clock`). | No shared canonical time capability exists. Chrona is the time-entry/time-tracking *application*, not a time library (Echelon CHR-F1). | Possible divergence once a portfolio time capability exists. No defect today. | Do **not** take a Chrona dependency. When an Echelon decision names an owner for canonical time primitives, offer `Time.fs` as the seed and migrate behind the existing `Clock` port. | WI-0030; Echelon CHR-F1/VIG-F5 |
| Vigila is not packaged or released and cannot be installed independently (no `PackageId`, `PackAsTool` or release workflow). Praxis has no code that consumes `followup.create`. | Distribution contract pending. Praxis-side optional consumer not built. | Consumers cannot install Vigila by version. Follow-ups that Praxis agents discover are not durably tracked. | Adopt the Echelon executable distribution contract (self-contained `vigila` executable, `echelon.release/v1` metadata, smoke-tested artifact). Praxis then adds an optional, capability-discovered `followup.create` consumer. | #22 (distribution), #23 (provider integration), #15 |
| Main ROS validation is red because commits land without work-item attribution (for example `LICENSE` in bc02edd). | Direct pushes to `main` without `./ros work` attribution. | The governance gate is ignored. | Attribute or reconcile via `./ros work reconcile` and restore the PR-only path to `main`. | WI-0027 |
