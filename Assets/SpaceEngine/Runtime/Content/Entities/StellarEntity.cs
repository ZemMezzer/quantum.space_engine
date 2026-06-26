using UnityEngine;

namespace SpaceEngine.Runtime.Content.Entities
{
    /// <summary>
    /// Authored content identity for one generated celestial kind.
    ///
    /// An entity is not an engine enum and does not imply any behaviour.
    /// It is the stable asset selected by a generator binding and resolved by
    /// presentation bindings. Gameplay, editor tools and UI can query the
    /// generated data by this reference without knowing its concrete class.
    /// </summary>
    [CreateAssetMenu(
        fileName = "Stellar Entity",
        menuName = "Space Engine/Entities/Stellar Entity")]
    public sealed class StellarEntity : ScriptableObject
    {
        [SerializeField] private string displayName = "Unnamed Stellar Entity";
        [SerializeField, TextArea(2, 6)] private string description;
        [SerializeField] private Color debugColor = Color.white;

        public string DisplayName => string.IsNullOrWhiteSpace(displayName)
            ? name
            : displayName;

        public string Description => description ?? string.Empty;

        /// <summary>
        /// Editor-only semantic colour. Runtime generation and rendering never
        /// interpret this value.
        /// </summary>
        public Color DebugColor => debugColor;
    }
}
