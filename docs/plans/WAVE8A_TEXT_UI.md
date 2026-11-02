# Wave 8a: text and UI (Fabric side)

Status: built and tested headless on branch `parity/wave-8a` (not merged). The engine side is in the engine repository's `docs/plans/GODOT_PARITY.md` (wave 8, "Status (2026-11-02)"), `docs/ABI.md` ("Transform, draw state, meshes and spacing", "Text in the world", "Typed text, key repeat and the clipboard", "WOFF fonts") and `docs/GAME_UI_TEXT.md`. Items here: F56 radial TextureProgressBar, F59 CanvasLayer and z_index, P40 and P41 as the GUI uses them, F58 the CanvasItem draw API, E6 and F47 Label3D, P38 font spacing, E14 and E16 as the Fabric controls reach them, F48 translation, F49 TextLine and TextParagraph, F50 CodeEdit, F52 foldable and menu controls, F55 the picker, F57 layout leftovers and P37 WOFF in the converter. Not built: F51, F53, F54.

The plan's rule held: the engine gained neutral capabilities only (a transform stack, per-draw state, a mesh export, spacing, world text, a clipboard seam, a WOFF decode); every Godot mapping below is this repository's.

## Where the numbers live

| What | Where |
|---|---|
| Godot's sRGB colour to the linear radiance the scene shades in | `Label3D.PackLinear` |
| The label's matrix: the node's basis columns through `FabricSpace.ToAverDirection`, times 100 (metres to centimetres) | `Label3D.BuildDescriptor` |
| Fixed size: pixels per plane unit = half the viewport height (**inferred** from Godot's shader, not measured) | `Label3D.FixedSizePixelsPerUnit` |
| Shapes to triangles, the radial wedge | `CanvasTess` (`CanvasDraw.cs`) |
| Which draws take which sampler, address and blend | `CanvasItem.ResolvedFilter`, `ResolvedAddress`, `ResolvedBlend`, `CanvasItemMaterial.ToEngineBlend` |
| The draw order | `Gui.DrawPasses` |
| Plural forms by language | `Translation.PluralForm` |

## What each item became

### Label3D (E6, F47)

A `Label3D` hands the engine a descriptor every frame it is visible (`InternalProcess` -> `Submit` -> `aver_text3d_draw`); the engine lays the string out, builds glyph quads in a plane and draws them in the scene's transparent pass with a plain raster shader. The converter keeps the node and rates its properties:

| Godot | Result | Rating |
|---|---|---|
| `text`, `font`, `font_size`, `outline_size`, `modulate`, `outline_modulate`, `pixel_size`, `offset`, `width`, `line_spacing`, `uppercase`, `horizontal_alignment`, `vertical_alignment`, `no_depth_test`, `double_sided` | the descriptor's matching field | converted |
| `billboard` | enabled and particles: face the camera; fixed Y: turn about the label's up axis | approximated (particles draws as enabled) |
| `autowrap_mode` | every mode wraps at words | approximated |
| `fixed_size` | constant on-screen size, `viewport height / 2` pixels per plane unit | approximated (inferred scale) |
| `shaded` | drawn unlit | approximated |
| `alpha_cut` | discard: drop pixels under `alpha_scissor_threshold`; opaque prepass and hash: ordinary blended text | approximated |
| `render_priority`, `outline_render_priority`, `texture_filter`, `alpha_hash_scale`, `language`, `text_direction` | kept on the node | unsupported |

`Label3D.Font` was already a `Font` (a wave 5 test pins it); it is now what the label draws and measures with. `GetTextSize()` measures in metres through the engine, or with the controls' fallback widths without one. Labels are sorted back to front among themselves; they are not sorted with other transparent draws (the plan's opt-in). They are not drawn in ray-driven mode.

### Canvas drawing (F58, F56, F59, P40, P41)

- `DrawLine` (diagonals), `DrawPolyline`, `DrawPolylineColors`, `DrawMultiline`, `DrawMultilineColors`, `DrawArc`, `DrawCircle` (filled or a ring), `DrawPolygon`, `DrawColoredPolygon`, `DrawPrimitive` and `DrawSetTransform` / `DrawSetTransformMatrix` record triangles and a transform. A polygon is ear-clipped (`Geometry2D.TriangulatePolygon`); a polyline is a quad per segment with a bevel on the outside of each joint; a circle's point count follows its radius (12 to 256). No antialiasing. A shape is capped at 16384 vertices.
- `TextureProgressBar` radial modes are a textured fan of the wedge (`CanvasTess.RadialFill`); `radial_initial_angle`, `radial_fill_degrees` and `radial_center_offset` are honoured. The linear modes draw as before.
- A control whose transform is not axis aligned (rotation, shear, or a rotated `CanvasLayer`) is drawn through `aver_ui_push_transform` with its scale moved into the rectangle sizes and the font size, so text is rasterised at the size it appears at. An axis-aligned item takes the old path.
- `CanvasItem.TextureFilter`, `TextureRepeat` and `Material` (a `CanvasItemMaterial`) resolve through the parents and set the engine's sampler and blend before the item's commands; a tiled `draw_texture_rect` takes the repeat address for that draw only.
- `Gui.DrawPasses` walks the tree once per distinct (layer, z) pair when there is more than one, drawing only the items that belong to the pair while still pushing every clip on the way down; one pair (nearly every UI) is one unfiltered walk, as before. A hidden `CanvasLayer` now draws nothing (it drew its children before).

### Fonts, text and translation (P38, P37, F48, F49)

- `FontVariation`'s `spacing_glyph` and `spacing_space` reach the engine's measure and draw calls; `spacing_top` + `spacing_bottom` lengthen the line. A plain font draws and measures through the old calls.
- `.woff` fonts are copied and named as a family (the engine rebuilds the sfnt when it loads them); `.woff2` is rated unsupported with the reason and not copied.
- `Translation`, `TranslationServer` (locale standardisation, fallback, exact then language then fallback lookup, plural forms by language), `PoFile.Parse` (contexts, plurals, escapes, continuation lines, the header's language) and `Object.Tr` / `TrN`. A message no added translation has goes to the engine's `Loc` (docs/LOCALIZATION.md), then comes back as it is.
- `TextLine` and `TextParagraph` over the controls' own line breaking and measuring: sizes, line ranges, alignment, overrun trimming, hit testing and drawing onto a `CanvasItem`. The RID-based `TextServer.shaped_text_*` calls are not built.

### Controls (F50, F52, F55, F57)

- `CodeEdit` and `CodeHighlighter` (keywords, member keywords, symbols, numbers, functions, delimited regions including multi-line ones); `TextEdit` draws a highlighter's colour runs. `CodeEdit` has string and comment delimiters (scanned together), indentation-based folding state, indent / unindent / duplicate / move / delete / comment-toggle on the selected lines. Not drawn: gutters, the minimap, the completion popup, brace completion.
- `ReferenceRect`, `FoldableContainer` with `FoldableGroup` (one open at a time, `allow_folding_all`), `MenuBar` over its `PopupMenu` children (titles, hidden and disabled menus, hover switching).
- `ColorPicker`: the HSV wheel shape (a hue ring with the saturation / value square inside), `Hex`, 32 recent presets. The other wheel shapes draw as the HSV one; the eyedropper is not built.
- `Control.PivotOffsetRatio`, an inherited `IsLayoutRtl()`, the `accessibility_*` properties and `ShortcutContext` are kept.

### The clipboard (E16)

`UiClipboard` (LineEdit, TextEdit and RichTextLabel copy and paste) is the operating system's clipboard when the host lends it (`aver_ui_set_clipboard_host`), the in-process buffer otherwise.

## The 32-demo reconversion against wave 6

`AverAssetC fabric --dry-run` over GodotCorpus/demo/3d (read through the converter only), counts of unsupported / approximated rows, wave 6 then wave 8a:

| Demo | Unsupported | Approximated |
|---|---|---|
| labels_and_texts | 222 -> 47 | 47 -> 72 |
| sprites | 168 -> 63 | 110 -> 126 |
| platformer | 41 -> 11 | 178 -> 178 |
| tonemap_color_correction | 26 -> 4 | 36 -> 36 |
| sky_shaders | 24 -> 4 | 10 -> 14 |

The other 27 demos are unchanged, and no demo gained an unsupported row or a script compile error (`error CS` counts equal wave 6 in all 32).

## Tests

`Wave8Tests` in `Aver.Fabric.Tests` (shapes, wedges, draw API, radial bar, state resolution, spacing, the Label3D descriptor and submission, translation and `.po`, TextLine / TextParagraph, CodeEdit, the controls, the picker, layout; with the engine's `Aver.UI.Abi` beside the tests also the draw order, rotated controls through the transform, the world text ABI and the clipboard seam against a fake host) and `FabricWave8Test` (the converter on `tests/fabric/data/Wave8Game`: the Label3D ratings, a WOFF converted, a WOFF2 unsupported). `FabricGeometryTest` follows the Label3D ratings. The Fabric suite is 39 of 39 (38 before); the managed suite is 6589 checks.

## Unverified, and the headed checks

Nothing here was run with a GPU or the editor. The picture of: a Label3D (billboard, fixed size and its inferred scale, the outline, depth testing against the scene, a label next to a translucent mesh), a rotated Control and a rotated CanvasLayer, a radial TextureProgressBar, a diagonal line and a polygon, `texture_filter` nearest on pixel art, a CanvasItemMaterial add / mul, z_index and layer order, the colour wheel, the FoldableContainer and MenuBar, copy and paste to and from another program, typing with a non-US layout. Run the `labels_and_texts` and `truck_town` demos.
