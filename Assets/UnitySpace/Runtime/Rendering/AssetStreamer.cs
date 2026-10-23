using System;
using System.Collections.Generic;
using Unity.SpaceGod.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.SpaceGod.Rendering
{
    /// <summary>
    /// Loads textures and ship models in the background (U.SPC-T414): never a <c>Resources.Load</c> on the frame that
    /// wants the asset. A view asks with <see cref="Request{T}"/> and draws a stand-in (a tinted sphere, an icon) until
    /// its callback brings the real thing; a system about to be entered, or a gate being flown toward, is warmed
    /// (<see cref="WarmSystem"/>) so its assets are already in memory when it is reached. Assets of a system left behind
    /// are let go of (<see cref="ReleaseExcept"/>) once nothing still drawn uses them.
    /// </summary>
    public static class AssetStreamer
    {
        sealed class Entry
        {
            public ResourceRequest Request;
            public Object Asset;
            public bool Done;
            public List<Action<Object>> Waiting;
        }

        static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>();
        static readonly Dictionary<Color32, Texture2D> Swatches = new Dictionary<Color32, Texture2D>();
        static readonly HashSet<string> Pinned = new HashSet<string>();

        /// <summary>Keeps <paramref name="path"/> in memory for good (an asset every system is drawn with), starting it
        /// loading.</summary>
        public static void Pin(string path)
        {
            if (Pinned.Add(path)) Warm(path);
        }

        /// <summary>How many loads are still on their way.</summary>
        public static int InFlight { get; private set; }

        /// <summary>Whether <paramref name="path"/> (under Resources) is already in memory, starting it loading if not.</summary>
        public static bool Ready(string path) => Get(path).Done;

        /// <summary>The asset at <paramref name="path"/> if it is in memory (null while loading, or if there is none),
        /// starting it loading if it is not.</summary>
        public static T Loaded<T>(string path) where T : Object => Get(path).Asset as T;

        /// <summary>Starts <paramref name="path"/> loading in the background, if it is not already.</summary>
        public static void Warm(string path) => Get(path);

        /// <summary>
        /// Hands the asset at <paramref name="path"/> to <paramref name="ready"/>: at once if it is in memory, otherwise
        /// on the frame it arrives (with null if there is no such asset). A view torn down meanwhile must check it is
        /// still alive in its callback.
        /// </summary>
        public static void Request<T>(string path, Action<T> ready) where T : Object
        {
            var e = Get(path);
            if (e.Done)
            {
                ready?.Invoke(e.Asset as T);
                return;
            }
            if (ready != null) (e.Waiting ??= new List<Action<Object>>()).Add(o => ready(o as T));
        }

        /// <summary>What sort of asset lives at <paramref name="path"/>: a model under Ships/, otherwise a map.</summary>
        static Type TypeAt(string path) => path.StartsWith("Ships/", StringComparison.Ordinal) ? typeof(GameObject) : typeof(Texture2D);

        static Entry Get(string path)
        {
            if (Entries.TryGetValue(path, out var e)) return e;
            e = new Entry();
            Entries[path] = e;
            var request = Resources.LoadAsync(path, TypeAt(path));
            if (request == null)
            {
                e.Done = true;
                return e;
            }
            e.Request = request;
            InFlight++;
            request.completed += _ => Complete(path, e);
            return e;
        }

        static void Complete(string path, Entry e)
        {
            if (e.Done) return;
            InFlight = Math.Max(0, InFlight - 1);
            e.Done = true;
            e.Asset = e.Request != null ? e.Request.asset : null;
            e.Request = null;
            var waiting = e.Waiting;
            e.Waiting = null;
            if (waiting == null) return;
            foreach (var w in waiting)
            {
                try { w(e.Asset); }
                catch (Exception ex) { Debug.LogException(ex); }
            }
        }

        /// <summary>The Resources path of a body's colour map and of its relief map (null where it has none).</summary>
        public static string TexturePath(string name) => name != null ? "Textures/" + name : null;

        /// <summary>The Resources path of a ship model.</summary>
        public static string ShipPath(string model) => "Ships/" + model;

        /// <summary>
        /// Starts everything <paramref name="system"/> is drawn with loading in the background: its bodies' maps and the
        /// models of the ships in it. Called as the view heads for it (a gate flown toward, a trip begun), so it is in
        /// memory by the time it is reached.
        /// </summary>
        public static void WarmSystem(StarSystem system)
        {
            if (system == null) return;
            foreach (var path in SystemPaths(system)) Warm(path);
        }

        /// <summary>Every asset <paramref name="system"/> is drawn with.</summary>
        public static IEnumerable<string> SystemPaths(StarSystem system)
        {
            var catalog = system.Catalog;
            for (int i = 0; i < catalog.Count; i++)
            {
                if (catalog[i].Texture != null) yield return TexturePath(catalog[i].Texture);
                if (catalog[i].NormalMap != null) yield return TexturePath(catalog[i].NormalMap);
            }
            var seen = new HashSet<string>();
            foreach (var ship in system.World.Fleet.Ships)
            {
                if (ship.Destroyed) continue;
                var model = ShipModels.KenneyModelFor(ship);
                if (model != null && seen.Add(model)) yield return ShipPath(model);
            }
        }

        /// <summary>
        /// Lets go of every texture loaded here that is not in <paramref name="keep"/> (the systems still drawn or
        /// warmed): unloaded from memory and loaded again in the background if ever wanted. Models are kept: they are
        /// shared by every system and small.
        /// </summary>
        public static void ReleaseExcept(HashSet<string> keep)
        {
            List<string> drop = null;
            foreach (var kv in Entries)
            {
                if (!kv.Value.Done || keep.Contains(kv.Key) || Pinned.Contains(kv.Key)) continue;
                if (!(kv.Value.Asset is Texture2D)) continue;
                (drop ??= new List<string>()).Add(kv.Key);
            }
            if (drop == null) return;
            foreach (var path in drop)
            {
                var asset = Entries[path].Asset;
                Entries.Remove(path);
                if (asset != null) Resources.UnloadAsset(asset);
            }
        }

        /// <summary>A one-pixel texture of <paramref name="color"/>: the stand-in for a map still on its way.</summary>
        public static Texture2D Swatch(Color color)
        {
            Color32 key = color;
            if (Swatches.TryGetValue(key, out var t) && t != null) return t;
            t = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "Swatch", hideFlags = HideFlags.DontSave };
            t.SetPixel(0, 0, color);
            t.Apply(false, true);
            Swatches[key] = t;
            return t;
        }
    }
}
