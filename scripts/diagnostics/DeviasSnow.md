# Devias ground snow

Devias enables a separate snow mesh above the original terrain, opaque inside and
feathered at its perimeter. Its default
height is 24 world units (terrain tiles are 100 units wide). Grounded players and
monsters leave separate, deep alternating sole impressions, with a narrower heel
and wider forefoot. Step spacing, splay, width and pressure vary slightly per actor;
spacing advances with travelled distance and direction follows the movement path.
There are no continuous dragging lanes, and frames between footfalls do not stamp
or dirty the snow mesh. Monster width and stride follow model scale.
These are movement-based impressions, not
bone-synchronized paw animations. Flying/dead actors and discontinuous movement
do not stamp tracks.

Settings under `MuOnline.Environment.DeviasGroundSnow` in `Client.Main/appsettings.json`:

- `Enabled`: defaults to `true`; requires re-entering Devias after changing config.
- `Depth`: defaults to `24`, clamped to 8–40 world units.
- `TrackLifetime`: defaults to `90` seconds, clamped to 5–600 seconds.

Samples are spaced 6.25 world units apart. Meshes use 4×4 terrain-tile patches,
rebuilding only after local deformation or at 0.5-second intervals during recovery.
The original terrain uses full detail while snow is enabled to avoid coarse
triangles intersecting groove floors. Colour and shadow passes share the same mesh.
The `DynamicLighting_Snow` technique shades displaced normals and compacted snow;
the BasicEffect fallback retains the actual geometry when terrain GPU lighting is disabled.
The shader samples the same base/overlay textures and 64-texel-per-tile UVs as
TerrainRenderer. At the perimeter it reproduces the terrain layer alpha and baked
lighting; powder/grain and smoothed lighting gradually increase with snow coverage.
There is no independent white/blue material tint or additional global sun brightness.
Its coverage tapers over 35 units beside excluded tiles and blends into the
underlying material through an irregular, translucent powder edge.

Track data uses at most 128 sparse 400×400-unit chunks (about 6 MiB for sample arrays).
The oldest track chunk is evicted at the limit. Up to 96 GPU mesh patches stay
resident across camera turns; only least-recently-used patches outside the current
view are evicted. At most two background jobs prepare immutable terrain geometry;
only graphics resource creation and current footprint displacement run on the render
thread. Uploading ready patches is limited to two per frame, with a soft
2 ms budget checked between patches. Nearby patches take priority.
Previously unseen snow fills in over several frames instead of building the whole
view synchronously. An individual patch can exceed the soft budget. Tracks persist through camera movement until they expire or
their CPU chunk is evicted; leaving the world clears them. There is no server
synchronization, physics/pathfinding change, disk persistence or snowfall accumulation.

## Verification

Run from the repository root, on Windows:

```powershell
rtk proxy dotnet run --project scripts/diagnostics/DeviasSnowRegression.csproj
rtk proxy dotnet build MuWinDX/MuWinDX.csproj -p:MonoGameFramework=MonoGame.Framework.WindowsDX
rtk proxy dotnet run --project scripts/diagnostics/DeviasSnowVisualRegression.csproj -p:MonoGameFramework=MonoGame.Framework.WindowsDX
```

The GPU fixture loads the built Windows shader, renders actual production snow
geometry into an offscreen target, verifies depth/seams/shadow casting/recovery,
and writes images into `artifacts/devias-snow`. The boxes represent boots/shins;
these images are a controlled graphics test, not screenshots of a logged-in game.
Append `-- C:/Games/MU_Red_1_20_61_Full/Data` to the last command to additionally
validate masks against the installed Devias mapping, terrain height and attributes.

Rebuild content when publishing; cached/prebuilt `DynamicLighting.xnb` files from
before this change do not contain the new snow technique.
