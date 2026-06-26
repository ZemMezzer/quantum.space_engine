using System;
using SpaceEngine.Rendering.Content.StellarObjects.Renderers.Galaxies;
using SpaceEngine.Runtime.Content.Entities;
using SpaceEngine.Runtime.Content.StellarObjects.Rendering;
using UnityEngine;

namespace SpaceEngine.Rendering.Content
{
    [Serializable]
    public sealed class GalaxyRendererBinding
    {
        [SerializeField] private StellarEntity entity;
        [SerializeField] private GalaxyRenderer renderer;

        public StellarEntity Entity => entity;
        public GalaxyRenderer Renderer => renderer;
        public bool IsValid => entity != null && renderer != null;
    }
    
    [Serializable]
    public sealed class StellarObjectRendererBinding
    {
        [SerializeField] private StellarEntity entity;
        [SerializeField] private StellarObjectRenderer renderer;

        public StellarEntity Entity => entity;
        public StellarObjectRenderer Renderer => renderer;
        public bool IsValid => entity != null && renderer != null;
    }
}
