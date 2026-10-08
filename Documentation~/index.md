# ShaderSnap documentation

ShaderSnap turns a Unity `.shadergraph` asset into a PNG that is meant to be read rather than to be
edited. It parses the asset, computes a layout, and draws the result itself.

| Page | Covers |
|---|---|
| [Architecture](architecture.md) | The assemblies, the types, and how a request flows from a selected asset to a PNG |
| [Layout algorithm](layout-algorithm.md) | Ranking, ordering, lane assignment, the aspect solver, and why each choice was made |
| [Exporter internals](exporter-internals.md) | The offscreen render, the reflection into UI Toolkit, and the fallbacks when it is unavailable |
| [Package layout](package-layout.md) | The repository structure, the `~` folders, `.meta` files, and the assembly definitions |
| [Design notes](design-notes.md) | The decisions behind the tool, including the ones that went against the obvious approach |
| [Troubleshooting](troubleshooting.md) | Symptoms, causes, and what to check |

## The shape of the tool

```
.shadergraph asset
      │
      │  ShaderGraphParser        reads the asset's JSON documents
      ▼
  GraphModel                      nodes, ports, edges, stacks, groups, notes
      │
      │  GraphAutoLayoutEngine    ranks → orders → places → routes
      ▼
  GraphLayout                     a rect per node, a polyline per wire, a canvas size
      │
      │  SnippetCanvasRenderer    draws into a Painter2D-backed VisualElement
      ▼
  offscreen UI Toolkit panel
      │
      │  PNGExportUtility         renders the panel to a RenderTexture, reads it back, encodes
      ▼
     PNG
```

Nothing in that chain depends on the Shader Graph editor window being open, on where the author left the
nodes, or on the current zoom. The same asset and the same preset produce the same image.

## Entry points

| What | Where |
|---|---|
| The editor window | `Window > ShaderSnap` |
| Parse an asset | `ShaderGraphParser.Parse(path)` |
| Lay out a graph | `GraphAutoLayoutEngine.Apply(model, metrics, options)` |
| Render to a PNG | `PNGExportUtility.Export(model, preset, name, path, out error)` |
| The saved settings | `SnippetExportPreset`, a `ScriptableObject` |

## Reading the code

Every assembly is editor-only (`includePlatforms: ["Editor"]`), because the tool is an editor tool and
nothing in it belongs in a player build.

`ShaderSnap.Core` holds everything that does not touch the Unity editor API: the parser, the layout
engine, the drawing, the palette, and the preset. It can be exercised without an editor window open,
which is what the parser and layout tests do.

`ShaderSnap.Editor` holds the window, the file watcher, the exporter, and the path helper.

`ShaderSnap.Tests` holds the EditMode suite. It only compiles when the package is listed under
`testables` in the consuming project's manifest.

### Comments

The code says what it does. Comments record the reasons that are not visible in the code — a Unity quirk,
a measurement, a bug that was fixed, a trade-off that was chosen. Several constants carry the numbers
behind them; if you change one, re-measure and update the comment.
