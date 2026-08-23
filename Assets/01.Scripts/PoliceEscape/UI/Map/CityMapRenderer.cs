using ConfusedGameDev.FiniteRunner.PoliceEscape.City;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.UI
{
    /// <summary>
    /// Paints the city schematic into a Texture2D — <b>one texel per city
    /// cell</b> — which a RawImage then scales up by the zoom.
    ///
    /// That one-texel-per-cell choice is the whole design. A view covering a
    /// few chunks is tens of thousands of cells (37x37 cells per chunk at the
    /// live settings), so a UI Image per cell is out of the question, and a
    /// per-screen-pixel repaint would be a megapixel of C# work every time the
    /// view moved. At cell resolution the texture is tiny, a repaint is cheap,
    /// and because the filter mode is Point, zooming stays crisp and blocky
    /// instead of blurring — the streets read as streets at every zoom.
    /// Building sprites/textures at runtime with SetPixels32 is the same idiom
    /// Minimap and Speedometer already use.
    ///
    /// Zoom does NOT repaint: it only rescales the RawImage. Only a change to
    /// the covered cell window (pan, or newly generated chunks) repaints.
    /// </summary>
    public class CityMapRenderer
    {
        readonly CityMapSettings settings;

        Texture2D texture;
        Color32[] pixels;
        RectInt window;      // covered cell window, in global cells
        bool hasWindow;

        public Texture2D Texture => texture;

        /// <summary>Cell window currently painted into the texture.</summary>
        public RectInt Window => window;

        public CityMapRenderer(CityMapSettings settings)
        {
            this.settings = settings;
        }

        /// <summary>
        /// Repaint for this cell window. Reallocates only when the window's
        /// size changes — panning at a fixed zoom reuses the same buffers.
        /// </summary>
        public void Paint(CityMapModel model, RectInt cellWindow, MapRoute route, Vector2Int? markerCell)
        {
            EnsureTexture(cellWindow);

            Color32 background = settings.backgroundColor;
            Color32 block = settings.blockColor;
            Color32 road = settings.roadColor;
            Color32 arterial = settings.arterialColor;
            Color32 reserved = settings.reservedColor;

            for (int y = 0; y < cellWindow.height; y++)
            {
                int rowStart = y * cellWindow.width;
                int cellY = cellWindow.yMin + y;
                for (int x = 0; x < cellWindow.width; x++)
                {
                    var cell = new Vector2Int(cellWindow.xMin + x, cellY);
                    Color32 colour;
                    if (!model.TryGetCell(cell, out ChunkData.CellKind kind, out _))
                    {
                        colour = background;           // chunk not generated yet
                    }
                    else
                    {
                        colour = kind switch
                        {
                            ChunkData.CellKind.Arterial => arterial,
                            ChunkData.CellKind.Connector => road,
                            ChunkData.CellKind.Reserved => reserved,
                            _ => block,
                        };
                    }
                    pixels[rowStart + x] = colour;
                }
            }

            // Route and marker are painted on top, in cell space, so they line
            // up with the streets exactly and scale with zoom for free.
            if (route != null && route.HasRoute)
            {
                Color32 routeColour = settings.routeColor;
                foreach (Vector2Int cell in route.Cells)
                    Plot(cell, cellWindow, routeColour);
            }
            if (markerCell.HasValue) Plot(markerCell.Value, cellWindow, settings.markerColor);

            texture.SetPixels32(pixels);
            texture.Apply(false);
            window = cellWindow;
            hasWindow = true;
        }

        void Plot(Vector2Int cell, RectInt cellWindow, Color32 colour)
        {
            int x = cell.x - cellWindow.xMin;
            int y = cell.y - cellWindow.yMin;
            if (x < 0 || y < 0 || x >= cellWindow.width || y >= cellWindow.height) return;
            pixels[y * cellWindow.width + x] = colour;
        }

        /// <summary>True when the painted window no longer covers what we want to show.</summary>
        public bool NeedsRepaint(RectInt wanted) => !hasWindow || !window.Equals(wanted);

        void EnsureTexture(RectInt cellWindow)
        {
            int w = Mathf.Max(1, cellWindow.width);
            int h = Mathf.Max(1, cellWindow.height);
            if (texture != null && texture.width == w && texture.height == h) return;

            Release();
            texture = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = "CityMapSchematic",
                // Point, so zooming in shows crisp cells rather than mush.
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            pixels = new Color32[w * h];
        }

        public void Release()
        {
            if (texture == null) return;
            Object.Destroy(texture);
            texture = null;
            pixels = null;
            hasWindow = false;
        }
    }
}
