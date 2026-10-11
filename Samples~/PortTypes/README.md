# Port Types

A URP shader graph that mixes the port types ShaderSnap has to draw differently: `Float` properties
feeding arithmetic, a `Vector 2` UV run, and a `Vector 4` colour, wired through Time, Distance,
Smoothstep and the Fragment stack.

This is an original graph, not the one in the README's hero image — that one comes from Unity's own Shader
Graph samples and cannot be redistributed with the package. Everything the hero image demonstrates is here
too: property nodes showing their values, a colour swatch, and cables routed around the nodes they cross.

1. Open `Window > ShaderSnap` and select `PortTypes.shadergraph` in the Project window. The preview
   appears and the status line reports the node and edge counts.
2. Press **Export PNG** and choose where to save. The default preset is already a readable image, so the
   first export needs no configuration.
3. Then try the three options that change the output most. **Text Scale** is the readability control: the
   canvas width is fixed by the graph's column count, so it makes the labels larger relative to the image
   rather than reflowing anything. **Cable Style** switches between rounded right angles, chamfered
   corners, smooth curves and straight lines. **Resolution Multiplier** supersamples the whole render, so
   2x is genuinely sharper rather than a scaled-up bitmap.
