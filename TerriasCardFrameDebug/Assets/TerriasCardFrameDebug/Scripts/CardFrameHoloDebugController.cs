using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Terrias.CardFrameDebug
{
    [ExecuteAlways]
    public sealed class CardFrameHoloDebugController : MonoBehaviour
    {
        private static readonly int MainTex = Shader.PropertyToID("_MainTex");
        private static readonly int NoiseTex = Shader.PropertyToID("_NoiseTex");
        private static readonly int HoloColorA = Shader.PropertyToID("_TerriasHoloColorA");
        private static readonly int HoloColorB = Shader.PropertyToID("_TerriasHoloColorB");
        private static readonly int HoloColorC = Shader.PropertyToID("_TerriasHoloColorC");
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
        private static readonly int EdgeSampleId = Shader.PropertyToID("_TerriasEdgeSample");

        [Header("Scene References")]
        public Image cardBackground;
        public Image cardFrame;
        public RectTransform cardRoot;
        public Material frameMaterial;
        public Texture2D flowNoise;

        [Header("Preview")]
        public bool showRuntimeControls = true;
        public bool animate = true;
        [Range(0.5f, 2.0f)] public float previewScale = 1.0f;

        [Header("Terrias/CardFrameHoloFlow")]
        public Color holoColorA = new Color(1.0f, 0.78f, 0.32f, 1.0f);
        public Color holoColorB = new Color(0.42f, 0.92f, 1.0f, 1.0f);
        public Color holoColorC = new Color(1.0f, 0.42f, 0.86f, 1.0f);
        [Range(0.0f, 2.0f)] public float flowSpeed = 0.36f;
        [Range(0.1f, 6.0f)] public float flowScale = 1.65f;
        [Range(0.1f, 12.0f)] public float noiseScale = 4.8f;
        [Range(0.0f, 0.08f)] public float distortion = 0.018f;
        [Range(0.0f, 2.0f)] public float effectIntensity = 0.72f;
        [Range(0.0f, 1.0f)] public float qualityScale = 1.0f;
        [Range(0.0f, 1.0f)] public float edgeGlow = 0.22f;
        [Range(0.1f, 16.0f)] public float sweepFrequency = 5.6f;
        [Range(0.01f, 1.0f)] public float sweepWidth = 0.16f;
        [Range(0.0f, 2.0f)] public float sweepIntensity = 0.9f;
        [Range(1.0f, 32.0f)] public float prismScale = 14.0f;
        [Range(0.0f, 1.0f)] public float prismStrength = 0.68f;
        [Range(0.0f, 1.0f)] public float foilGrain = 0.26f;
        [Range(0.5f, 6.0f)] public float edgeSample = 2.0f;

        private Vector2 controlScroll;

        public void ResetToTerriasDefaults()
        {
            holoColorA = new Color(1.0f, 0.78f, 0.32f, 1.0f);
            holoColorB = new Color(0.42f, 0.92f, 1.0f, 1.0f);
            holoColorC = new Color(1.0f, 0.42f, 0.86f, 1.0f);
            flowSpeed = 0.36f;
            flowScale = 1.65f;
            noiseScale = 4.8f;
            distortion = 0.018f;
            effectIntensity = 0.72f;
            qualityScale = 1.0f;
            edgeGlow = 0.22f;
            sweepFrequency = 5.6f;
            sweepWidth = 0.16f;
            sweepIntensity = 0.9f;
            prismScale = 14.0f;
            prismStrength = 0.68f;
            foilGrain = 0.26f;
            edgeSample = 2.0f;
            previewScale = 1.0f;
            animate = true;
            ApplyToMaterial();
        }

        public void ApplyToMaterial()
        {
            if (cardRoot != null)
            {
                cardRoot.localScale = Vector3.one * Mathf.Clamp(previewScale, 0.5f, 2.0f);
            }

            if (frameMaterial == null)
            {
                return;
            }

            if (cardFrame != null)
            {
                cardFrame.material = frameMaterial;
                if (cardFrame.sprite != null)
                {
                    frameMaterial.SetTexture(MainTex, cardFrame.sprite.texture);
                }
            }

            if (flowNoise != null)
            {
                frameMaterial.SetTexture(NoiseTex, flowNoise);
            }

            frameMaterial.SetColor(HoloColorA, holoColorA);
            frameMaterial.SetColor(HoloColorB, holoColorB);
            frameMaterial.SetColor(HoloColorC, holoColorC);
            frameMaterial.SetFloat(FlowSpeedId, animate ? flowSpeed : 0.0f);
            frameMaterial.SetFloat(FlowScaleId, flowScale);
            frameMaterial.SetFloat(NoiseScaleId, noiseScale);
            frameMaterial.SetFloat(DistortionId, distortion);
            frameMaterial.SetFloat(EffectIntensityId, effectIntensity);
            frameMaterial.SetFloat(QualityScaleId, qualityScale);
            frameMaterial.SetFloat(EdgeGlowId, edgeGlow);
            frameMaterial.SetFloat(SweepFrequencyId, sweepFrequency);
            frameMaterial.SetFloat(SweepWidthId, sweepWidth);
            frameMaterial.SetFloat(SweepIntensityId, sweepIntensity);
            frameMaterial.SetFloat(PrismScaleId, prismScale);
            frameMaterial.SetFloat(PrismStrengthId, prismStrength);
            frameMaterial.SetFloat(FoilGrainId, foilGrain);
            frameMaterial.SetFloat(EdgeSampleId, edgeSample);
        }

        [ContextMenu("Export Terrias Frame Effect Profile")]
        public void ExportCurrentProfile()
        {
            var exportDirectory = Path.Combine(Application.dataPath, "TerriasCardFrameDebug", "Export");
            Directory.CreateDirectory(exportDirectory);
            var outputPath = Path.Combine(exportDirectory, "card_frame_foil_profile.json");
            File.WriteAllText(outputPath, BuildProfileJson(), Encoding.UTF8);
#if UNITY_EDITOR
            UnityEditor.AssetDatabase.Refresh();
            Debug.Log("Exported Terrias card frame effect profile: " + outputPath);
#endif
        }

        private void OnEnable()
        {
            ApplyToMaterial();
        }

        private void OnValidate()
        {
            ApplyToMaterial();
        }

        private void Update()
        {
            ApplyToMaterial();
        }

        private void OnGUI()
        {
            if (!showRuntimeControls)
            {
                return;
            }

            GUILayout.BeginArea(new Rect(16, 16, 330, Screen.height - 32), GUI.skin.window);
            controlScroll = GUILayout.BeginScrollView(controlScroll);
            GUILayout.Label("Terrias Card Frame Foil");
            animate = GUILayout.Toggle(animate, "Animate");
            previewScale = Slider("Preview Scale", previewScale, 0.5f, 2.0f);
            flowSpeed = Slider("Flow Speed", flowSpeed, 0.0f, 2.0f);
            flowScale = Slider("Flow Scale", flowScale, 0.1f, 6.0f);
            noiseScale = Slider("Noise Scale", noiseScale, 0.1f, 12.0f);
            distortion = Slider("Distortion", distortion, 0.0f, 0.08f);
            effectIntensity = Slider("Intensity", effectIntensity, 0.0f, 2.0f);
            qualityScale = Slider("Quality", qualityScale, 0.0f, 1.0f);
            edgeGlow = Slider("Edge Glow", edgeGlow, 0.0f, 1.0f);
            sweepFrequency = Slider("Sweep Frequency", sweepFrequency, 0.1f, 16.0f);
            sweepWidth = Slider("Sweep Width", sweepWidth, 0.01f, 1.0f);
            sweepIntensity = Slider("Sweep Intensity", sweepIntensity, 0.0f, 2.0f);
            prismScale = Slider("Prism Scale", prismScale, 1.0f, 32.0f);
            prismStrength = Slider("Prism Strength", prismStrength, 0.0f, 1.0f);
            foilGrain = Slider("Foil Grain", foilGrain, 0.0f, 1.0f);
            edgeSample = Slider("Edge Sample", edgeSample, 0.5f, 6.0f);

            GUILayout.Space(8);
            if (GUILayout.Button("Reset Defaults"))
            {
                ResetToTerriasDefaults();
            }

            if (GUILayout.Button("Export Profile"))
            {
                ExportCurrentProfile();
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();

            if (GUI.changed)
            {
                ApplyToMaterial();
            }
        }

        private static float Slider(string label, float value, float min, float max)
        {
            GUILayout.Label(label + ": " + value.ToString("0.###", CultureInfo.InvariantCulture));
            return GUILayout.HorizontalSlider(value, min, max);
        }

        private string BuildProfileJson()
        {
            var builder = new StringBuilder();
            builder.AppendLine("{");
            builder.AppendLine("  \"profileId\": \"terrias.card_frame_effect.foil_holo.debug\",");
            builder.AppendLine("  \"shaderName\": \"Terrias/CardFrameHoloFlow\",");
            builder.AppendLine("  \"intendedTarget\": \"card-frame\",");
            builder.AppendLine("  \"notes\": \"Effect parameters are tuned on the card frame alpha mask only. Move these values into Terrias visual registry/material defaults when integrating.\",");
            builder.AppendLine("  \"floats\": {");
            AppendFloat(builder, "_TerriasFlowSpeed", flowSpeed, true);
            AppendFloat(builder, "_TerriasFlowScale", flowScale, true);
            AppendFloat(builder, "_TerriasNoiseScale", noiseScale, true);
            AppendFloat(builder, "_TerriasDistortion", distortion, true);
            AppendFloat(builder, "_TerriasEffectIntensity", effectIntensity, true);
            AppendFloat(builder, "_TerriasQualityScale", qualityScale, true);
            AppendFloat(builder, "_TerriasEdgeGlow", edgeGlow, true);
            AppendFloat(builder, "_TerriasSweepFrequency", sweepFrequency, true);
            AppendFloat(builder, "_TerriasSweepWidth", sweepWidth, true);
            AppendFloat(builder, "_TerriasSweepIntensity", sweepIntensity, true);
            AppendFloat(builder, "_TerriasPrismScale", prismScale, true);
            AppendFloat(builder, "_TerriasPrismStrength", prismStrength, true);
            AppendFloat(builder, "_TerriasFoilGrain", foilGrain, true);
            AppendFloat(builder, "_TerriasEdgeSample", edgeSample, false);
            builder.AppendLine("  },");
            builder.AppendLine("  \"colors\": {");
            AppendColor(builder, "_TerriasHoloColorA", holoColorA, true);
            AppendColor(builder, "_TerriasHoloColorB", holoColorB, true);
            AppendColor(builder, "_TerriasHoloColorC", holoColorC, false);
            builder.AppendLine("  }");
            builder.AppendLine("}");
            return builder.ToString();
        }

        private static void AppendFloat(StringBuilder builder, string name, float value, bool comma)
        {
            builder.Append("    \"");
            builder.Append(name);
            builder.Append("\": ");
            builder.Append(value.ToString("0.######", CultureInfo.InvariantCulture));
            builder.AppendLine(comma ? "," : "");
        }

        private static void AppendColor(StringBuilder builder, string name, Color color, bool comma)
        {
            builder.Append("    \"");
            builder.Append(name);
            builder.Append("\": \"#");
            builder.Append(ColorUtility.ToHtmlStringRGBA(color));
            builder.AppendLine(comma ? "\"," : "\"");
        }
    }
}
