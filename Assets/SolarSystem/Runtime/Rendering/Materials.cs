using UnityEngine;

namespace SolarSystem.Rendering
{
    /// <summary>Loads the project's shaders from Resources (which also guarantees they ship in builds).</summary>
    public static class Materials
    {
        public static Shader Planet => Load("Planet");
        public static Shader Sun => Load("Sun");
        public static Shader Glow => Load("Glow");
        public static Shader Ring => Load("Ring");
        public static Shader Ship => Load("Ship");
        public static Shader Line => Load("Line");
        public static Shader Stars => Load("Stars");

        static Shader Load(string name)
        {
            var shader = Resources.Load<Shader>("Shaders/" + name);
            if (shader == null)
            {
                Debug.LogError($"[SolarSystem] Missing shader Resources/Shaders/{name}");
                shader = Shader.Find("Hidden/InternalErrorShader");
            }
            return shader;
        }

        public static Material Create(Shader shader, string name)
        {
            return new Material(shader) { name = name, hideFlags = HideFlags.DontSave };
        }
    }
}
