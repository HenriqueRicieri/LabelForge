# LabelForge documentation

Start with [Project status](STATUS.md) for the current implementation and verified
baseline. Read [Roadmap](ROADMAP.md) for the proposed work order and completion criteria.

| Document | Purpose |
| --- | --- |
| [Project status](STATUS.md) | Implemented behavior, verification, Git/release state and limits |
| [Roadmap](ROADMAP.md) | Next work, acceptance criteria and deferred features |
| [Development guide](DEVELOPMENT.md) | Architecture, build, tests, Git workflow and packaging |
| [Changelog](../CHANGELOG.md) | User-visible changes since the last tagged version |
| [Validation record](RELEASE-VALIDATION.md) | Native/package results, checksums and remaining release gates |
| [Application README](../README.md) | Features and quick start |
| [Third-party notices](../THIRD-PARTY-NOTICES.md) | Bundled dependency and font licenses |

## Documentation policy

These documents are versioned and safe to publish. Status snapshots name a source
commit and verification date; counts describe that baseline. Roadmap items stay
planned until evidence closes them.

The local files `PLAN.md`, `IMPROVEMENTS.md`, `REVIEW.md`, `CANVAS-PLAN.md`,
`CANVAS-UI-PLAN.md`, `PLAN-NOTES.md`, and the root `CLAUDE.md` remain gitignored.
They retain implementation history and private development context. Read historical
proposals and counts with their dates. Public docs must not link to those files as
though a clean clone contains them.

When behavior changes, update the README and changelog, then status and roadmap if
their conclusions change. Refresh local records too. Keep customer labels, screenshots
of customer content, machine paths and credentials out of published documentation.
Use synthetic examples for shared evidence.
