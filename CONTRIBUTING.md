# Contributing to ShaderSnap

Thanks for looking. This page covers the practical parts: getting a working setup, the conventions the
code follows, and what a change needs before it can land.

## Setup

The package is a UPM package, so the fastest loop is to point a throwaway Unity project at your checkout.

1. Create a Unity 6000.3 project.
2. Add the checkout to `Packages/manifest.json`:
   ```json
   {
     "dependencies": {
       "com.zhayagt.shadersnap": "file:../../ShaderSnap"
     },
     "testables": [
       "com.zhayagt.shadersnap"
     ]
   }
   ```
   The `testables` entry is required for Unity to compile the test assembly at all; without it the suite
   silently does not exist.
3. Open `Window > ShaderSnap`.

## Running the tests

`Window > General > Test Runner` > **EditMode** > Run All.

The export tests render an offscreen UI Toolkit panel and read the pixels back, so they need a graphics
device and will skip themselves without one. When you change anything that affects layout or drawing,
run the whole suite — the layout tests assert properties over every node in the fixtures, not just a
sample, so a regression usually shows up as a specific node name in the failure message.

## Code conventions

- **Namespaces.** Everything lives under `ShaderSnap.Core`, `ShaderSnap.Editor` or `ShaderSnap.Tests`.
  Nothing goes in the global namespace; the type names here (`GraphModel`, `GraphLayout`, `PortSlot`)
  would collide with other graph tooling.
- **Editor-only.** All three assemblies set `includePlatforms: ["Editor"]`. The tool is an editor tool and
  nothing in it belongs in a player build.
- **Comments explain why.** The code says what it does. A comment earns its place by recording a reason
  that is not visible in the code: a Unity quirk, a measurement, a bug that was fixed, a trade-off that
  was chosen. Do not narrate the next line.
- **Measurements go in the comment.** Several constants here are the result of measuring the alternatives
  (the text-scale default, the dimming alpha, the wire flattening segment count). When you change one,
  re-measure and update the number in the comment, or the comment becomes a lie.
- **No dead code.** No commented-out blocks, no unused fields, no `TODO` markers. If something is not
  reachable, delete it.
- **Naming.** Fields are `camelCase`, private methods `PascalCase` to match the surrounding code, public
  members `PascalCase`. Prefer a descriptive name over a short one.

## Tests

A change to parsing, layout, routing, styling or export needs a test that fails before it and passes
after. The suite follows a few rules worth knowing:

- Test the behaviour, not the constant. A test asserting that a value equals `200f` breaks on any
  intentional retune and catches nothing; a test asserting that a property holds across a range of inputs
  catches real regressions.
- Do not duplicate an assertion that already exists in another file. If you find a duplicate, delete it.
- Do not assert that a function merely does not throw, that a list is non-empty, or that a file exists
  because the test just wrote it. Those pass for the wrong reasons.
- Name tests as a sentence about the property: `Refresh_PicksUpAutoAspectChange`, not `TestRefresh2`.

## Fixtures

`Tests/Fixtures~/` holds the `.shadergraph` assets the suite parses. The `~` suffix keeps Unity from
importing them as real shaders. They are:

| Fixture | Nodes | Edges | Upstream source | What it exercises |
|---|---|---|---|---|
| `UnlitBasic.shadergraph` | 8 | 4 | `com.unity.shadergraph/GraphTemplates/BuiltIn/BuiltIn Unlit Basic.shadergraph` | The small case; the minimum that still has a fan-out |
| `PropertyTypes.shadergraph` | 8 | 7 | authored for this package | One node per property type, for the value and port-colour paths |
| `TerrainSimple.shadergraph` | 38 | 45 | `com.unity.shadergraph/GraphTemplates/Cross Pipeline/Terrain Simple.shadergraph` | The mid-size case: groups, sticky notes, a master stack, long edges |

Two of the three are Unity's own graph templates, copied **unmodified** — verify with `md5sum` against the
package copy. That is deliberate: a real graph exercises the parser and the layout in ways a hand-built
fixture would not, and keeping them byte-identical means a failure can be reproduced against the upstream
file. Do not edit them. If a test needs a variation, add a new fixture rather than modifying these.

`PropertyTypes.shadergraph` is the exception, because no Unity template has one node per property type.

If you add one, add it here with a note in this table, and make sure at least one test parses it — a
fixture nothing reads is dead weight. Fixtures from a package are the cheapest to add: they are already
real, and the parser test's counts can be checked against the graph in the editor.

## Pull requests

Before opening one:

- The full EditMode suite passes.
- The change is exercised by hand, not only by tests. Export a real graph and look at the PNG.
- `README.md` and `Documentation~/` match the change: a new option needs a row in the options table, and a
  new behaviour needs a sentence where the behaviour is described.
- `CHANGELOG.md` has an entry under `Unreleased`.
- No new compiler warnings.

Keep a pull request to one concern. A layout change and a documentation rewrite in the same branch are
two pull requests.

## License

By contributing you agree that your contribution is licensed under the MIT license in `LICENSE.md`.
