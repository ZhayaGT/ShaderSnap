# Layout algorithm

The layout engine turns a `GraphModel` into a `GraphLayout`: a rectangle per node, a polyline per wire,
and a canvas size. It is deterministic — the same graph and the same metrics always produce the same
result — and it never draws a wire across a node.

The work happens in four phases, in this order:

```
rank      assign every node a column
order     sort each column vertically
place     give every item a rectangle, size the gaps, size the canvas
route     turn each edge into a polyline and give each segment a lane
```

## 1. Ranking

Every node gets an integer rank, which becomes its column index.

**Longest path first.** `AssignRanks` walks the predecessor graph and puts each node one past its deepest
predecessor. This is the classic layered-DAG ranking: a node can never sit to the left of something it
depends on, and the number of columns equals the longest dependency chain. Sinks end up in the last
column.

**Then relaxed.** Longest-path ranking has a known weakness: a node with no inputs is pinned to column 0
even when the only node reading it lives near the output. On a Shader Graph that shows up as property
nodes parked at the far left with a long wire trailing across the whole canvas.

`BalanceRanks` therefore revisits each node inside its feasible window — from its earliest legal column up
to `maxEarliest - depth`, where `depth` is the longest path from the node to a sink — and picks the column
that minimises a blend of two costs:

| Term | Weight | Meaning |
|---|---|---|
| Distance from the consumers | `locality` | Prefer the column nearest the nodes that read this one |
| Column load | `1 - locality` | Prefer the column that is currently emptiest |

`locality = 1` hugs the consumers, so a property node lands beside the node that reads it. `locality = 0`
spreads nodes into the lightest column, which evens out column heights at the cost of a wider canvas.
`balance` caps how far a node may move from its longest-path rank; `balance = 0` reproduces the classic
ranking exactly.

**Master stacks are pinned.** A Fragment or Vertex context and its block rows are one visual unit. Every
block is forced to the stack's rank so a block can never drift away from the title bar it belongs to.

**Compaction.** `Compact` renumbers the ranks onto consecutive integers, so a graph that only uses ranks
0, 1 and 7 does not leave four empty columns.

## 2. Ordering

Within a column, the vertical order is chosen by alternating barycenter sweeps: three passes, each
sorting a column by the average position of its neighbours in the previously processed column. Even passes
sweep left to right using predecessors, odd passes right to left using successors. The result is that
wires tend to run roughly horizontally instead of crossing each other inside a column gap.

Ties are broken by node id (`StringComparer.Ordinal`) so the ordering is stable and reproducible.

## 3. Placement

**Long edges get dummy rows.** An edge that spans more than one rank is split into rank-adjacent
segments, and each intermediate column receives a zero-height *dummy* item that claims a row. That row is
reserved empty space, so the wire crosses the column through a gap rather than over whatever node happens
to be there. This is what makes the "no wire crosses a node" property hold for arbitrary graphs rather
than only for adjacent-rank edges.

An edge whose two ends live in the same stack has no column to cross; it is emitted as a single segment
that leaves into the gap beside the stack and comes back.

**Gaps are sized from the lanes they must hold.** For every gap, the engine counts how many wire segments
will cross it. A gap is then

```
max(horizontalGap, segments × wireLaneSpacing + wireLaneMargin)
```

so a gap that carries twelve cables is wider than one that carries two. The left padding behaves the same
way, growing on demand to hold same-unit detours.

**Columns are then laid out** left to right at the computed x positions, and the canvas is sized to fit.

**Bands.** Sticky notes and the port legend are drawn under the graph, not beside it. Their heights are
measured from the text they will hold and reserved as a band at the bottom of the canvas, so a long note
never overlaps the graph.

## 4. Routing

Each chain of items becomes a polyline in canvas space: horizontal runs along the centre line of a node
row, vertical runs inside a column gap.

`AssignLanes` gives every vertical segment a lane within its gap, ordering segments so that ones which
share a gap do not land on top of each other. Lane assignment is a greedy interval placement, sorted
deterministically.

The polyline is then drawn according to the cable style:

| Style | How the corners are drawn |
|---|---|
| `Orthogonal` | Rounded arcs of `wireCornerRadius` |
| `Conduit` | Corners cut at 45°, like electrical conduit |
| `Bezier` | The polyline is smoothed into a curve through the same waypoints |
| `Straight` | A single line from port to port, ignoring the route — the only style allowed to cross a node |

The bezier style deliberately does not use `Painter2D.BezierCurveTo`. On a 99-node graph with 108 curves,
Unity's native curve tessellator (`UIPainter2D.ExecuteSnapshotFromJob`) overflowed the stack and crashed
the editor. The curve is flattened into a polyline instead: visually identical, and bounded.

## Canvas shape

The canvas width is fixed by the topology — one column per rank, each column one node wide, plus the gaps.
Nothing in the layout can make it narrower. Height, on the other hand, is free: the gaps inside a column
can be stretched.

That asymmetry is what `verticalSpread` and the aspect solver exploit. `Auto Aspect` binary-searches the
spread (14 iterations) to land the canvas as close to `Target Aspect` as the spread limit allows. It is
off by default, because the stretch is pure empty space: it moves nodes apart without making anything
larger, so a graph stretched to 16:10 looks *smaller* once fitted to a screen. Turn it on when the frame
shape matters more than how large the graph reads.

## Metrics

All spacing and font sizes come from one place, `LayoutMetrics`, so the renderer and the layout engine can
never disagree about how tall a title bar is.

| Constant | Value | Follows text scale? |
|---|---|---|
| `MinNodeWidth` | 200 | no — the renderer raises it by measuring labels |
| Port row height | 22 | yes |
| Title height | 32 | yes |
| Horizontal gap | 56 | no — grows on demand to hold lanes |
| Vertical gap | 28 | yes, and by `verticalSpread` |
| Padding | 60 | no |
| Title font | 15 | yes |
| Port label font | 12 | yes |
| Watermark font | 12 | yes |
| Port radius | 4 | yes |

`LayoutMetrics.FromFontScale` is the only constructor. Note what does *not* scale: node width, the
horizontal gap and the padding. Growing the text while keeping the column width and the empty space
constant is precisely what raises the text-to-canvas ratio, which is the readability problem the tool
exists to solve. The canvas therefore only grows in height when the text scale rises.
