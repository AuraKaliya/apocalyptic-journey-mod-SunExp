using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace AuraCardFrameFxLab
{
    [ExecuteAlways]
    public sealed class AuraCardFrameFxLabController : MonoBehaviour
    {
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int NoiseTexId = Shader.PropertyToID("_NoiseTex");
        private static readonly int FoilTexId = Shader.PropertyToID("_FoilTex");
        private static readonly int EffectModeId = Shader.PropertyToID("_TerriasEffectMode");
        private static readonly int OverlayModeId = Shader.PropertyToID("_TerriasOverlayMode");
        private static readonly int FrameOnlyOverlayId = Shader.PropertyToID("_TerriasFrameOnlyOverlay");
        private static readonly int FoilModeId = Shader.PropertyToID("_TerriasFoilMode");
        private static readonly int HoloColorAId = Shader.PropertyToID("_TerriasHoloColorA");
        private static readonly int HoloColorBId = Shader.PropertyToID("_TerriasHoloColorB");
        private static readonly int HoloColorCId = Shader.PropertyToID("_TerriasHoloColorC");
        private static readonly int FlowSpeedId = Shader.PropertyToID("_TerriasFlowSpeed");
        private static readonly int FlowScaleId = Shader.PropertyToID("_TerriasFlowScale");
        private static readonly int NoiseScaleId = Shader.PropertyToID("_TerriasNoiseScale");
        private static readonly int DistortionId = Shader.PropertyToID("_TerriasDistortion");
        private static readonly int EffectIntensityId = Shader.PropertyToID("_TerriasEffectIntensity");
        private static readonly int QualityScaleId = Shader.PropertyToID("_TerriasQualityScale");
        private static readonly int EdgeGlowId = Shader.PropertyToID("_TerriasEdgeGlow");
        private static readonly int SweepFrequencyId = Shader.PropertyToID("_TerriasSweepFrequency");
        private static readonly int SweepWidthId = Shader.PropertyToID("_TerriasSweepWidth");
        private static readonly int SweepIntensityId = Shader.PropertyToID("_TerriasSweepIntensity");
        private static readonly int PrismScaleId = Shader.PropertyToID("_TerriasPrismScale");
        private static readonly int PrismStrengthId = Shader.PropertyToID("_TerriasPrismStrength");
        private static readonly int FoilGrainId = Shader.PropertyToID("_TerriasFoilGrain");
        private static readonly int MirrorSweepId = Shader.PropertyToID("_TerriasMirrorSweep");
        private static readonly int SwirlStrengthId = Shader.PropertyToID("_TerriasSwirlStrength");
        private static readonly int FoilShardScaleId = Shader.PropertyToID("_TerriasFoilShardScale");
        private static readonly int FoilShardWarpId = Shader.PropertyToID("_TerriasFoilShardWarp");
        private static readonly int FoilGalaxyDensityId = Shader.PropertyToID("_TerriasFoilGalaxyDensity");
        private static readonly int FoilGlintSpeedId = Shader.PropertyToID("_TerriasFoilGlintSpeed");
        private static readonly int FoilTextureStrengthId = Shader.PropertyToID("_TerriasFoilTextureStrength");
        private static readonly int RainbowStrengthId = Shader.PropertyToID("_TerriasRainbowStrength");
        private static readonly int RidgeStrengthId = Shader.PropertyToID("_TerriasRidgeStrength");
        private static readonly int GlareStrengthId = Shader.PropertyToID("_TerriasGlareStrength");
        private static readonly int PointerAutoSpeedId = Shader.PropertyToID("_TerriasPointerAutoSpeed");
        private static readonly int FoilOverlayAlphaId = Shader.PropertyToID("_TerriasFoilOverlayAlpha");
        private static readonly int PointerXId = Shader.PropertyToID("_TerriasPointerX");
        private static readonly int PointerYId = Shader.PropertyToID("_TerriasPointerY");
        private static readonly int EdgeSampleId = Shader.PropertyToID("_TerriasEdgeSample");

        [Header("Scene References")]
        [SerializeField] private Image backgroundImage;
        [SerializeField] private Image baseFrameImage;
        [SerializeField] private Image effectFrameImage;
        [SerializeField] private Material frameEffectMaterial;
        [SerializeField] private Texture2D noiseTexture;
        [SerializeField] private Texture2D foilTexture;

        [Header("Debug UI")]
        [SerializeField] private bool showRuntimePanel = true;
        [SerializeField] private Rect runtimePanelRect = new Rect(18f, 18f, 380f, 660f);

        [Header("Frame Overlay")]
        [SerializeField] private bool overlayMode = true;
        [SerializeField] private bool frameOnlyOverlay;
        [SerializeField, Range(0f, 1f)] private float effectMode;
        [SerializeField, Range(1f, 2f)] private float foilMode = 1f;
        [SerializeField, Range(0f, 1f)] private float qualityScale = 1f;

        [Header("Foil Motion")]
        [SerializeField] private float flowSpeed = 0.55f;
        [SerializeField] private float flowScale = 1.22f;
        [SerializeField] private float noiseScale = 4f;
        [SerializeField, Range(0f, 0.05f)] private float distortion = 0.009f;
        [SerializeField, Range(0f, 2f)] private float effectIntensity = 1.04f;
        [SerializeField, Range(0f, 1f)] private float edgeGlow = 0.28f;
        [SerializeField] private float edgeSample = 2f;

        [Header("Sweep")]
        [SerializeField] private float sweepFrequency = 4.4f;
        [SerializeField, Range(0.01f, 1f)] private float sweepWidth = 0.13f;
        [SerializeField, Range(0f, 2f)] private float sweepIntensity = 1.12f;
        [SerializeField, Range(0f, 2f)] private float mirrorSweep = 0.58f;

        [Header("Prism")]
        [SerializeField] private float prismScale = 13.5f;
        [SerializeField, Range(0f, 1f)] private float prismStrength = 1f;
        [SerializeField, Range(0f, 1f)] private float foilGrain = 0.08f;
        [SerializeField, Range(0f, 1f)] private float swirlStrength = 0.06f;
        [SerializeField] private float foilShardScale = 18f;
        [SerializeField, Range(0f, 1f)] private float foilShardWarp = 0.08f;
        [SerializeField, Range(0f, 1f)] private float foilGalaxyDensity = 0.015f;
        [SerializeField] private float foilGlintSpeed = 1.1f;

        [Header("Pokemon Foil Layer")]
        [SerializeField, Range(0f, 2f)] private float foilTextureStrength = 0.6f;
        [SerializeField, Range(0f, 2f)] private float rainbowStrength = 1.25f;
        [SerializeField, Range(0f, 2f)] private float ridgeStrength = 0.7f;
        [SerializeField, Range(0f, 2f)] private float glareStrength = 0.35f;
        [SerializeField] private float pointerAutoSpeed = 0.78f;
        [SerializeField, Range(0f, 2f)] private float foilOverlayAlpha = 1f;

        [Header("Holo Colors")]
        [SerializeField] private Color holoColorA = new Color32(255, 240, 166, 255);
        [SerializeField] private Color holoColorB = new Color32(166, 242, 255, 255);
        [SerializeField] private Color holoColorC = new Color32(210, 184, 255, 255);

        private Vector2 runtimeScroll;

        private void OnEnable()
        {
            ApplySettings();
        }

        private void OnValidate()
        {
            ApplySettings();
        }

        private void Update()
        {
            if (!Application.isPlaying)
            {
                ApplySettings();
            }
        }

        private void OnGUI()
        {
            if (!Application.isPlaying || !showRuntimePanel)
            {
                return;
            }

            runtimePanelRect = GUILayout.Window(GetInstanceID(), runtimePanelRect, DrawRuntimePanel, "Aura Card Frame FX Lab");
        }

        [ContextMenu("Reset Terrias Defaults")]
        public void ResetTerriasDefaults()
        {
            overlayMode = true;
            frameOnlyOverlay = false;
            effectMode = 0.23f;
            foilMode = 1.603f;
            qualityScale = 1f;
            flowSpeed = -2f;
            flowScale = 1.603f;
            noiseScale = 7.722f;
            distortion = 0.021f;
            effectIntensity = 1.093f;
            edgeGlow = 0.12f;
            sweepFrequency = 3.716f;
            sweepWidth = 0.18f;
            sweepIntensity = 0.892f;
            prismScale = 18.19f;
            prismStrength = 0.759f;
            foilGrain = 0.453f;
            mirrorSweep = 0.846f;
            swirlStrength = 0.206f;
            foilShardScale = 15.367f;
            foilShardWarp = 0.162f;
            foilGalaxyDensity = 0.148f;
            foilGlintSpeed = 1.1f;
            foilTextureStrength = 0.967f;
            rainbowStrength = 1.25f;
            ridgeStrength = 0.523f;
            glareStrength = 1.008f;
            pointerAutoSpeed = 0.646f;
            foilOverlayAlpha = 1.557f;
            edgeSample = 3.218f;
            holoColorA = new Color32(255, 240, 166, 255);
            holoColorB = new Color32(166, 242, 255, 255);
            holoColorC = new Color32(210, 184, 255, 255);
            ApplySettings();
        }

        [ContextMenu("Log Terrias Registry Values")]
        public void LogTerriasRegistryValues()
        {
            var builder = new StringBuilder();
            builder.AppendLine("Terrias foil_holo frame effect values:");
            builder.AppendLine("\"floats\": {");
            AppendFloat(builder, "_TerriasOverlayMode", overlayMode ? 1f : 0f, true);
            AppendFloat(builder, "_TerriasFrameOnlyOverlay", frameOnlyOverlay ? 1f : 0f, true);
            AppendFloat(builder, "_TerriasEffectMode", effectMode, true);
            AppendFloat(builder, "_TerriasFoilMode", foilMode, true);
            AppendFloat(builder, "_TerriasFlowSpeed", flowSpeed, true);
            AppendFloat(builder, "_TerriasFlowScale", flowScale, true);
            AppendFloat(builder, "_TerriasNoiseScale", noiseScale, true);
            AppendFloat(builder, "_TerriasDistortion", distortion, true);
            AppendFloat(builder, "_TerriasEffectIntensity", effectIntensity, true);
            AppendFloat(builder, "_TerriasEdgeGlow", edgeGlow, true);
            AppendFloat(builder, "_TerriasSweepFrequency", sweepFrequency, true);
            AppendFloat(builder, "_TerriasSweepWidth", sweepWidth, true);
            AppendFloat(builder, "_TerriasSweepIntensity", sweepIntensity, true);
            AppendFloat(builder, "_TerriasPrismScale", prismScale, true);
            AppendFloat(builder, "_TerriasPrismStrength", prismStrength, true);
            AppendFloat(builder, "_TerriasFoilGrain", foilGrain, true);
            AppendFloat(builder, "_TerriasMirrorSweep", mirrorSweep, true);
            AppendFloat(builder, "_TerriasSwirlStrength", swirlStrength, true);
            AppendFloat(builder, "_TerriasFoilShardScale", foilShardScale, true);
            AppendFloat(builder, "_TerriasFoilShardWarp", foilShardWarp, true);
            AppendFloat(builder, "_TerriasFoilGalaxyDensity", foilGalaxyDensity, true);
            AppendFloat(builder, "_TerriasFoilGlintSpeed", foilGlintSpeed, true);
            AppendFloat(builder, "_TerriasFoilTextureStrength", foilTextureStrength, true);
            AppendFloat(builder, "_TerriasRainbowStrength", rainbowStrength, true);
            AppendFloat(builder, "_TerriasRidgeStrength", ridgeStrength, true);
            AppendFloat(builder, "_TerriasGlareStrength", glareStrength, true);
            AppendFloat(builder, "_TerriasPointerAutoSpeed", pointerAutoSpeed, true);
            AppendFloat(builder, "_TerriasFoilOverlayAlpha", foilOverlayAlpha, true);
            AppendFloat(builder, "_TerriasEdgeSample", edgeSample, false);
            builder.AppendLine("},");
            builder.AppendLine("\"colors\": {");
            builder.AppendLine("  \"_TerriasHoloColorA\": \"" + ColorToRegistryHex(holoColorA) + "\",");
            builder.AppendLine("  \"_TerriasHoloColorB\": \"" + ColorToRegistryHex(holoColorB) + "\",");
            builder.AppendLine("  \"_TerriasHoloColorC\": \"" + ColorToRegistryHex(holoColorC) + "\"");
            builder.AppendLine("}");
            Debug.Log(builder.ToString());
        }

        private void ApplySettings()
        {
            if (effectFrameImage != null)
            {
                effectFrameImage.material = frameEffectMaterial;
                effectFrameImage.raycastTarget = false;
            }

            if (backgroundImage != null)
            {
                backgroundImage.raycastTarget = false;
            }

            if (baseFrameImage != null)
            {
                baseFrameImage.raycastTarget = false;
            }

            var material = frameEffectMaterial;
            if (material == null)
            {
                return;
            }

            SetTexture(material, MainTexId, effectFrameImage != null && effectFrameImage.sprite != null ? effectFrameImage.sprite.texture : null);
            SetTexture(material, NoiseTexId, noiseTexture);
            SetTexture(material, FoilTexId, foilTexture);
            SetFloat(material, EffectModeId, effectMode);
            SetFloat(material, OverlayModeId, overlayMode ? 1f : 0f);
            SetFloat(material, FrameOnlyOverlayId, frameOnlyOverlay ? 1f : 0f);
            SetFloat(material, FoilModeId, foilMode);
            SetFloat(material, FlowSpeedId, flowSpeed);
            SetFloat(material, FlowScaleId, flowScale);
            SetFloat(material, NoiseScaleId, noiseScale);
            SetFloat(material, DistortionId, distortion);
            SetFloat(material, EffectIntensityId, effectIntensity);
            SetFloat(material, QualityScaleId, qualityScale);
            SetFloat(material, EdgeGlowId, edgeGlow);
            SetFloat(material, SweepFrequencyId, sweepFrequency);
            SetFloat(material, SweepWidthId, sweepWidth);
            SetFloat(material, SweepIntensityId, sweepIntensity);
            SetFloat(material, PrismScaleId, prismScale);
            SetFloat(material, PrismStrengthId, prismStrength);
            SetFloat(material, FoilGrainId, foilGrain);
            SetFloat(material, MirrorSweepId, mirrorSweep);
            SetFloat(material, SwirlStrengthId, swirlStrength);
            SetFloat(material, FoilShardScaleId, foilShardScale);
            SetFloat(material, FoilShardWarpId, foilShardWarp);
            SetFloat(material, FoilGalaxyDensityId, foilGalaxyDensity);
            SetFloat(material, FoilGlintSpeedId, foilGlintSpeed);
            SetFloat(material, FoilTextureStrengthId, foilTextureStrength);
            SetFloat(material, RainbowStrengthId, rainbowStrength);
            SetFloat(material, RidgeStrengthId, ridgeStrength);
            SetFloat(material, GlareStrengthId, glareStrength);
            SetFloat(material, PointerAutoSpeedId, pointerAutoSpeed);
            SetFloat(material, FoilOverlayAlphaId, foilOverlayAlpha);
            SetFloat(material, PointerXId, -1f);
            SetFloat(material, PointerYId, -1f);
            SetFloat(material, EdgeSampleId, edgeSample);
            SetColor(material, HoloColorAId, holoColorA);
            SetColor(material, HoloColorBId, holoColorB);
            SetColor(material, HoloColorCId, holoColorC);
        }

        private void DrawRuntimePanel(int windowId)
        {
            var changed = false;
            runtimeScroll = GUILayout.BeginScrollView(runtimeScroll, GUILayout.Height(Mathf.Max(300f, runtimePanelRect.height - 80f)));
            changed |= Toggle("Overlay Mode", ref overlayMode);
            changed |= Toggle("Frame Only Overlay", ref frameOnlyOverlay);
            changed |= Slider("Effect Mode", ref effectMode, 0f, 1f);
            changed |= Slider("Foil Mode", ref foilMode, 1f, 2f);
            changed |= Slider("Quality", ref qualityScale, 0f, 1f);
            GUILayout.Space(8f);
            changed |= Slider("Intensity", ref effectIntensity, 0f, 2f);
            changed |= Slider("Flow Speed", ref flowSpeed, -2f, 2f);
            changed |= Slider("Flow Scale", ref flowScale, 0f, 4f);
            changed |= Slider("Noise Scale", ref noiseScale, 0f, 12f);
            changed |= Slider("Distortion", ref distortion, 0f, 0.05f);
            changed |= Slider("Edge Glow", ref edgeGlow, 0f, 1f);
            changed |= Slider("Edge Sample", ref edgeSample, 0.5f, 6f);
            GUILayout.Space(8f);
            changed |= Slider("Sweep Freq", ref sweepFrequency, 0f, 12f);
            changed |= Slider("Sweep Width", ref sweepWidth, 0.01f, 1f);
            changed |= Slider("Sweep Power", ref sweepIntensity, 0f, 2f);
            changed |= Slider("Mirror Sweep", ref mirrorSweep, 0f, 2f);
            GUILayout.Space(8f);
            changed |= Slider("Prism Scale", ref prismScale, 1f, 40f);
            changed |= Slider("Prism Power", ref prismStrength, 0f, 1f);
            changed |= Slider("Foil Grain", ref foilGrain, 0f, 1f);
            changed |= Slider("Swirl", ref swirlStrength, 0f, 1f);
            changed |= Slider("Shard Scale", ref foilShardScale, 8f, 60f);
            changed |= Slider("Shard Warp", ref foilShardWarp, 0f, 1f);
            changed |= Slider("Galaxy", ref foilGalaxyDensity, 0f, 1f);
            changed |= Slider("Glint Speed", ref foilGlintSpeed, 0f, 4f);
            GUILayout.Space(8f);
            changed |= Slider("Foil Texture", ref foilTextureStrength, 0f, 2f);
            changed |= Slider("Rainbow", ref rainbowStrength, 0f, 2f);
            changed |= Slider("Ridge", ref ridgeStrength, 0f, 2f);
            changed |= Slider("Glare", ref glareStrength, 0f, 2f);
            changed |= Slider("Pointer Speed", ref pointerAutoSpeed, 0f, 3f);
            changed |= Slider("Overlay Alpha", ref foilOverlayAlpha, 0f, 2f);
            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset Terrias"))
            {
                ResetTerriasDefaults();
                changed = false;
            }

            if (GUILayout.Button("Log Registry"))
            {
                LogTerriasRegistryValues();
            }

            GUILayout.EndHorizontal();

            if (changed)
            {
                ApplySettings();
            }

            GUI.DragWindow(new Rect(0f, 0f, 10000f, 22f));
        }

        private static bool Slider(string label, ref float value, float min, float max)
        {
            var oldValue = value;
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(116f));
            value = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(170f));
            value = Mathf.Clamp(value, min, max);
            GUILayout.Label(value.ToString("0.###", CultureInfo.InvariantCulture), GUILayout.Width(58f));
            GUILayout.EndHorizontal();
            return !Mathf.Approximately(oldValue, value);
        }

        private static bool Toggle(string label, ref bool value)
        {
            var oldValue = value;
            value = GUILayout.Toggle(value, label);
            return oldValue != value;
        }

        private static void SetTexture(Material material, int propertyId, Texture texture)
        {
            if (texture != null && material.HasProperty(propertyId))
            {
                material.SetTexture(propertyId, texture);
            }
        }

        private static void SetFloat(Material material, int propertyId, float value)
        {
            if (material.HasProperty(propertyId))
            {
                material.SetFloat(propertyId, value);
            }
        }

        private static void SetColor(Material material, int propertyId, Color value)
        {
            if (material.HasProperty(propertyId))
            {
                material.SetColor(propertyId, value);
            }
        }

        private static void AppendFloat(StringBuilder builder, string propertyName, float value, bool comma)
        {
            builder.Append("  \"");
            builder.Append(propertyName);
            builder.Append("\": ");
            builder.Append(value.ToString("0.###", CultureInfo.InvariantCulture));
            if (comma)
            {
                builder.Append(',');
            }

            builder.AppendLine();
        }

        private static string ColorToRegistryHex(Color color)
        {
            return "#" + ColorUtility.ToHtmlStringRGBA(color);
        }
    }
}
